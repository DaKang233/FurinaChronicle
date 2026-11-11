// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaImportPlanningTests
{
    private static readonly Guid ArchiveAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ArchiveBId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Seeded = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ModeStateDefaultsToConservativeAndExposesRequiredWording()
    {
        var mode = new GachaImportModeState();
        var changed = 0;
        mode.Changed += (_, _) => changed++;

        Assert.Equal(GachaImportMode.Incremental, mode.Snapshot());
        Assert.False(mode.IsFull);
        Assert.Equal("仅补充", mode.Label);
        Assert.Contains("不删除来源缺失的本地记录", mode.Description);
        Assert.Equal(0, changed);

        mode.Toggle();

        Assert.Equal(GachaImportMode.Full, mode.Snapshot());
        Assert.True(mode.IsFull);
        Assert.Equal("全量导入：覆盖同记录", mode.Label);
        Assert.Equal(
            "只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。",
            mode.Description);
        Assert.Equal(1, changed);

        mode.SetMode(GachaImportMode.Incremental);
        Assert.Equal("仅补充", mode.Label);
        Assert.Throws<ArgumentOutOfRangeException>(() => mode.SetMode((GachaImportMode)9));
    }

    [Fact]
    public async Task PlanSnapshotsModeAndTargetWhenTheFlowStarts()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2)));

        var mode = new GachaImportModeState();
        mode.Toggle();
        GachaImportMode snapshot = mode.Snapshot();
        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            snapshot);

        Assert.True(planned.Succeeded);
        GachaImportPlan plan = planned.Plan!;
        Assert.True(plan.CanExecute);
        Assert.Equal(GachaImportMode.Full, plan.Mode);
        Assert.Contains("全量导入：覆盖同记录", plan.ConfirmationText);

        // A later page toggle must not rewrite the captured mode or the confirmed plan.
        mode.Toggle();
        Assert.Equal(GachaImportMode.Incremental, mode.Snapshot());
        Assert.Equal(GachaImportMode.Full, plan.Mode);

        importer.Result = new GachaImportResult(2, 2, 0, 0, 0, 0);
        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);

        Assert.True(execution.IsSuccess);
        Assert.Equal(GachaImportMode.Full, Assert.Single(importer.Calls).Mode);
        Assert.Equal(ArchiveAId, importer.Calls[0].ArchiveId);
        Assert.Null(importer.Calls[0].AccountId);
    }

    [Fact]
    public async Task CurrentAccountPreviewBlocksDifferentUidWithoutWriting()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        GameAccount target = Account(ArchiveAId, "100000001", "A 账号");
        planning.SetAccounts(ArchiveAId, target);
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000002", 2)));

        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.CurrentAccount, ArchiveAId) { GameAccountId = target.Id },
            GachaImportMode.Incremental);

        Assert.True(planned.Succeeded);
        GachaImportPlan plan = planned.Plan!;
        Assert.False(plan.CanExecute);
        Assert.Contains("UID", plan.BlockMessage);
        Assert.Contains("100000002", plan.BlockMessage);
        Assert.Contains("100000001", plan.BlockMessage);
        Assert.Contains("未写入任何数据", plan.BlockMessage);

        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);
        Assert.Equal(GachaImportExecutionStatus.Rejected, execution.Status);
        Assert.Empty(importer.Calls);
    }

    [Fact]
    public async Task CurrentAccountPreviewReportsOtherUidsAsIgnored()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        GameAccount target = Account(ArchiveAId, "100000001", "A 账号");
        planning.SetAccounts(ArchiveAId, target);
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2), SourceAccount("100000002", 3)));

        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.CurrentAccount, ArchiveAId) { GameAccountId = target.Id },
            GachaImportMode.Incremental);

        GachaImportPlan plan = planned.Plan!;
        Assert.True(plan.CanExecute);
        Assert.Equal(2, plan.ImportableRecordCount);
        Assert.Equal(3, plan.IgnoredRecordCount);
        Assert.Contains("将忽略 3 条", plan.ConfirmationText);
        Assert.Contains("与目标账号身份一致", plan.ConfirmationText);
        Assert.Contains("A 账号", plan.Target.AccountIdentity);
        Assert.Contains("UID 100000001", plan.Target.AccountIdentity);
        Assert.Contains("官服", plan.Target.AccountIdentity);

        importer.Result = new GachaImportResult(5, 2, 0, 0, 3, 0);
        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);

        Assert.True(execution.IsSuccess);
        Assert.Equal(target.Id, Assert.Single(importer.Calls).AccountId);
        Assert.Contains("忽略 3", execution.Message);
    }

    [Fact]
    public async Task ArchivePreviewMatchesOnlyTheTargetArchiveAccountCopies()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"), Archive(ArchiveBId, "档案 B"));
        planning.SetAccounts(
            ArchiveAId,
            Account(ArchiveAId, "100000001", "A 账号"),
            Account(ArchiveAId, "100000003", "A 空账号"));
        planning.SetAccounts(
            ArchiveBId,
            Account(ArchiveBId, "100000001", "B 同名副本"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(
                SourceAccount("100000001", 2),
                SourceAccount("100000002", 1)));

        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental);

        GachaImportPlan plan = planned.Plan!;
        Assert.True(plan.CanExecute);
        Assert.Equal(3, plan.ImportableRecordCount);
        GachaImportSourceAccountPreview existing = Assert.Single(
            plan.Accounts,
            account => account.Uid == "100000001");
        Assert.True(existing.ExistingAccount);
        Assert.False(existing.WillCreateAccount);
        Assert.Contains("A 账号", existing.ExistingAccountLabel);
        Assert.DoesNotContain("B 同名副本", existing.ExistingAccountLabel);
        GachaImportSourceAccountPreview missing = Assert.Single(
            plan.Accounts,
            account => account.Uid == "100000002");
        Assert.True(missing.WillCreateAccount);
        Assert.Contains("将新建", plan.ConfirmationText);
        Assert.Contains("档案 A", plan.ConfirmationText);

        importer.Result = new GachaImportResult(3, 3, 0, 0, 0, 1);
        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);

        Assert.True(execution.IsSuccess);
        FakeImporter.Call call = Assert.Single(importer.Calls);
        Assert.Equal(ArchiveAId, call.ArchiveId);
        Assert.Null(call.AccountId);
    }

    [Fact]
    public async Task PreviewShowsFileNameVersionSourceUidsCountsAndModeExplanation()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter _, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2), SourceAccount("100000002", 1)),
            header: """{"info":{"uigf_version":"v3.0"}}""",
            fileName: "fixture-v3.json");

        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental);

        GachaImportPlan plan = planned.Plan!;
        Assert.Equal("v3.0", plan.Source.FormatVersion);
        Assert.Equal("UIGF 3.0（兼容读取）", plan.Source.FormatLabel);
        string text = plan.ConfirmationText;
        Assert.Contains("fixture-v3.json", text);
        Assert.Contains("UIGF 3.0（兼容读取）", text);
        Assert.Contains("档案 A", text);
        Assert.Contains("当前档案全部账号", text);
        Assert.Contains("UID 100000001", text);
        Assert.Contains("UID 100000002", text);
        Assert.Contains("可提交 2 条", text);
        Assert.Contains("可提交 1 条", text);
        Assert.Contains("其中可提交 3 条", text);
        Assert.Contains("模式：仅补充", text);
        Assert.Contains("不删除来源缺失的本地记录", text);
        Assert.Contains("撤销说明", text);
        Assert.DoesNotContain("全量导入：覆盖同记录", text);
    }

    [Fact]
    public async Task InvalidUnsupportedOrForeignFilesNeverReachTheReaderOrImporter()
    {
        foreach ((string header, string expected) in new[]
        {
            ("""{"info": {"version": "v4.2" """, "有效的 JSON"),
            ("""{"info":{"srgf_version":"v1.0"}}""", "SRGF"),
            ("""{"info":{"version":"v9.9"}}""", "不支持的 UIGF 版本"),
            ("""{"info":{}}""", "无法识别 UIGF 版本")
        })
        {
            (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource _) = Create();
            GachaImportSourceReadResult read = await coordinator.ReadSourceAsync(Source(header));

            Assert.False(read.Succeeded);
            Assert.Contains(expected, read.FailureMessage);
            Assert.Equal(0, reader.Calls);
            Assert.Empty(importer.Calls);
        }
    }

    [Fact]
    public async Task CancelledExecutionWritesNothing()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2)));
        GachaImportPlan plan = (await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental)).Plan!;

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.ExecuteAsync(plan, cancellation.Token));
        Assert.Empty(importer.Calls);
    }

    [Fact]
    public async Task StaleTargetInvalidatesThePreviewAndWritesNothing()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        GameAccount target = Account(ArchiveAId, "100000001", "A 账号");
        planning.SetAccounts(ArchiveAId, target);
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2)));
        GachaImportPlan plan = (await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.CurrentAccount, ArchiveAId) { GameAccountId = target.Id },
            GachaImportMode.Incremental)).Plan!;

        planning.SetAccounts(ArchiveAId);

        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);

        Assert.Equal(GachaImportExecutionStatus.Rejected, execution.Status);
        Assert.Contains("未写入任何数据", execution.Message);
        Assert.Empty(importer.Calls);
    }

    [Fact]
    public async Task RemovedArchiveBlocksPlanningWithChineseExplanation()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2)));
        planning.SetArchives();

        GachaImportPlanResult planned = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("档案已不存在", planned.FailureMessage);
        Assert.Contains("未写入任何数据", planned.FailureMessage);
        Assert.Empty(importer.Calls);
    }

    [Fact]
    public async Task UnparseableAccountIsSkippedInArchiveScopeButBlocksTheAccountScope()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        var readResult = new GachaReadResult(
            new GachaSourceInfo(1787500000, "Tests", "1.0.0", "v4.2"),
            [SourceAccount("100000001", 2), SourceAccount("100000002", 2)],
            [new GachaReadError("100000002", 1, "invalid_time", "bad time")],
            4);
        GachaImportSourceSummary source = await ReadSummaryAsync(coordinator, reader, readResult);

        GachaImportPlanResult archivePlan = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental);
        Assert.True(archivePlan.Plan!.CanExecute);
        Assert.Equal(2, archivePlan.Plan.ImportableRecordCount);
        Assert.Equal(2, archivePlan.Plan.SkippedRecordCount);
        GachaImportSourceAccountPreview skipped = Assert.Single(
            archivePlan.Plan.Accounts,
            account => account.Uid == "100000002");
        Assert.True(skipped.Skipped);
        Assert.Contains("整账号跳过", archivePlan.Plan.ConfirmationText);

        importer.Result = new GachaImportResult(4, 2, 0, 2, 0, 0);
        Assert.True((await coordinator.ExecuteAsync(archivePlan.Plan)).IsSuccess);

        GameAccount blockedTarget = Account(ArchiveAId, "100000002", "B 账号");
        planning.SetAccounts(ArchiveAId, blockedTarget);
        GachaImportPlanResult accountPlan = await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.CurrentAccount, ArchiveAId) { GameAccountId = blockedTarget.Id },
            GachaImportMode.Incremental);
        Assert.False(accountPlan.Plan!.CanExecute);
        Assert.Contains("解析问题", accountPlan.Plan.BlockMessage);
    }

    [Fact]
    public async Task SuccessfulExecutionReportsSavedFactsSeparatelyFromDisplayRefresh()
    {
        (GachaImportCoordinator coordinator, FakeReader reader, FakeImporter importer, FakePlanningSource planning) = Create();
        planning.SetArchives(Archive(ArchiveAId, "档案 A"));
        planning.SetAccounts(ArchiveAId, Account(ArchiveAId, "100000001", "A 账号"));
        GachaImportSourceSummary source = await ReadSummaryAsync(
            coordinator,
            reader,
            ReadResult(SourceAccount("100000001", 2)));
        GachaImportPlan plan = (await coordinator.PlanAsync(
            source,
            new(GachaImportTargetScope.ArchiveAccounts, ArchiveAId),
            GachaImportMode.Incremental)).Plan!;
        importer.Result = new GachaImportResult(2, 1, 1, 0, 0, 0, 1);

        GachaImportExecution execution = await coordinator.ExecuteAsync(plan);

        Assert.True(execution.IsSuccess);
        Assert.False(execution.WasRejected);
        Assert.NotNull(execution.Result);
        Assert.StartsWith("已保存的事实：", execution.Message);
        Assert.Contains("总数 2 条", execution.Message);
        Assert.Contains("新增 1", execution.Message);
        Assert.Contains("重复 1", execution.Message);
        Assert.Contains("无效 0", execution.Message);
        Assert.Contains("忽略 0", execution.Message);
        Assert.Contains("新建账号 0", execution.Message);
        Assert.Contains("覆盖修订 1", execution.Message);
        Assert.Contains("模式：仅补充", execution.Message);
    }

    private static (
        GachaImportCoordinator Coordinator,
        FakeReader Reader,
        FakeImporter Importer,
        FakePlanningSource Planning) Create()
    {
        var reader = new FakeReader();
        var importer = new FakeImporter();
        var planning = new FakePlanningSource();
        return (new GachaImportCoordinator(reader, importer, planning), reader, importer, planning);
    }

    private static async Task<GachaImportSourceSummary> ReadSummaryAsync(
        GachaImportCoordinator coordinator,
        FakeReader reader,
        GachaReadResult result,
        string header = """{"info":{"version":"v4.2"}}""",
        string fileName = "fixture.json")
    {
        reader.Result = result;
        GachaImportSourceReadResult read = await coordinator.ReadSourceAsync(Source(header, fileName));
        Assert.True(read.Succeeded, read.FailureMessage);
        return read.Summary!;
    }

    private static GachaImportSource Source(string json, string fileName = "fixture.json") =>
        new(
            fileName,
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(json))));

    private static GachaReadResult ReadResult(params GachaSourceAccount[] accounts) =>
        new(
            new GachaSourceInfo(1787500000, "Tests", "1.0.0", "v4.2"),
            accounts,
            [],
            accounts.Sum(account => account.Records.Count));

    private static GachaSourceAccount SourceAccount(string uid, int count) =>
        new(
            uid,
            8,
            "zh-cn",
            Enumerable.Range(1, count)
                .Select(index => new GachaSourceRecord(
                    $"{uid}{index:000}",
                    "10000089",
                    "301",
                    "301",
                    new DateTimeOffset(2026, 11, 1, 12, index, 0, TimeSpan.FromHours(8)),
                    "合成物品",
                    "角色",
                    5,
                    1))
                .ToArray());

    private static PlayerArchive Archive(Guid id, string name) => new(id, name, Seeded, Seeded);

    private static GameAccount Account(Guid archiveId, string uid, string name)
    {
        GameRoleNaturalIdentity identity = GenshinGameRoleIdentity.Create(uid, GameServerRegion.ChinaOfficial);
        return new GameAccount(
            Guid.NewGuid(),
            archiveId,
            uid,
            GameServerRegion.ChinaOfficial,
            name,
            IsPlaceholder: false,
            Seeded,
            Seeded,
            new GameRoleIdentity(GameRoleIdentityId.FromNaturalIdentity(identity), identity));
    }

    private sealed class FakeReader : IGachaImportReader
    {
        public GachaReadResult Result { get; set; } = new(
            new GachaSourceInfo(1787500000, "Tests", "1.0.0", "v4.2"),
            [],
            [],
            0);

        public int Calls { get; private set; }

        public Task<GachaReadResult> ReadAsync(Stream source, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeImporter : IGachaUigfImporter
    {
        public sealed record Call(Guid ArchiveId, Guid? AccountId, GachaImportMode Mode);

        public List<Call> Calls { get; } = [];

        public GachaImportResult Result { get; set; } = new(0, 0, 0, 0, 0, 0);

        public Task<GachaImportResult> ExecuteAsync(
            Stream source,
            Guid playerArchiveId,
            Guid? targetGameAccountId,
            GachaImportMode mode,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new(playerArchiveId, targetGameAccountId, mode));
            return Task.FromResult(Result);
        }
    }

    private sealed class FakePlanningSource : IGachaImportPlanningSource
    {
        private readonly Dictionary<Guid, List<GameAccount>> accounts = [];

        public IReadOnlyList<PlayerArchive> Archives { get; private set; } = [];

        public void SetArchives(params PlayerArchive[] archives) => Archives = archives;

        public void SetAccounts(Guid archiveId, params GameAccount[] archiveAccounts)
        {
            accounts[archiveId] = [.. archiveAccounts];
        }

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Archives);
        }

        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GameAccount> result = accounts.TryGetValue(playerArchiveId, out List<GameAccount>? value)
                ? value.ToArray()
                : [];
            return Task.FromResult(result);
        }
    }
}

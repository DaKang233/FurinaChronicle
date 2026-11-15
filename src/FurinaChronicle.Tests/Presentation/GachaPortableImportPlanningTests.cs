// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaPortableImportPlanningTests
{
    private static readonly Guid ArchiveId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SourceAccountReference =
        Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly GameRoleNaturalIdentity Identity =
        GenshinGameRoleIdentity.Create("100000001", GameServerRegion.ChinaOfficial);
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task PrepareStagesIndexesAndPlansWithFrozenTargetAndMode()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(mode: GachaPortableApplyMode.ReplaceExisting, add: 2);

        GachaPortableImportPrepareResult result = await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.CurrentArchive(ArchiveId),
            GachaImportMode.Full);

        Assert.True(result.Succeeded, result.FailureMessage);
        GachaPortableImportSession session = result.Session!;
        Assert.Equal(GachaImportMode.Full, session.Mode);
        Assert.Equal(GachaPortableImportTargetMode.CurrentArchive, session.Target.Mode);
        Assert.Equal(ArchiveId, session.Target.ArchiveId);
        Assert.Equal(GachaPortableApplyMode.ReplaceExisting, harness.Planner.LastRequest!.Mode);
        Assert.Equal(ArchiveId, harness.Planner.LastRequest.TargetArchiveId);
        Assert.False(harness.Planner.LastRequest.CreateNewArchive);
        string text = session.ConfirmationText;
        Assert.Contains("Furina Gacha Portable v1.0", text);
        Assert.Contains("01 合成档案", text);
        Assert.Contains("全量导入：覆盖同记录", text);
        Assert.Contains("只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。", text);
        Assert.Contains("不是完整应用备份", text);
        Assert.Contains("新增 2", text);

        await session.DisposeAsync();
        Assert.True(harness.Package.Disposed);
    }

    [Fact]
    public async Task CancelledPrepareWritesNothing()
    {
        var harness = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Coordinator.PrepareAsync(
                Source(),
                GachaPortableImportTarget.Automatic,
                GachaImportMode.Incremental,
                cancellation.Token));
        Assert.Empty(harness.Applier.Requests);
    }

    [Fact]
    public async Task PrepareRejectsInvalidPackageWithoutSessionOrApply()
    {
        var harness = new Harness();
        harness.Factory.OpenException = new GachaPortableException(
            GachaPortableErrorCode.UnsupportedVersion,
            "unsupported internal message");

        GachaPortableImportPrepareResult result = await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.Automatic,
            GachaImportMode.Incremental);

        Assert.False(result.Succeeded);
        Assert.Null(result.Session);
        Assert.Contains("版本不受支持", result.FailureMessage);
        Assert.DoesNotContain("unsupported internal message", result.FailureMessage);
        Assert.True(harness.Stager.LastStaged!.Disposed);
        Assert.Empty(harness.Applier.Requests);
    }

    [Fact]
    public async Task NewArchiveProposalIdsAreRegeneratedButPlanStillApplies()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(
            add: 3,
            createAccounts: 1,
            reuseAccounts: 0,
            archiveKind: GachaPortableArchivePlanKind.CreateNew);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.NewArchive,
            GachaImportMode.Incremental)).Session!;
        Assert.Equal(GachaPortableArchivePlanKind.CreateNew, session.Plan.Archive.Kind);
        Assert.Contains("新建档案", session.ConfirmationText);
        Assert.True(harness.Planner.LastRequest!.CreateNewArchive);

        // A real replan regenerates the proposed archive/account GUIDs; the confirmed preview
        // must still apply because those proposals are not stable target identity.
        harness.Planner.Plan = MakePlan(
            add: 3,
            createAccounts: 1,
            reuseAccounts: 0,
            archiveKind: GachaPortableArchivePlanKind.CreateNew);
        GachaPortableApplyOutcome outcome = await harness.Coordinator.ApplyAsync(session);

        Assert.True(outcome.IsSuccess, outcome.Message);
        Assert.Single(harness.Applier.Requests);
        Assert.NotNull(harness.Applier.Requests[0].Plan.Archive.ProposedArchiveId);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ApplyRefusesWhenReplannedPreviewDiffers()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(add: 1);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.CurrentArchive(ArchiveId),
            GachaImportMode.Incremental)).Session!;

        harness.Planner.Plan = MakePlan(add: 2);
        GachaPortableApplyOutcome outcome = await harness.Coordinator.ApplyAsync(session);

        Assert.Equal(GachaPortableApplyOutcomeKind.Conflict, outcome.Kind);
        Assert.Contains("预览已失效", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.Empty(harness.Applier.Requests);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ApplyRefusesWhenNewConflictsAppear()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(add: 1);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.Automatic,
            GachaImportMode.Incremental)).Session!;

        harness.Planner.Plan = MakePlan(
            add: 0,
            conflict: 1,
            conflicts: [new GachaPortableRecordConflict("100001", ["item_name"])]);
        GachaPortableApplyOutcome outcome = await harness.Coordinator.ApplyAsync(session);

        Assert.Equal(GachaPortableApplyOutcomeKind.Conflict, outcome.Kind);
        Assert.Contains("仅补充模式不会覆盖", outcome.Message);
        Assert.Contains("100001", outcome.Message);
        Assert.Empty(harness.Applier.Requests);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ReplanUpdatesSessionTargetAndPreview()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(add: 1);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.Automatic,
            GachaImportMode.Incremental)).Session!;

        harness.Planner.Plan = MakePlan(add: 3);
        GachaPortableReplanResult replanned = await harness.Coordinator.ReplanAsync(
            session,
            GachaPortableImportTarget.CurrentArchive(ArchiveId));

        Assert.True(replanned.Succeeded, replanned.FailureMessage);
        Assert.Equal(GachaPortableImportTargetMode.CurrentArchive, session.Target.Mode);
        Assert.Contains("新增 3", session.ConfirmationText);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ApplyRequiresReintroductionThenRetriesWithConfirmations()
    {
        var harness = new Harness();
        GachaPortableImportPlan plan = MakePlan(add: 1);
        harness.Planner.Plan = plan;
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.CurrentArchive(ArchiveId),
            GachaImportMode.Incremental)).Session!;
        var reference = new GachaFactReference(Guid.NewGuid(), "100001");
        harness.Applier.Results.Enqueue(new GachaPortableApplyResult(
            ChangeExecutionStatus.Suppressed,
            null,
            ArchiveId,
            [],
            0,
            0,
            0,
            ReintroductionWarnings: [new TombstoneReintroductionWarning("key", 3, reference, ArchiveId)]));

        GachaPortableApplyOutcome first = await harness.Coordinator.ApplyAsync(session);

        Assert.Equal(GachaPortableApplyOutcomeKind.RequiresReintroduction, first.Kind);
        Assert.True(first.RequiresUserConfirmation);
        Assert.Single(first.ReintroductionWarnings!);
        Assert.False(session.IsApplied);

        harness.Applier.Results.Enqueue(new GachaPortableApplyResult(
            ChangeExecutionStatus.Applied, null, ArchiveId, [], 1, 0, 0));
        GachaPortableApplyOutcome second = await harness.Coordinator.ApplyAsync(
            session,
            [new TombstoneReintroductionConfirmation("key", 3, reference)],
            cleanupConfirmationId: null);

        Assert.True(second.IsSuccess);
        Assert.True(session.IsApplied);
        Assert.Equal(2, harness.Applier.Requests.Count);
        Assert.Single(harness.Applier.Requests[1].ReintroductionConfirmations!);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ApplyRequiresCleanupThenRetriesWithConfirmationId()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(add: 1);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.Automatic,
            GachaImportMode.Incremental)).Session!;
        Guid cleanupId = Guid.NewGuid();
        harness.Applier.Results.Enqueue(new GachaPortableApplyResult(
            ChangeExecutionStatus.NeedsConfirmation,
            null,
            ArchiveId,
            [],
            0,
            0,
            0,
            CleanupPlan: new HistoryCleanupPlan(cleanupId, 10, 1, 100, [])));

        GachaPortableApplyOutcome first = await harness.Coordinator.ApplyAsync(session);
        Assert.Equal(GachaPortableApplyOutcomeKind.RequiresCleanup, first.Kind);
        Assert.NotNull(first.CleanupPlan);

        harness.Applier.Results.Enqueue(new GachaPortableApplyResult(
            ChangeExecutionStatus.Applied, null, ArchiveId, [], 1, 0, 0));
        GachaPortableApplyOutcome second = await harness.Coordinator.ApplyAsync(
            session,
            [],
            cleanupId);

        Assert.True(second.IsSuccess);
        Assert.Equal(cleanupId, harness.Applier.Requests[1].CleanupConfirmationId);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task CancelledApplyWritesNothing()
    {
        var harness = new Harness();
        harness.Planner.Plan = MakePlan(add: 1);
        GachaPortableImportSession session = (await harness.Coordinator.PrepareAsync(
            Source(),
            GachaPortableImportTarget.Automatic,
            GachaImportMode.Incremental)).Session!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Coordinator.ApplyAsync(session, cancellation.Token));
        Assert.Empty(harness.Applier.Requests);
        await session.DisposeAsync();
    }

    [Fact]
    public async Task ExportPlannerDescribesScopeAndDataBoundary()
    {
        var planning = new FakePlanningSource();
        PlayerArchive archive = Archive();
        planning.SetArchives(archive);
        GameAccount account = Account(archive.Id);
        planning.SetAccounts(archive.Id, account);
        var planner = new GachaPortableExportPlanner(planning);

        GachaPortableExportPlanResult result = await planner.PlanAsync(
            archive.Id,
            [account.Id]);

        Assert.True(result.Succeeded, result.FailureMessage);
        GachaPortableExportPlan plan = result.Plan!;
        Assert.True(plan.CanExecute);
        Assert.Equal(archive.Id, plan.ArchiveId);
        Assert.Equal(new[] { account.Id }, plan.AccountIds);
        Assert.Contains("抽卡数据集，不是完整应用备份", plan.ConfirmationText);
        Assert.Contains("Furina Gacha Portable v1.0", plan.ConfirmationText);
        Assert.Contains("A 账号", plan.ConfirmationText);
        Assert.Contains("UID 100000001", plan.ConfirmationText);
    }

    [Fact]
    public async Task ExportPlannerRejectsEmptyOrStaleScope()
    {
        var planning = new FakePlanningSource();
        PlayerArchive archive = Archive();
        planning.SetArchives(archive);
        planning.SetAccounts(archive.Id, Account(archive.Id));
        var planner = new GachaPortableExportPlanner(planning);

        Assert.False((await planner.PlanAsync(archive.Id, [])).Succeeded);
        Assert.False((await planner.PlanAsync(Guid.NewGuid(), [Guid.NewGuid()])).Succeeded);
        Assert.Contains(
            "失",
            (await planner.PlanAsync(archive.Id, [Guid.NewGuid()])).FailureMessage);
    }

    private static GachaImportSource Source() =>
        new("portable.zip", _ => Task.FromResult<Stream>(
            new MemoryStream(Encoding.UTF8.GetBytes("synthetic-portable"))));

    private static PlayerArchive Archive() => new(ArchiveId, "01 合成档案", FixedTime, FixedTime);

    private static GameAccount Account(Guid archiveId) => new(
        Guid.NewGuid(),
        archiveId,
        "100000001",
        GameServerRegion.ChinaOfficial,
        "A 账号",
        IsPlaceholder: false,
        FixedTime,
        FixedTime,
        new GameRoleIdentity(GameRoleIdentityId.FromNaturalIdentity(Identity), Identity));

    private static GachaPortableImportPlan MakePlan(
        GachaPortableApplyMode mode = GachaPortableApplyMode.AddOnly,
        int add = 1,
        int replace = 0,
        int skip = 0,
        int conflict = 0,
        int createAccounts = 0,
        int reuseAccounts = 1,
        GachaPortableArchivePlanKind archiveKind = GachaPortableArchivePlanKind.MapExisting,
        IReadOnlyList<GachaPortableRecordConflict>? conflicts = null,
        string fingerprint = "0123456789abcdef")
    {
        var archive = new GachaPortableArchivePlan(
            archiveKind,
            archiveKind == GachaPortableArchivePlanKind.MapExisting ? ArchiveId : null,
            archiveKind == GachaPortableArchivePlanKind.CreateNew ? Guid.NewGuid() : null,
            "01 合成档案",
            [],
            FixedTime);
        var account = new GachaPortableAccountImportPlan(
            SourceAccountReference,
            archiveKind == GachaPortableArchivePlanKind.MapExisting ? Guid.NewGuid() : null,
            archiveKind == GachaPortableArchivePlanKind.CreateNew ? Guid.NewGuid() : null,
            Identity,
            new GachaPortableIdentityPlan(
                GachaPortableIdentityPlanKind.PreserveSource,
                GameRoleIdentityId.FromNaturalIdentity(Identity),
                GameRoleIdentityId.FromNaturalIdentity(Identity),
                null),
            add,
            replace,
            skip,
            conflict,
            conflicts ?? [],
            [],
            new Dictionary<DataOrigin, int> { [DataOrigin.FurinaImport] = add + replace + skip },
            null,
            10);
        return new GachaPortableImportPlan(
            archive,
            [account],
            new GachaPortableImportContext(DataOrigin.FurinaImport, FixedTime, Guid.NewGuid()),
            new GachaPortableImportPreview(
                createAccounts,
                reuseAccounts,
                add,
                replace,
                skip,
                conflict,
                AliasCount: 0),
            mode,
            fingerprint,
            RequireUniqueArchiveNames: true);
    }

    private sealed class Harness
    {
        public FakeStager Stager { get; } = new();
        public FakePackageFactory Factory { get; } = new();
        public FakePlanner Planner { get; } = new();
        public FakeApplier Applier { get; } = new();
        public FakePackage Package { get; } = new();
        public GachaPortableImportCoordinator Coordinator { get; }

        public Harness()
        {
            Factory.Package = Package;
            Coordinator = new(
                Stager,
                Factory,
                Planner,
                Applier,
                TimeProvider.System);
        }
    }

    private sealed class FakeStager : IGachaPortableInputStager
    {
        public FakeStaged? LastStaged { get; private set; }

        public Task<IGachaPortableStagedInput> StageAsync(
            Stream source,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastStaged = new FakeStaged();
            return Task.FromResult<IGachaPortableStagedInput>(LastStaged);
        }
    }

    private sealed class FakeStaged : IGachaPortableStagedInput
    {
        public long Length => 128;

        public string Sha256 => "0123456789abcdef0123456789abcdef";

        public bool Disposed { get; private set; }

        public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3]));

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePackage : IGachaPortableIndexedPackage
    {
        public string FormatVersion => "1.0";

        public DateTimeOffset GeneratedAt => FixedTime;

        public GachaPortableArchive SourceArchive { get; } =
            new(Guid.Parse("44444444-4444-4444-4444-444444444444"), "来源档案");

        public IReadOnlyList<GachaPortableIndexedAccount> Accounts => [];

        public int RecordCount => 3;

        public string PackageFingerprint => "0123456789abcdef";

        public bool Disposed { get; private set; }

        public IReadOnlyList<GachaRecord> ReadRecordPage(
            Guid accountReference,
            int offset,
            int pageSize) => throw new NotSupportedException();

        public void ResetPlanEntries() { }

        public void StorePlanEntries(
            Guid accountReference,
            IReadOnlyList<GachaPortableIndexedPlanEntry> entries) { }

        public IReadOnlyList<GachaPortableIndexedPlanEntry> ReadPlanPage(
            Guid accountReference,
            int offset,
            int pageSize) => [];

        public IReadOnlyList<GachaPortableRecordConflict> ReadConflictPage(
            Guid accountReference,
            int offset,
            int pageSize) => [];

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakePackageFactory : IGachaPortableIndexedPackageFactory
    {
        public FakePackage Package { get; set; } = new();

        public Exception? OpenException { get; set; }

        public Task<IGachaPortableIndexedPackage> OpenAsync(
            IGachaPortableStagedInput stagedInput,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return OpenException is not null
                ? Task.FromException<IGachaPortableIndexedPackage>(OpenException)
                : Task.FromResult<IGachaPortableIndexedPackage>(Package);
        }
    }

    private sealed class FakePlanner : IGachaPortableImportPlanner
    {
        public GachaPortableImportPlan Plan { get; set; } = MakePlan();

        public GachaPortableIndexedImportPlanRequest? LastRequest { get; private set; }

        public Task<GachaPortableImportPlan> ExecuteAsync(
            GachaPortableIndexedImportPlanRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return Task.FromResult(Plan);
        }
    }

    private sealed class FakeApplier : IGachaPortableImportApplier
    {
        public List<GachaPortableIndexedApplyRequest> Requests { get; } = [];

        public Queue<GachaPortableApplyResult> Results { get; } = new();

        public Task<GachaPortableApplyResult> ApplyAsync(
            GachaPortableApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GachaPortableApplyResult> ApplyAsync(
            GachaPortableIndexedApplyRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(Results.Count > 0
                ? Results.Dequeue()
                : new GachaPortableApplyResult(
                    ChangeExecutionStatus.Applied, null, ArchiveId, [], 1, 0, 0));
        }
    }

    private sealed class FakePlanningSource : IGachaImportPlanningSource
    {
        private readonly Dictionary<Guid, List<GameAccount>> accounts = [];

        public IReadOnlyList<PlayerArchive> Archives { get; private set; } = [];

        public void SetArchives(params PlayerArchive[] archives) => Archives = archives;

        public void SetAccounts(Guid archiveId, params GameAccount[] archiveAccounts) =>
            accounts[archiveId] = [.. archiveAccounts];

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Archives);

        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<GameAccount> result = accounts.TryGetValue(
                playerArchiveId,
                out List<GameAccount>? value)
                ? value.ToArray()
                : [];
            return Task.FromResult(result);
        }
    }
}

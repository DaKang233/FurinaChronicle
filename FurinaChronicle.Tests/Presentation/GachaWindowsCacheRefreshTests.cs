// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaWindowsCacheRefreshTests
{
    private static readonly Guid ArchiveId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
    private const string GameFolder = @"D:\Secret\Genshin Impact";

    [Fact]
    public async Task UnsupportedPlatformIsRejectedWithoutCalls()
    {
        var harness = new Harness();
        harness.Gateway.IsSupported = false;
        harness.SetupTarget();

        GachaWindowsCacheRefreshPlanResult planned = await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("仅在 Windows", planned.FailureMessage);
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task MissingPathArchiveOrAccountIsRejectedLocally()
    {
        var harness = new Harness();
        harness.SetupTarget();

        Assert.False((await harness.Coordinator.PlanAsync(
            " ", ArchiveId, AccountId, GachaImportMode.Incremental)).Succeeded);
        Assert.False((await harness.Coordinator.PlanAsync(
            GameFolder, Guid.NewGuid(), AccountId, GachaImportMode.Incremental)).Succeeded);
        Assert.False((await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, Guid.NewGuid(), GachaImportMode.Incremental)).Succeeded);
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task PlanFreezesFullModeAndTargetWithoutRepeatingTheFolderPath()
    {
        var harness = new Harness();
        harness.SetupTarget();

        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Full)).Plan!;

        Assert.Equal(ArchiveId, plan.ArchiveId);
        Assert.Equal(AccountId, plan.TargetAccount.Id);
        Assert.Equal(GachaImportMode.Full, plan.Mode);
        Assert.Equal(GameFolder, plan.GameInstallationPath);
        Assert.Contains("全量导入：覆盖同记录", plan.ConfirmationText);
        Assert.Contains(
            "只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。",
            plan.ConfirmationText);
        Assert.Contains("网页缓存", plan.ConfirmationText);
        Assert.Contains("UID 100000001", plan.ConfirmationText);
        Assert.DoesNotContain(GameFolder, plan.ConfirmationText);
        Assert.Contains("不显示、不保存、不写入日志", plan.ConfirmationText);
    }

    [Fact]
    public async Task MissingCacheFileMapsToChineseWithoutRefreshing()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        harness.Gateway.DiscoverException = new FileNotFoundException("cache", "data_2");

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.False(outcome.Succeeded);
        Assert.Contains("未在所选目录找到原神网页缓存文件", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task CacheWithoutUrlMapsToChinese()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        harness.Gateway.DiscoverException = new InvalidDataException("no url");

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.False(outcome.Succeeded);
        Assert.Contains("没有找到可用的抽卡链接", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task InvalidCachedUrlMapsToChinese()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        harness.Gateway.DiscoverException = new FormatException("authkey");

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.False(outcome.Succeeded);
        Assert.Contains("无效或缺少 authkey", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task UidMismatchDoesNotRefresh()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        harness.Gateway.Identity = new GachaRefreshIdentity("100000002", GameServerRegion.ChinaOfficial);

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.False(outcome.Succeeded);
        Assert.Contains("100000002", outcome.Message);
        Assert.Contains("100000001", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task StaleAccountIsRejectedBeforeReadingTheCache()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        harness.Planning.SetAccounts(ArchiveId);

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.False(outcome.Succeeded);
        Assert.Contains("预览后变化", outcome.Message);
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task SuccessReportsCountsAndUsesFrozenMode()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Full)).Plan!;
        harness.Gateway.Result = new GachaRefreshResult(5, 3, 1, 1, 2, 0, SuppressedCount: 1);

        GachaWindowsCacheRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan);

        Assert.True(outcome.Succeeded);
        Assert.Contains("获取 5 条", outcome.Message);
        Assert.Contains("新增 3 条", outcome.Message);
        Assert.Contains("覆盖校正 1 条", outcome.Message);
        Assert.Contains("保留重复 1 条", outcome.Message);
        Assert.Contains("永久删除抑制 1 条", outcome.Message);
        Assert.Contains("请求 2 页", outcome.Message);
        Assert.Equal(GachaImportMode.Full, harness.Gateway.LastMode);
        Assert.Equal(GameFolder, harness.Gateway.LastPath);
        Assert.Equal(AccountId, harness.Gateway.LastTarget!.Id);
    }

    [Fact]
    public async Task CancelledExecuteReadsNothing()
    {
        var harness = new Harness();
        harness.SetupTarget();
        GachaWindowsCacheRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            GameFolder, ArchiveId, AccountId, GachaImportMode.Incremental)).Plan!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Coordinator.ExecuteAsync(plan, cancellation.Token));
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    private sealed class Harness
    {
        public FakePlanningSource Planning { get; } = new();

        public FakeGateway Gateway { get; } = new();

        public GachaWindowsCacheRefreshCoordinator Coordinator { get; }

        public Harness()
        {
            Coordinator = new(Planning, Gateway);
        }

        public void SetupTarget()
        {
            Planning.SetArchives(new PlayerArchive(ArchiveId, "01 合成档案", FixedTime, FixedTime));
            Planning.SetAccounts(ArchiveId, Account());
        }

        private static GameAccount Account()
        {
            GameRoleNaturalIdentity identity = GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
            return new(
                AccountId,
                ArchiveId,
                "100000001",
                GameServerRegion.ChinaOfficial,
                "A 账号",
                IsPlaceholder: false,
                FixedTime,
                FixedTime,
                new GameRoleIdentity(GameRoleIdentityId.FromNaturalIdentity(identity), identity));
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

    private sealed class FakeGateway : IGachaWindowsCacheRefreshGateway
    {
        public bool IsSupported { get; set; } = true;

        public GachaRefreshIdentity Identity { get; set; } =
            new("100000001", GameServerRegion.ChinaOfficial);

        public GachaRefreshResult Result { get; set; } = new(0, 0, 0, 0, 0, 0);

        public Exception? DiscoverException { get; set; }

        public int DiscoverCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public GameAccount? LastTarget { get; private set; }

        public GachaImportMode? LastMode { get; private set; }

        public string? LastPath { get; private set; }

        public Task<GachaRefreshIdentity> DiscoverIdentityAsync(
            string gameInstallationPath,
            GameServerRegion regionHint,
            CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            LastPath = gameInstallationPath;
            if (DiscoverException is not null)
            {
                return Task.FromException<GachaRefreshIdentity>(DiscoverException);
            }

            return Task.FromResult(Identity);
        }

        public Task<GachaRefreshResult> RefreshAsync(
            GameAccount targetAccount,
            GachaImportMode mode,
            string gameInstallationPath,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            LastTarget = targetAccount;
            LastMode = mode;
            LastPath = gameInstallationPath;
            return Task.FromResult(Result);
        }
    }
}

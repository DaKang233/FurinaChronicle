// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaManualRefreshTests
{
    private const string Url =
        "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
        "?authkey=supersecret&game_biz=hk4e_cn&region=cn_gf01";

    private static readonly Guid ArchiveId = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("http://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=x")]
    [InlineData("https://example.com/gacha_info/api/getGachaLog?authkey=x")]
    [InlineData("https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog")]
    public async Task InvalidUrlIsRejectedLocallyWithoutNetwork(string url)
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());

        GachaManualRefreshPlanResult planned = await harness.Coordinator.PlanAsync(
            url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("未写入任何数据", planned.FailureMessage);
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task PlanFreezesTargetAndNeverEchoesTheUrl()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());

        GachaManualRefreshPlanResult planned = await harness.Coordinator.PlanAsync(
            Url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Full);

        Assert.True(planned.Succeeded, planned.FailureMessage);
        GachaManualRefreshPlan plan = planned.Plan!;
        Assert.Equal(ArchiveId, plan.ArchiveId);
        Assert.Equal(harness.Account.Id, plan.TargetAccount.Id);
        Assert.Equal(GachaImportMode.Full, plan.Mode);
        Assert.Contains("全量导入：覆盖同记录", plan.ConfirmationText);
        Assert.Contains("不显示、不保存", plan.ConfirmationText);
        Assert.Contains("A 账号", plan.ConfirmationText);
        Assert.Contains("UID 100000001", plan.ConfirmationText);
        Assert.DoesNotContain("supersecret", plan.ConfirmationText);
        Assert.DoesNotContain("authkey=", plan.ConfirmationText);
    }

    [Fact]
    public async Task ExecuteRejectsMismatchedUidWithoutRefreshing()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());
        GachaManualRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            Url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Incremental)).Plan!;
        harness.Gateway.Identity = new GachaRefreshIdentity("100000002", GameServerRegion.ChinaOfficial);

        GachaManualRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan, Url);

        Assert.False(outcome.Succeeded);
        Assert.Contains("100000002", outcome.Message);
        Assert.Contains("100000001", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
        Assert.DoesNotContain("supersecret", outcome.Message);
    }

    [Fact]
    public async Task ExecuteMapsNetworkFailureToChineseWithoutLeakingUrl()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());
        GachaManualRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            Url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Incremental)).Plan!;
        harness.Gateway.DiscoverException = new HttpRequestException("internal detail");

        GachaManualRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan, Url);

        Assert.False(outcome.Succeeded);
        Assert.Contains("无法访问米哈游抽卡接口", outcome.Message);
        Assert.Contains("未写入任何数据", outcome.Message);
        Assert.DoesNotContain("internal detail", outcome.Message);
        Assert.DoesNotContain("supersecret", outcome.Message);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    [Fact]
    public async Task SuccessfulRefreshReportsCountsAndUsesFrozenMode()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());
        GachaManualRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            Url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Full)).Plan!;
        harness.Gateway.Result = new GachaRefreshResult(5, 3, 1, 1, 2, 0, SuppressedCount: 1);

        GachaManualRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan, Url);

        Assert.True(outcome.Succeeded);
        Assert.Contains("获取 5 条", outcome.Message);
        Assert.Contains("新增 3 条", outcome.Message);
        Assert.Contains("覆盖校正 1 条", outcome.Message);
        Assert.Contains("保留重复 1 条", outcome.Message);
        Assert.Contains("永久删除抑制 1 条", outcome.Message);
        Assert.Contains("请求 2 页", outcome.Message);
        Assert.Equal(GachaImportMode.Full, harness.Gateway.LastMode);
        Assert.Equal(harness.Account.Id, harness.Gateway.LastTarget!.Id);
        Assert.DoesNotContain("supersecret", outcome.Message);
    }

    [Fact]
    public async Task StaleAccountIsRejectedBeforeNetwork()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Planning.SetAccounts(ArchiveId, Account());
        GachaManualRefreshPlan plan = (await harness.Coordinator.PlanAsync(
            Url,
            ArchiveId,
            harness.Account.Id,
            GachaImportMode.Incremental)).Plan!;
        harness.Planning.SetAccounts(ArchiveId);

        GachaManualRefreshOutcome outcome = await harness.Coordinator.ExecuteAsync(plan, Url);

        Assert.False(outcome.Succeeded);
        Assert.Contains("预览后变化", outcome.Message);
        Assert.Equal(0, harness.Gateway.DiscoverCalls);
        Assert.Equal(0, harness.Gateway.RefreshCalls);
    }

    private static PlayerArchive Archive() => new(ArchiveId, "01 合成档案", FixedTime, FixedTime);

    private static GameAccount Account()
    {
        GameRoleNaturalIdentity identity = GenshinGameRoleIdentity.Create(
            "100000001",
            GameServerRegion.ChinaOfficial);
        return new(
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            ArchiveId,
            "100000001",
            GameServerRegion.ChinaOfficial,
            "A 账号",
            IsPlaceholder: false,
            FixedTime,
            FixedTime,
            new GameRoleIdentity(GameRoleIdentityId.FromNaturalIdentity(identity), identity));
    }

    private sealed class Harness
    {
        public FakePlanningSource Planning { get; } = new();

        public FakeGateway Gateway { get; } = new();

        public GameAccount Account { get; }

        public GachaManualRefreshCoordinator Coordinator { get; }

        public Harness()
        {
            Account = GachaManualRefreshTests.Account();
            Coordinator = new(Planning, Gateway);
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

    private sealed class FakeGateway : IGachaManualRefreshGateway
    {
        public GachaRefreshIdentity Identity { get; set; } =
            new("100000001", GameServerRegion.ChinaOfficial);

        public Exception? DiscoverException { get; set; }

        public Exception? RefreshException { get; set; }

        public GachaRefreshResult Result { get; set; } = new(0, 0, 0, 0, 0, 0);

        public int DiscoverCalls { get; private set; }

        public int RefreshCalls { get; private set; }

        public GameAccount? LastTarget { get; private set; }

        public GachaImportMode? LastMode { get; private set; }

        public Task<GachaRefreshIdentity> DiscoverIdentityAsync(
            string manualUrl,
            CancellationToken cancellationToken = default)
        {
            DiscoverCalls++;
            if (DiscoverException is not null)
            {
                return Task.FromException<GachaRefreshIdentity>(DiscoverException);
            }

            return Task.FromResult(Identity);
        }

        public Task<GachaRefreshResult> RefreshAsync(
            GameAccount targetAccount,
            GachaImportMode mode,
            string manualUrl,
            CancellationToken cancellationToken = default)
        {
            RefreshCalls++;
            LastTarget = targetAccount;
            LastMode = mode;
            return RefreshException is not null
                ? Task.FromException<GachaRefreshResult>(RefreshException)
                : Task.FromResult(Result);
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaTeyvatHelperImportTests
{
    private const string Url =
        "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
        "?authkey=supersecret&game_biz=hk4e_cn&region=cn_gf01";
    private const string Uid = "100000001";

    private static readonly Guid ArchiveId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset FixedTime = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345")]
    public async Task InvalidUidIsRejectedBeforeAnyRequest(string uid)
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());

        GachaTeyvatHelperImportPlanResult planned = await harness.Coordinator.PlanAsync(
            uid,
            Url,
            ArchiveId,
            GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("有效的原神 UID", planned.FailureMessage);
        Assert.Contains("未发起第三方请求", planned.FailureMessage);
        Assert.Equal(0, harness.Gateway.AvailabilityCalls);
        Assert.Equal(0, harness.Gateway.DownloadCalls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://public-operation-hk4e.mihoyo.com/x?authkey=a")]
    [InlineData("https://example.com/x?authkey=a")]
    [InlineData("https://public-operation-hk4e.mihoyo.com/x")]
    public async Task InvalidLinkIsRejectedBeforeAnyRequest(string url)
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());

        GachaTeyvatHelperImportPlanResult planned = await harness.Coordinator.PlanAsync(
            Uid,
            url,
            ArchiveId,
            GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("未发起第三方请求", planned.FailureMessage);
        Assert.Equal(0, harness.Gateway.AvailabilityCalls);
        Assert.Equal(0, harness.Gateway.DownloadCalls);
    }

    [Fact]
    public async Task PlanFreezesModeAndPrivacyWithoutEchoingTheLink()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());

        GachaTeyvatHelperImportPlan plan = (await harness.Coordinator.PlanAsync(
            Uid,
            Url,
            ArchiveId,
            GachaImportMode.Full)).Plan!;

        Assert.Equal(Uid, plan.Uid);
        Assert.Equal(ArchiveId, plan.ArchiveId);
        Assert.Equal(GachaImportMode.Full, plan.Mode);
        Assert.Contains("提瓦特小助手", plan.ConfirmationText);
        Assert.Contains("lelaer.com", plan.ConfirmationText);
        Assert.Contains("全量导入：覆盖同记录", plan.ConfirmationText);
        Assert.Contains(
            "只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。",
            plan.ConfirmationText);
        Assert.Contains(Uid, plan.PrivacyNotice);
        Assert.Contains("不显示、不保存、不写入日志", plan.ConfirmationText);
        Assert.DoesNotContain("supersecret", plan.ConfirmationText);
        Assert.DoesNotContain("authkey=supersecret", plan.ConfirmationText);
        Assert.Equal(0, harness.Gateway.DownloadCalls);
    }

    [Fact]
    public async Task AutomaticPathAvailabilityIsStatedExplicitly()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        harness.Gateway.Availability = new TeyvatHelperImportAvailability(null, false);

        GachaTeyvatHelperImportPlan unavailable = (await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental)).Plan!;
        Assert.False(unavailable.AutomaticImportAvailable);
        Assert.Contains("自动导入：不可用", unavailable.ConfirmationText);
        Assert.Contains("请使用手动方式", unavailable.ConfirmationText);

        harness.Gateway.Availability = new TeyvatHelperImportAvailability(Uid, true);
        GachaTeyvatHelperImportPlan available = (await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental)).Plan!;
        Assert.True(available.AutomaticImportAvailable);
        Assert.Contains("只提供手动方式", available.ConfirmationText);
    }

    [Fact]
    public async Task RemovedArchiveIsRejectedBeforeAnyRequest()
    {
        var harness = new Harness();
        harness.Planning.SetArchives();

        GachaTeyvatHelperImportPlanResult planned = await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental);

        Assert.False(planned.Succeeded);
        Assert.Contains("档案已不存在", planned.FailureMessage);
        Assert.Equal(0, harness.Gateway.AvailabilityCalls);
        Assert.Equal(0, harness.Gateway.DownloadCalls);
    }

    [Fact]
    public async Task PrivacyCancelBeforeDownloadSendsNothing()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        GachaTeyvatHelperImportPlan plan = (await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental)).Plan!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => harness.Coordinator.DownloadAsync(plan, Url, cancellation.Token));
        Assert.Equal(0, harness.Gateway.DownloadCalls);
    }

    [Fact]
    public async Task DownloadFailureMapsToChineseWithoutLeakingTheUrl()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        GachaTeyvatHelperImportPlan plan = (await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental)).Plan!;
        harness.Gateway.DownloadException = new HttpRequestException("internal detail");

        GachaTeyvatHelperImportOutcome outcome = await harness.Coordinator.DownloadAsync(plan, Url);

        Assert.False(outcome.Succeeded);
        Assert.Contains("无法访问提瓦特小助手", outcome.FailureMessage);
        Assert.Contains("未写入任何数据", outcome.FailureMessage);
        Assert.DoesNotContain("supersecret", outcome.FailureMessage);
        Assert.DoesNotContain("internal detail", outcome.FailureMessage);
        Assert.Equal(1, harness.Gateway.DownloadCalls);
    }

    [Fact]
    public async Task DownloadSuccessReturnsFileNameAndPayload()
    {
        var harness = new Harness();
        harness.Planning.SetArchives(Archive());
        GachaTeyvatHelperImportPlan plan = (await harness.Coordinator.PlanAsync(
            Uid, Url, ArchiveId, GachaImportMode.Incremental)).Plan!;
        byte[] payload = Encoding.UTF8.GetBytes("{\"info\":{}}");
        harness.Gateway.Download = new TeyvatHelperUigfDownload("Teyvat-UIGF.json", payload);

        GachaTeyvatHelperImportOutcome outcome = await harness.Coordinator.DownloadAsync(plan, Url);

        Assert.True(outcome.Succeeded);
        Assert.Equal("Teyvat-UIGF.json", outcome.FileName);
        Assert.Equal(payload, outcome.Content);
        Assert.Equal(Uid, harness.Gateway.LastUid);
        Assert.Equal(Url, harness.Gateway.LastUrl);
    }

    private static PlayerArchive Archive() =>
        new(ArchiveId, "01 合成档案", FixedTime, FixedTime);

    private sealed class Harness
    {
        public FakePlanningSource Planning { get; } = new();

        public FakeGateway Gateway { get; } = new();

        public GachaTeyvatHelperImportCoordinator Coordinator { get; }

        public Harness()
        {
            Coordinator = new(Planning, Gateway);
        }
    }

    private sealed class FakePlanningSource : IGachaImportPlanningSource
    {
        public IReadOnlyList<PlayerArchive> Archives { get; private set; } = [];

        public void SetArchives(params PlayerArchive[] archives) => Archives = archives;

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Archives);

        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameAccount>>([]);
    }

    private sealed class FakeGateway : IGachaTeyvatHelperImportGateway
    {
        public TeyvatHelperImportAvailability Availability { get; set; } = new(null, false);

        public TeyvatHelperUigfDownload Download { get; set; } =
            new("Teyvat-UIGF.json", []);

        public Exception? DownloadException { get; set; }

        public int AvailabilityCalls { get; private set; }

        public int DownloadCalls { get; private set; }

        public string? LastUid { get; private set; }

        public string? LastUrl { get; private set; }

        public Task<TeyvatHelperImportAvailability> GetAvailabilityAsync(
            CancellationToken cancellationToken = default)
        {
            AvailabilityCalls++;
            return Task.FromResult(Availability);
        }

        public Task<TeyvatHelperUigfDownload> DownloadManuallyAsync(
            string uid,
            string gachaUrl,
            CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            LastUid = uid;
            LastUrl = gachaUrl;
            return DownloadException is not null
                ? Task.FromException<TeyvatHelperUigfDownload>(DownloadException)
                : Task.FromResult(Download);
        }
    }
}

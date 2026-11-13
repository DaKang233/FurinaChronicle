// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Diagnostics;

// Composition-level checks: the real Windows cache provider, the real RefreshGachaRecords use
// case and the real Teyvat Helper import source are wired to an isolated SQLite database with
// fake network/cache payloads only.
public sealed class Ui1RefreshCommandsIntegrationTests
{
    private const string SyntheticGachaUrl =
        "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
        "?authkey=synthetic-test-only&game_biz=hk4e_cn&region=cn_gf01";

    [Fact]
    public async Task WindowsCacheProviderRefreshImportsAndUpdatesIsolatedFacts()
    {
        if (!OperatingSystem.IsWindows())
        {
            // The real Windows web-cache provider is Windows-only; the coordinator/gateway logic
            // is covered cross-platform by GachaWindowsCacheRefreshTests.
            return;
        }

        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive archive = await ArchiveAsync(environment, "01 合成档案");
            GameAccount account = await AccountAsync(environment, archive.Id, "100000001");
            string gameRoot = Path.Combine(directory, "fake-game");
            string cacheDirectory = Path.Combine(
                gameRoot,
                "YuanShen_Data",
                "webCaches",
                "Cache",
                "Cache_Data");
            Directory.CreateDirectory(cacheDirectory);
            File.WriteAllText(Path.Combine(cacheDirectory, "data_2"), SyntheticGachaUrl);

            var provider = new WindowsGachaCacheUrlProvider();
            var logClient = new FakeGachaLogClient();
            var refresher = new RefreshGachaRecords(
                environment.Records,
                new CommitGachaRecords(environment.Changes),
                new EmptyPassportStore(),
                new ThrowingSTokenProvider(),
                provider,
                logClient,
                new EmptyGachaItemMetadataProvider());
            var planner = new ServiceGachaImportPlanningSource(
                new GetPlayerArchives(environment.Archives),
                new GetGameAccounts(environment.Archives, environment.Accounts));
            var coordinator = new GachaWindowsCacheRefreshCoordinator(
                planner,
                new ServiceGachaWindowsCacheRefreshGateway(refresher, provider));

            GachaWindowsCacheRefreshPlan plan = (await coordinator.PlanAsync(
                gameRoot,
                archive.Id,
                account.Id,
                GachaImportMode.Incremental)).Plan!;
            GachaWindowsCacheRefreshOutcome first = await coordinator.ExecuteAsync(plan);

            Assert.True(first.Succeeded, first.Message);
            Assert.Equal(2, first.Result!.InsertedCount);
            Assert.Equal(63, (await environment.Records.GetRecentAsync(account.Id, 100)).Count);

            logClient.Records[0] = logClient.Records[0] with { ItemName = "网页缓存覆盖物品" };
            GachaWindowsCacheRefreshPlan fullPlan = (await coordinator.PlanAsync(
                gameRoot,
                archive.Id,
                account.Id,
                GachaImportMode.Full)).Plan!;
            GachaWindowsCacheRefreshOutcome second = await coordinator.ExecuteAsync(fullPlan);

            Assert.True(second.Succeeded, second.Message);
            Assert.Equal(1, second.Result!.UpdatedCount);
            IReadOnlyList<GachaRecord> stored =
                await environment.Records.GetRecentAsync(account.Id, 100);
            Assert.Equal(
                "网页缓存覆盖物品",
                Assert.Single(stored, record => record.ExternalRecordId == "990001").ItemName);

            string emptyFolder = Path.Combine(directory, "empty-game");
            Directory.CreateDirectory(emptyFolder);
            GachaWindowsCacheRefreshPlan missingPlan = (await coordinator.PlanAsync(
                emptyFolder,
                archive.Id,
                account.Id,
                GachaImportMode.Incremental)).Plan!;
            GachaWindowsCacheRefreshOutcome missing = await coordinator.ExecuteAsync(missingPlan);
            Assert.False(missing.Succeeded);
            Assert.Contains("未在所选目录找到", missing.Message);
            Assert.Contains("未写入任何数据", missing.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task TeyvatHelperManualDownloadFeedsTheUnifiedUigfImportFlow()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive archive = await ArchiveAsync(environment, "01 合成档案");
            GameAccount account = await AccountAsync(environment, archive.Id, "100000001");
            byte[] payload = Encoding.UTF8.GetBytes(SyntheticUigfJson(
                account.Uid,
                ("990001", "提瓦特物品1", "2026-11-10 12:00:00"),
                ("990002", "提瓦特物品2", "2026-11-10 12:01:00")));
            var teyvatClient = new FakeTeyvatClient(payload);
            var teyvatSource = new TeyvatHelperUigfImportSource(
                new EmptyPassportStore(),
                new EmptySelectionStore(),
                new ThrowingSTokenProvider(),
                teyvatClient);
            var planner = new ServiceGachaImportPlanningSource(
                new GetPlayerArchives(environment.Archives),
                new GetGameAccounts(environment.Archives, environment.Accounts));
            var coordinator = new GachaTeyvatHelperImportCoordinator(
                planner,
                new ServiceGachaTeyvatHelperImportGateway(teyvatSource));

            GachaTeyvatHelperImportPlan plan = (await coordinator.PlanAsync(
                account.Uid,
                SyntheticGachaUrl,
                archive.Id,
                GachaImportMode.Incremental)).Plan!;
            GachaTeyvatHelperImportOutcome download = await coordinator.DownloadAsync(
                plan,
                SyntheticGachaUrl);

            Assert.True(download.Succeeded, download.FailureMessage);
            Assert.Equal("Teyvat-UIGF.json", download.FileName);
            Assert.Equal(account.Uid, teyvatClient.LastUid);

            var uigf = new GachaImportCoordinator(
                environment.GachaImportReader,
                new ServiceGachaUigfImporter(environment.GachaImporter),
                planner);
            GachaImportSource source = new(
                download.FileName!,
                _ => Task.FromResult<Stream>(
                    new MemoryStream(download.Content!, writable: false)));
            GachaImportSourceReadResult read = await uigf.ReadSourceAsync(source);
            Assert.True(read.Succeeded, read.FailureMessage);
            GachaImportPlanResult imported = await uigf.PlanAsync(
                read.Summary!,
                new GachaImportTargetRequest(GachaImportTargetScope.CurrentAccount, archive.Id)
                {
                    GameAccountId = account.Id
                },
                GachaImportMode.Incremental);
            Assert.True(imported.Succeeded, imported.FailureMessage);
            GachaImportExecution execution = await uigf.ExecuteAsync(imported.Plan!);

            Assert.True(execution.IsSuccess, execution.Message);
            Assert.Equal(2, execution.Result!.ImportedCount);
            Assert.Equal(63, (await environment.Records.GetRecentAsync(account.Id, 100)).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string SyntheticUigfJson(
        string uid,
        params (string Id, string Name, string Time)[] records)
    {
        string items = string.Join(
            ",",
            records.Select(record => $$"""
                {
                  "uigf_gacha_type": "301",
                  "gacha_type": "301",
                  "item_id": "10000089",
                  "count": "1",
                  "time": "{{record.Time}}",
                  "name": "{{record.Name}}",
                  "item_type": "角色",
                  "rank_type": "5",
                  "id": "{{record.Id}}"
                }
                """));
        return $$"""
            {
              "info": {
                "export_timestamp": 1762500000,
                "export_app": "SyntheticTeyvatTests",
                "export_app_version": "1.0.0",
                "version": "v4.2"
              },
              "hk4e": [{
                "uid": "{{uid}}",
                "timezone": 8,
                "lang": "zh-cn",
                "list": [{{items}}]
              }]
            }
            """;
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));

    private static async Task<PlayerArchive> ArchiveAsync(
        Ui1PreviewEnvironment environment,
        string name) =>
        Assert.Single(
            await environment.Archives.GetAllAsync(default),
            archive => archive.Name == name);

    private static async Task<GameAccount> AccountAsync(
        Ui1PreviewEnvironment environment,
        Guid archiveId,
        string uid) =>
        Assert.Single(
            await environment.Accounts.GetByArchiveIdAsync(archiveId, default),
            account => account.Uid == uid);

    private sealed class FakeGachaLogClient : IGachaLogClient
    {
        public List<GachaRemoteRecord> Records { get; } =
        [
            new("100000001", "990001", "网页缓存物品1", "10000089", "角色", 5, "301",
                new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.FromHours(8))),
            new("100000001", "990002", "网页缓存物品2", "11401", "武器", 4, "301",
                new DateTimeOffset(2026, 11, 10, 12, 1, 0, TimeSpan.FromHours(8)))
        ];

        public Task<GachaRemotePage> GetPageAsync(
            Uri sourceUrl,
            string gachaType,
            string? endId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(gachaType == "301" && endId is null
                ? new GachaRemotePage(Records.ToArray(), null)
                : new GachaRemotePage([], null));
    }

    private sealed class FakeTeyvatClient(byte[] payload) : ITeyvatHelperUigfClient
    {
        public string? LastUid { get; private set; }

        public Task<TeyvatHelperUigfDownload> DownloadAsync(
            string uid,
            Uri gachaUrl,
            CancellationToken cancellationToken = default)
        {
            LastUid = uid;
            return Task.FromResult(new TeyvatHelperUigfDownload("Teyvat-UIGF.json", payload));
        }
    }

    private sealed class EmptyPassportStore : IPassportAccountStore
    {
        public Task<IReadOnlyList<PassportAccount>> GetAllAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PassportAccount>>([]);

        public Task<PassportAccount?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PassportAccount?>(null);

        public Task SaveAsync(PassportAccount account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TrySaveIfUnchangedAsync(
            PassportAccount original,
            PassportAccount updated,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class EmptySelectionStore : IPassportSelectionStore
    {
        public Task<PassportSelection?> LoadAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PassportSelection?>(null);

        public Task SaveAsync(
            PassportSelection selection,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ClearAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingSTokenProvider : ISTokenGachaUrlProvider
    {
        public Task<Uri> CreateAsync(
            PassportAccount passportAccount,
            GameAccount gameAccount,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

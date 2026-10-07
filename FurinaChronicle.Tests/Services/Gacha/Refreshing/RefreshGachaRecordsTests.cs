// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Gacha.Refreshing;

public sealed class RefreshGachaRecordsTests
{
    private static readonly Guid ArchiveId = Guid.NewGuid();
    private static readonly Guid GameAccountId = Guid.NewGuid();
    private static readonly GameAccount GameAccount = new(
        GameAccountId,
        ArchiveId,
        "123456789",
        GameServerRegion.ChinaOfficial,
        "测试账号",
        false,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow);
    private static readonly Uri ValidUrl = new(
        "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
        "?authkey=abc&auth_appid=webview_gacha&lang=zh-cn");

    [Fact]
    public async Task ExecuteAsync_DefaultIncremental_StopsAtFirstLocalRecord()
    {
        GachaRecord known = Record("100") with
        {
            ItemName = "保留的本地名称",
            RankType = 5
        };
        var repository = new InMemoryGachaRecordRepository([known]);
        var client = new StubGachaLogClient();
        client.Add("301", null, Page(
            Remote("103"),
            Remote("102"),
            Remote("100"),
            Remote("099")));
        var service = CreateService(repository, client);

        GachaRefreshResult result = await service.ExecuteAsync(new GachaRefreshRequest(
            GameAccount,
            GachaRefreshSource.ManualUrl,
            ManualUrl: ValidUrl.AbsoluteUri));

        Assert.Equal(2, result.FetchedCount);
        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.ReachedLocalBoundaryCount);
        IReadOnlyList<GachaRecord> stored =
            await repository.GetRecentAsync(GameAccountId, 20);
        Assert.Contains(stored, item => item.ExternalRecordId == "103");
        Assert.All(
            stored.Where(item => item.ExternalRecordId is "102" or "103"),
            item =>
            {
                Assert.Equal(DataOrigin.OfficialApi, item.Provenance.Origin);
                Assert.NotNull(item.Provenance.Timestamps.FetchedAt);
                Assert.Null(item.Provenance.Timestamps.ImportedAt);
                Assert.NotNull(item.Provenance.AcquisitionBatchId);
                Assert.Null(item.Provenance.Source);
            });
        Assert.Contains(stored, item =>
            item.ExternalRecordId == "100" &&
            item.ItemName == "保留的本地名称" &&
            item.RankType == 5);
        Assert.DoesNotContain(stored, item => item.ExternalRecordId == "099");
        Assert.Single(client.Calls, call => call.GachaType == "301");
    }

    [Fact]
    public async Task ExecuteAsync_FullRefresh_DoesNotStopAtLocalRecord()
    {
        var repository = new InMemoryGachaRecordRepository(
        [
            Record("100") with
            {
                ItemName = "不准确的旧名称",
                RankType = 5
            }
        ]);
        var client = new StubGachaLogClient();
        client.Add("301", null, new GachaRemotePage(
            [Remote("103"), Remote("100")],
            "100"));
        client.Add("301", "100", new GachaRemotePage(
            [Remote("099")],
            NextEndId: null));
        var service = CreateService(repository, client);

        GachaRefreshResult result = await service.ExecuteAsync(new GachaRefreshRequest(
            GameAccount,
            GachaRefreshSource.ManualUrl,
            GachaRefreshMode.Full,
            ManualUrl: ValidUrl.AbsoluteUri));

        Assert.Equal(3, result.FetchedCount);
        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(0, result.ReachedLocalBoundaryCount);
        Assert.Equal(2, client.Calls.Count(call => call.GachaType == "301"));
        IReadOnlyList<GachaRecord> stored =
            await repository.GetRecentAsync(GameAccountId, 20);
        Assert.Contains(stored, item =>
            item.ExternalRecordId == "100" &&
            item.ItemName == "Item 100" &&
            item.RankType == 3 &&
            item.Provenance.Origin == DataOrigin.OfficialApi);
        AcquisitionBatchId?[] batchIds = stored
            .Select(item => item.Provenance.AcquisitionBatchId)
            .Distinct()
            .ToArray();
        Assert.Single(batchIds);
        Assert.NotNull(batchIds[0]);
    }

    [Fact]
    public async Task ExecuteAsync_STokenSource_LoadsPassportAccountAndUsesProvider()
    {
        var repository = new InMemoryGachaRecordRepository([]);
        PassportAccount passportAccount = Passport("root-token");
        var passportStore = new StubPassportStore(passportAccount);
        var sTokenProvider = new StubSTokenProvider();
        var service = new RefreshGachaRecords(
            repository,
            new RepositoryCommitter(repository),
            passportStore,
            sTokenProvider,
            new StubWindowsProvider(false),
            new StubGachaLogClient(),
            new StubMetadataProvider());

        await service.ExecuteAsync(new GachaRefreshRequest(
            GameAccount,
            GachaRefreshSource.SToken,
            PassportAccountId: passportAccount.Id));

        Assert.Same(passportAccount, sTokenProvider.PassportAccount);
        Assert.Same(GameAccount, sTokenProvider.GameAccount);
    }

    [Fact]
    public async Task ExecuteAsync_WindowsCacheOnUnsupportedPlatform_ThrowsClearError()
    {
        var repository = new InMemoryGachaRecordRepository([]);
        var service = new RefreshGachaRecords(
            repository,
            new RepositoryCommitter(repository),
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            new StubGachaLogClient(),
            new StubMetadataProvider());

        PlatformNotSupportedException exception =
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
                service.ExecuteAsync(new GachaRefreshRequest(
                    GameAccount,
                    GachaRefreshSource.WindowsWebCache,
                    GameInstallationPath: "C:\\Games\\Genshin Impact")));

        Assert.Contains("only on Windows", exception.Message);
    }

    [Fact]
    public async Task ExecuteAsync_ResponseFromAnotherUid_IsRejectedWithoutSaving()
    {
        var repository = new InMemoryGachaRecordRepository([]);
        var client = new StubGachaLogClient();
        client.Add("301", null, Page(Remote("103") with { Uid = "223456789" }));
        var service = CreateService(repository, client);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ExecuteAsync(new GachaRefreshRequest(
                GameAccount,
                GachaRefreshSource.ManualUrl,
                ManualUrl: ValidUrl.AbsoluteUri)));

        Assert.Equal(0, await repository.CountAsync(GameAccountId));
    }

    [Fact]
    public async Task ExecuteAsync_MissingItemId_CompletesItFromItemName()
    {
        var repository = new InMemoryGachaRecordRepository([]);
        var client = new StubGachaLogClient();
        client.Add(
            "301",
            null,
            Page(Remote("103") with
            {
                ItemName = "芙宁娜",
                ItemId = null,
                ItemType = "Avatar",
                RankType = 5
            }));
        var service = CreateService(repository, client);

        await service.ExecuteAsync(new GachaRefreshRequest(
            GameAccount,
            GachaRefreshSource.ManualUrl,
            ManualUrl: ValidUrl.AbsoluteUri));

        GachaRecord stored = Assert.Single(
            await repository.GetRecentAsync(GameAccountId, 20));
        Assert.Equal("10000089", stored.ItemId);
    }

    [Fact]
    public async Task ExecuteAsync_OfficialRefresh_PersistsProvenanceThroughSqliteRepository()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: GameAccount.Uid,
            region: GameAccount.ServerRegion);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        var client = new StubGachaLogClient();
        client.Add("301", null, Page(Remote("sqlite-provenance")));
        var service = new RefreshGachaRecords(
            context.Gacha,
            new CommitGachaRecords(context.AtomicGacha),
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            client,
            new StubMetadataProvider());

        await service.ExecuteAsync(new GachaRefreshRequest(
            account,
            GachaRefreshSource.ManualUrl,
            ManualUrl: ValidUrl.AbsoluteUri));

        GachaRecord stored = Assert.Single(
            await context.Gacha.GetRecentAsync(account.Id, 10));
        Assert.Equal(DataOrigin.OfficialApi, stored.Provenance.Origin);
        Assert.NotNull(stored.Provenance.Timestamps.FetchedAt);
        Assert.Null(stored.Provenance.Timestamps.ImportedAt);
        Assert.NotNull(stored.Provenance.AcquisitionBatchId);
        Assert.Null(stored.Provenance.Source);
    }

    [Fact]
    public async Task ExecuteAsync_NewTargetCreatesArchiveAccountAndFactsAtomically()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("刷新新档案");
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: GameAccount.Uid,
            region: GameAccount.ServerRegion);
        var client = new StubGachaLogClient();
        client.Add("301", null, Page(Remote("atomic-parent-refresh")));
        var service = new RefreshGachaRecords(
            context.Gacha,
            new CommitGachaRecords(context.AtomicGacha),
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            client,
            new StubMetadataProvider());

        GachaRefreshResult result = await service.ExecuteAsync(
            new GachaRefreshRequest(
                account,
                GachaRefreshSource.ManualUrl,
                ManualUrl: ValidUrl.AbsoluteUri,
                ArchiveToCreate: archive,
                AccountToCreate: account));

        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(archive, await context.Archives.GetByIdAsync(archive.Id));
        Assert.Equal(account, await context.Accounts.GetByIdAsync(account.Id));
        Assert.Single(await context.Gacha.GetRecentAsync(account.Id, 10));
        using SQLite.SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(0, raw.ExecuteScalar<int>("SELECT COUNT(*) FROM UndoMaterials;"));
    }

    [Fact]
    public async Task DiscoverIdentityAsync_UsesFirstNonEmptyPoolToFindUid()
    {
        var client = new StubGachaLogClient();
        client.Add("302", null, Page(Remote("103")));
        var service = CreateService(
            new InMemoryGachaRecordRepository([]),
            client);

        GachaRefreshIdentity identity = await service.DiscoverIdentityAsync(
            new GachaRefreshDiscoveryRequest(
                GachaRefreshSource.ManualUrl,
                ManualUrl: ValidUrl.AbsoluteUri));

        Assert.Equal(GameAccount.Uid, identity.Uid);
        Assert.Equal(GameServerRegion.ChinaOfficial, identity.ServerRegion);
        Assert.Equal(["301", "302"], client.Calls.Select(call => call.GachaType));
    }

    [Theory]
    [InlineData("http://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=a")]
    [InlineData("https://evil.example/getGachaLog?authkey=a")]
    [InlineData("https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog")]
    public void ManualUrl_InvalidInput_IsRejected(string value)
    {
        Assert.Throws<FormatException>(() => GachaRefreshUrl.Parse(value));
    }

    private static RefreshGachaRecords CreateService(
        InMemoryGachaRecordRepository repository,
        StubGachaLogClient client)
    {
        return new RefreshGachaRecords(
            repository,
            new RepositoryCommitter(repository),
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            client,
            new StubMetadataProvider());
    }

    private static GachaRecord Record(string id)
    {
        return Remote(id).ToDomain(GameAccountId);
    }

    private static GachaRemoteRecord Remote(string id)
    {
        return new GachaRemoteRecord(
            GameAccount.Uid,
            id,
            $"Item {id}",
            id,
            "Weapon",
            3,
            "301",
            new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.FromHours(8)));
    }

    private static GachaRemotePage Page(params GachaRemoteRecord[] records)
    {
        return new GachaRemotePage(records, NextEndId: null);
    }

    private static PassportAccount Passport(string sToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new PassportAccount(
            Guid.NewGuid(),
            "12345",
            "mid",
            null,
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                sToken,
                null,
                null,
                now,
                null,
                null,
                now),
            PassportDeviceIdentity.Create(),
            now,
            now);
    }

    private sealed class StubGachaLogClient : IGachaLogClient
    {
        private readonly Dictionary<(string, string?), GachaRemotePage> pages = [];

        public List<(string GachaType, string? EndId)> Calls { get; } = [];

        public void Add(string type, string? endId, GachaRemotePage page)
        {
            pages[(type, endId)] = page;
        }

        public Task<GachaRemotePage> GetPageAsync(
            Uri sourceUrl,
            string gachaType,
            string? endId,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((gachaType, endId));
            return Task.FromResult(
                pages.GetValueOrDefault((gachaType, endId)) ??
                new GachaRemotePage([], null));
        }
    }

    private sealed class RepositoryCommitter(
        InMemoryGachaRecordRepository repository)
        : ICommitGachaRecords
    {
        public async Task<CommitGachaRecordsResult> ExecuteAsync(
            CommitGachaRecordsRequest request,
            CancellationToken cancellationToken = default)
        {
            GachaSaveResult saved = await repository.SaveBatchAsync(
                request.Records,
                cancellationToken,
                request.ConflictPolicy);
            return new CommitGachaRecordsResult(
                ChangeExecutionStatus.Applied,
                ChangeSetId: null,
                saved.InsertedCount,
                saved.UpdatedCount,
                saved.DuplicateCount,
                ConflictCount: 0,
                SuppressedCount: 0);
        }
    }

    private sealed class StubMetadataProvider : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<GachaItemMetadata?>(null);
        }

        public ValueTask<GachaItemMetadata?> FindByNameAsync(
            GachaGame game,
            string itemName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaItemMetadata? result = itemName == "芙宁娜"
                ? new(
                    game,
                    "10000089",
                    itemName,
                    "Avatar",
                    5,
                    "https://example.test/furina.png")
                : null;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubSTokenProvider : ISTokenGachaUrlProvider
    {
        public PassportAccount? PassportAccount { get; private set; }

        public GameAccount? GameAccount { get; private set; }

        public Task<Uri> CreateAsync(
            PassportAccount passportAccount,
            GameAccount gameAccount,
            CancellationToken cancellationToken = default)
        {
            PassportAccount = passportAccount;
            GameAccount = gameAccount;
            return Task.FromResult(ValidUrl);
        }
    }

    private sealed class StubWindowsProvider(bool isSupported)
        : IWindowsGachaCacheUrlProvider
    {
        public bool IsSupported => isSupported;

        public Task<Uri> FindAsync(
            string gameInstallationPath,
            GameServerRegion region,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ValidUrl);
        }
    }

    private sealed class StubPassportStore(params PassportAccount[] accounts)
        : IPassportAccountStore
    {
        private readonly Dictionary<Guid, PassportAccount> values =
            accounts.ToDictionary(account => account.Id);

        public Task<IReadOnlyList<PassportAccount>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<PassportAccount>>(
                values.Values.ToArray());
        }

        public Task<PassportAccount?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            values.TryGetValue(accountId, out PassportAccount? account);
            return Task.FromResult(account);
        }

        public Task SaveAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default)
        {
            values[account.Id] = account;
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveIfUnchangedAsync(
            PassportAccount original,
            PassportAccount updated,
            CancellationToken cancellationToken = default)
        {
            if (!values.TryGetValue(original.Id, out PassportAccount? current) ||
                current.UpdatedAt != original.UpdatedAt)
            {
                return Task.FromResult(false);
            }

            values[updated.Id] = updated;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            values.Remove(accountId);
            return Task.CompletedTask;
        }
    }
}

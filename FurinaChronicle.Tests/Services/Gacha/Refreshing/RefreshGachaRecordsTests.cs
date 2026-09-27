using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Passport;

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
        WishRecord known = Record("100");
        var repository = new InMemoryWishRecordRepository([known]);
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
        Assert.Equal(1, result.ReachedLocalBoundaryCount);
        IReadOnlyList<WishRecord> stored =
            await repository.GetRecentAsync(GameAccountId, 20);
        Assert.Contains(stored, item => item.ExternalRecordId == "103");
        Assert.DoesNotContain(stored, item => item.ExternalRecordId == "099");
        Assert.Single(client.Calls, call => call.GachaType == "301");
    }

    [Fact]
    public async Task ExecuteAsync_FullRefresh_DoesNotStopAtLocalRecord()
    {
        var repository = new InMemoryWishRecordRepository([Record("100")]);
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
        Assert.Equal(1, result.DuplicateCount);
        Assert.Equal(0, result.ReachedLocalBoundaryCount);
        Assert.Equal(2, client.Calls.Count(call => call.GachaType == "301"));
    }

    [Fact]
    public async Task ExecuteAsync_STokenSource_LoadsPassportAccountAndUsesProvider()
    {
        var repository = new InMemoryWishRecordRepository([]);
        PassportAccount passportAccount = Passport("root-token");
        var passportStore = new StubPassportStore(passportAccount);
        var sTokenProvider = new StubSTokenProvider();
        var service = new RefreshGachaRecords(
            repository,
            passportStore,
            sTokenProvider,
            new StubWindowsProvider(false),
            new StubGachaLogClient());

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
        var service = new RefreshGachaRecords(
            new InMemoryWishRecordRepository([]),
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            new StubGachaLogClient());

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
        var repository = new InMemoryWishRecordRepository([]);
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
    public async Task DiscoverIdentityAsync_UsesFirstNonEmptyPoolToFindUid()
    {
        var client = new StubGachaLogClient();
        client.Add("302", null, Page(Remote("103")));
        var service = CreateService(
            new InMemoryWishRecordRepository([]),
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
        InMemoryWishRecordRepository repository,
        StubGachaLogClient client)
    {
        return new RefreshGachaRecords(
            repository,
            new StubPassportStore(),
            new StubSTokenProvider(),
            new StubWindowsProvider(false),
            client);
    }

    private static WishRecord Record(string id)
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

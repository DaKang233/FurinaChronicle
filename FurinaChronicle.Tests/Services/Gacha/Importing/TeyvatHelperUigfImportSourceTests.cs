using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Services.Gacha.Importing;

public sealed class TeyvatHelperUigfImportSourceTests
{
    [Fact]
    public async Task DownloadForSelectedRoleAsync_GeneratesFreshUrlForEveryImport()
    {
        PassportAccount account = Passport();
        var accountStore = new StubAccountStore(account);
        var selectionStore = new StubSelectionStore(
            new PassportSelection(account.Id, "123456789"));
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(
            accountStore,
            selectionStore,
            provider,
            client);

        TeyvatHelperImportAvailability availability =
            await source.GetAvailabilityAsync();
        await source.DownloadForSelectedRoleAsync();
        await source.DownloadForSelectedRoleAsync();

        Assert.Equal("123456789", availability.SelectedRoleUid);
        Assert.True(availability.CanAutomaticallyImport);
        Assert.Equal(2, provider.CallCount);
        Assert.Equal(2, client.Requests.Count);
        Assert.Equal(
            new[] { "fresh-1", "fresh-2" },
            client.Requests.Select(request =>
                GachaRefreshUrl.ParseQuery(request.Url.Query)["authkey"]));
        Assert.All(
            client.Requests,
            request => Assert.Equal("123456789", request.Uid));
    }

    [Fact]
    public async Task DownloadManuallyAsync_UsesValidatedInputWithoutGeneratingUrl()
    {
        PassportAccount account = Passport();
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(
            new StubAccountStore(account),
            new StubSelectionStore(
                new PassportSelection(account.Id, "123456789")),
            provider,
            client);
        const string manualUrl =
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
            "?authkey=manual-key&region=cn_gf01";

        await source.DownloadManuallyAsync(" 123456789 ", manualUrl);

        Assert.Equal(0, provider.CallCount);
        var request = Assert.Single(client.Requests);
        Assert.Equal("123456789", request.Uid);
        Assert.Equal("manual-key", GachaRefreshUrl.ParseQuery(request.Url.Query)["authkey"]);
    }

    [Fact]
    public async Task GetAvailabilityAsync_ReturnsUnavailableWhenAccountWasDeleted()
    {
        Guid missingId = Guid.NewGuid();
        var source = new TeyvatHelperUigfImportSource(
            new StubAccountStore(account: null),
            new StubSelectionStore(
                new PassportSelection(missingId, "123456789")),
            new StubSTokenProvider(),
            new RecordingClient());

        TeyvatHelperImportAvailability availability =
            await source.GetAvailabilityAsync();

        Assert.Null(availability.SelectedRoleUid);
        Assert.False(availability.CanAutomaticallyImport);
    }

    [Fact]
    public async Task DownloadManuallyAsync_WorksWithoutPassportOrSelectedRole()
    {
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(
            new StubAccountStore(account: null),
            new StubSelectionStore(selection: null),
            provider,
            client);

        await source.DownloadManuallyAsync(
            "123456789",
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog?authkey=manual");

        Assert.Equal(0, provider.CallCount);
        var request = Assert.Single(client.Requests);
        Assert.Equal("123456789", request.Uid);
        Assert.Equal(
            "manual",
            GachaRefreshUrl.ParseQuery(request.Url.Query)["authkey"]);
    }

    [Fact]
    public async Task DownloadForSelectedRoleAsync_RejectsOverseaAutomaticMode()
    {
        PassportAccount account = Passport(PassportRealm.Oversea);
        var provider = new StubSTokenProvider();
        var client = new RecordingClient();
        var source = new TeyvatHelperUigfImportSource(
            new StubAccountStore(account),
            new StubSelectionStore(
                new PassportSelection(account.Id, "812345678")),
            provider,
            client);

        TeyvatHelperImportAvailability availability =
            await source.GetAvailabilityAsync();
        NotSupportedException exception =
            await Assert.ThrowsAsync<NotSupportedException>(() =>
                source.DownloadForSelectedRoleAsync());

        Assert.Equal("812345678", availability.SelectedRoleUid);
        Assert.False(availability.CanAutomaticallyImport);
        Assert.Contains("仅支持国服", exception.Message);
        Assert.Equal(0, provider.CallCount);
        Assert.Empty(client.Requests);
    }

    private static PassportAccount Passport(
        PassportRealm realm = PassportRealm.MainlandChina)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new PassportAccount(
            Guid.NewGuid(),
            "12345",
            "mid-1",
            "Tester",
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                "stoken",
                null,
                null,
                now,
                null,
                null,
                now),
            new PassportDeviceIdentity(
                "31bf26d5-2afe-4b30-aab4-42fcdb3d4b09",
                "fp"),
            now,
            now,
            realm);
    }

    private sealed class StubSTokenProvider : ISTokenGachaUrlProvider
    {
        public int CallCount { get; private set; }

        public Task<Uri> CreateAsync(
            PassportAccount passportAccount,
            GameAccount gameAccount,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new Uri(
                "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
                $"?authkey=fresh-{CallCount}&region=cn_gf01"));
        }
    }

    private sealed class RecordingClient : ITeyvatHelperUigfClient
    {
        public List<(string Uid, Uri Url)> Requests { get; } = [];

        public Task<TeyvatHelperUigfDownload> DownloadAsync(
            string uid,
            Uri gachaUrl,
            CancellationToken cancellationToken = default)
        {
            Requests.Add((uid, gachaUrl));
            return Task.FromResult(new TeyvatHelperUigfDownload(
                "test.json",
                "{}"u8.ToArray()));
        }
    }

    private sealed class StubSelectionStore(PassportSelection? selection)
        : IPassportSelectionStore
    {
        public Task<PassportSelection?> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(selection);
        }

        public Task SaveAsync(
            PassportSelection value,
            CancellationToken cancellationToken = default)
        {
            selection = value;
            return Task.CompletedTask;
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default)
        {
            selection = null;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAccountStore(PassportAccount? account)
        : IPassportAccountStore
    {
        public Task<IReadOnlyList<PassportAccount>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<PassportAccount> accounts = account is null
                ? []
                : [account];
            return Task.FromResult(accounts);
        }

        public Task<PassportAccount?> GetByIdAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                account?.Id == accountId ? account : null);
        }

        public Task SaveAsync(
            PassportAccount value,
            CancellationToken cancellationToken = default)
        {
            account = value;
            return Task.CompletedTask;
        }

        public Task<bool> TrySaveIfUnchangedAsync(
            PassportAccount original,
            PassportAccount updated,
            CancellationToken cancellationToken = default)
        {
            account = updated;
            return Task.FromResult(true);
        }

        public Task DeleteAsync(
            Guid accountId,
            CancellationToken cancellationToken = default)
        {
            if (account?.Id == accountId)
            {
                account = null;
            }

            return Task.CompletedTask;
        }
    }
}

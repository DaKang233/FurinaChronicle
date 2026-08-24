using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class SqliteGachaItemMetadataProviderTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FindByIdAsync_FirstUseDownloadsCombinesAndCachesMetadata()
    {
        await using MetadataTestContext context = MetadataTestContext.Create();

        GachaItemMetadata? first = await context.Provider.FindByIdAsync(
            GachaGame.GenshinImpact,
            "10000089");
        GachaItemMetadata? second = await context.Provider.FindByIdAsync(
            GachaGame.GenshinImpact,
            "10000089");

        Assert.NotNull(first);
        Assert.Equal("Furina", first.Name);
        Assert.Equal("Avatar", first.ItemType);
        Assert.Equal(5, first.RankType);
        Assert.Equal("https://example.test/furina.png", first.IconUrl);
        Assert.Equal("\u8299\u5b81\u5a1c", first.LocalizedNames!["chs"]);
        Assert.Equal("Furina", first.LocalizedNames["en"]);
        Assert.Equal(first.ItemId, second!.ItemId);
        Assert.Equal(first.RankType, second.RankType);
        Assert.Equal(1, context.RemoteSource.CallCount);
    }

    [Fact]
    public async Task RefreshIfNeededAsync_AfterIntervalUsesSha256AndReplacesChangedContent()
    {
        await using MetadataTestContext context = MetadataTestContext.Create();
        GachaMetadataRefreshResult initial =
            await context.Provider.RefreshIfNeededAsync(GachaGame.GenshinImpact);

        context.Clock.Advance(TimeSpan.FromDays(8));
        GachaMetadataRefreshResult unchanged =
            await context.Provider.RefreshIfNeededAsync(GachaGame.GenshinImpact);

        Assert.True(initial.ContentUpdated);
        Assert.False(unchanged.ContentUpdated);
        Assert.True(unchanged.UsedExistingCache);
        Assert.Equal(initial.ContentSha256, unchanged.ContentSha256);
        Assert.Equal(2, context.RemoteSource.CallCount);

        context.RemoteSource.Items =
        [
            new(
                "10000089",
                "Furina",
                "Avatar",
                4,
                "https://example.test/furina-v2.png")
        ];
        context.Clock.Advance(TimeSpan.FromDays(8));

        GachaMetadataRefreshResult changed =
            await context.Provider.RefreshIfNeededAsync(GachaGame.GenshinImpact);
        GachaItemMetadata? loaded = await context.Provider.FindByIdAsync(
            GachaGame.GenshinImpact,
            "10000089");

        Assert.True(changed.ContentUpdated);
        Assert.False(changed.UsedExistingCache);
        Assert.NotEqual(initial.ContentSha256, changed.ContentSha256);
        Assert.Equal(4, loaded!.RankType);
        Assert.Equal("https://example.test/furina-v2.png", loaded.IconUrl);
        Assert.Equal(3, context.RemoteSource.CallCount);
    }

    [Fact]
    public async Task FindByIdAsync_RefreshFailurePreservesCacheAndThrottlesRetry()
    {
        await using MetadataTestContext context = MetadataTestContext.Create();
        GachaItemMetadata? initial = await context.Provider.FindByIdAsync(
            GachaGame.GenshinImpact,
            "10000089");

        context.Clock.Advance(TimeSpan.FromDays(8));
        context.RemoteSource.Exception =
            new HttpRequestException("metadata endpoint unavailable");

        GachaItemMetadata? afterFailure =
            await context.Provider.FindByIdAsync(
                GachaGame.GenshinImpact,
                "10000089");
        GachaItemMetadata? secondLookup =
            await context.Provider.FindByIdAsync(
                GachaGame.GenshinImpact,
                "10000089");

        Assert.Equal(initial!.ItemId, afterFailure!.ItemId);
        Assert.Equal(initial.RankType, afterFailure.RankType);
        Assert.Equal(initial.ItemId, secondLookup!.ItemId);
        Assert.Equal(initial.RankType, secondLookup.RankType);
        Assert.Equal(2, context.RemoteSource.CallCount);
    }

    [Fact]
    public async Task FindByIdAsync_AfterDatabaseReopensUsesPersistedCache()
    {
        string directory = MetadataTestContext.CreateDirectory();
        string databasePath = Path.Combine(directory, "metadata.db3");

        try
        {
            var firstClock = new MutableTimeProvider(InitialTime);
            var firstRemote = new StubRemoteSource();
            await using (var firstDatabase = new GachaMetadataDatabase(
                new GachaMetadataOptions(databasePath)))
            {
                SqliteGachaItemMetadataProvider firstProvider = CreateProvider(
                    firstDatabase,
                    firstRemote,
                    firstClock,
                    databasePath);
                Assert.NotNull(await firstProvider.FindByIdAsync(
                    GachaGame.GenshinImpact,
                    "10000089"));
            }

            var secondRemote = new StubRemoteSource
            {
                Exception = new HttpRequestException("must not be called")
            };
            await using (var secondDatabase = new GachaMetadataDatabase(
                new GachaMetadataOptions(databasePath)))
            {
                SqliteGachaItemMetadataProvider secondProvider = CreateProvider(
                    secondDatabase,
                    secondRemote,
                    new MutableTimeProvider(InitialTime.AddDays(1)),
                    databasePath);

                GachaItemMetadata? loaded = await secondProvider.FindByIdAsync(
                    GachaGame.GenshinImpact,
                    "10000089");

                Assert.NotNull(loaded);
                Assert.Equal(5, loaded.RankType);
                Assert.Equal(0, secondRemote.CallCount);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SqliteGachaItemMetadataProvider CreateProvider(
        GachaMetadataDatabase database,
        StubRemoteSource remoteSource,
        TimeProvider timeProvider,
        string databasePath)
    {
        return new SqliteGachaItemMetadataProvider(
            database,
            remoteSource,
            new StubLocalizationSource(),
            new GachaMetadataOptions(
                databasePath,
                TimeSpan.FromDays(7),
                preferredLanguage: "en"),
            timeProvider);
    }

    private sealed class MetadataTestContext : IAsyncDisposable
    {
        private readonly string directory;

        private MetadataTestContext(
            string directory,
            GachaMetadataDatabase database,
            StubRemoteSource remoteSource,
            MutableTimeProvider clock,
            SqliteGachaItemMetadataProvider provider)
        {
            this.directory = directory;
            Database = database;
            RemoteSource = remoteSource;
            Clock = clock;
            Provider = provider;
        }

        public GachaMetadataDatabase Database { get; }

        public StubRemoteSource RemoteSource { get; }

        public MutableTimeProvider Clock { get; }

        public SqliteGachaItemMetadataProvider Provider { get; }

        public static MetadataTestContext Create()
        {
            string directory = CreateDirectory();
            string databasePath = Path.Combine(directory, "metadata.db3");
            var database = new GachaMetadataDatabase(
                new GachaMetadataOptions(databasePath));
            var remoteSource = new StubRemoteSource();
            var clock = new MutableTimeProvider(InitialTime);
            SqliteGachaItemMetadataProvider provider = CreateProvider(
                database,
                remoteSource,
                clock,
                databasePath);

            return new MetadataTestContext(
                directory,
                database,
                remoteSource,
                clock,
                provider);
        }

        public static string CreateDirectory()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleMetadataTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return directory;
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubRemoteSource : IGachaMetadataRemoteSource
    {
        public IReadOnlyList<GachaMetadataSourceItem> Items { get; set; } =
        [
            new(
                "10000089",
                "Furina",
                "Avatar",
                5,
                "https://example.test/furina.png")
        ];

        public Exception? Exception { get; set; }

        public int CallCount { get; private set; }

        public Task<IReadOnlyList<GachaMetadataSourceItem>> FetchAsync(
            GachaGame game,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Items);
        }
    }

    private sealed class StubLocalizationSource : IGachaLocalizationSource
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> LoadAsync(
            GachaGame game,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, string> names =
                new Dictionary<string, string>
                {
                    ["chs"] = "\u8299\u5b81\u5a1c",
                    ["en"] = "Furina"
                };
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> result =
                new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["10000089"] = names
                };
            return Task.FromResult(result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan duration)
        {
            utcNow = utcNow.Add(duration);
        }
    }
}

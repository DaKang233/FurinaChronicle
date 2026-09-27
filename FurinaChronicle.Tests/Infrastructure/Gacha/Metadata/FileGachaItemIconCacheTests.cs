using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using System.Net;
using System.Net.Http.Headers;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class FileGachaItemIconCacheTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n7sAAAAASUVORK5CYII=");

    [Fact]
    public async Task GetOrRefreshAsync_ReusesSameItemFileUntilExpired()
    {
        string directory = CreateTemporaryDirectory();
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            Interlocked.Increment(ref requests);
            return ImageResponse();
        }));
        using var cache = new FileGachaItemIconCache(
            new GachaItemIconCacheOptions(directory),
            clock,
            client);
        try
        {
            string? first = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "10000089",
                "//example.test/first.png");
            clock.Advance(TimeSpan.FromDays(29));
            string? second = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "10000089",
                "https://example.test/changed.png");

            Assert.NotNull(first);
            Assert.Equal(first, second);
            Assert.True(File.Exists(first));
            Assert.Equal(1, requests);

            await cache.ClearAsync();
            Assert.False(File.Exists(first));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrRefreshAsync_ExpiredRefreshFailureUsesOldFile()
    {
        string directory = CreateTemporaryDirectory();
        var clock = new MutableTimeProvider(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            int requestNumber = Interlocked.Increment(ref requests);
            return requestNumber == 1
                ? ImageResponse()
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }));
        using var cache = new FileGachaItemIconCache(
            new GachaItemIconCacheOptions(directory),
            clock,
            client);
        try
        {
            string? first = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "10000089",
                "https://example.test/icon.png");
            clock.Advance(TimeSpan.FromDays(31));
            string? afterFailure = await cache.GetOrRefreshAsync(
                GachaGame.GenshinImpact,
                "10000089",
                "https://example.test/icon.png");

            Assert.NotNull(first);
            Assert.Equal(first, afterFailure);
            Assert.True(File.Exists(first));
            Assert.Equal(2, requests);
            Assert.Equal(OnePixelPng, await File.ReadAllBytesAsync(first));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GetOrRefreshAsync_ConcurrentSameItemDownloadsOnce()
    {
        string directory = CreateTemporaryDirectory();
        int requests = 0;
        using var client = new HttpClient(new StubHandler(_ =>
        {
            Interlocked.Increment(ref requests);
            return ImageResponse();
        }));
        using var cache = new FileGachaItemIconCache(
            new GachaItemIconCacheOptions(directory),
            TimeProvider.System,
            client);
        try
        {
            Task<string?>[] calls = Enumerable.Range(0, 8)
                .Select(_ => cache.GetOrRefreshAsync(
                    GachaGame.GenshinImpact,
                    "10000089",
                    "https://example.test/icon.png"))
                .ToArray();

            string?[] paths = await Task.WhenAll(calls);

            Assert.Single(paths.Distinct(StringComparer.Ordinal));
            Assert.Equal(1, requests);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static HttpResponseMessage ImageResponse()
    {
        var content = new ByteArrayContent(OnePixelPng);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content
        };
    }

    private static string CreateTemporaryDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(handler(request));
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public void Advance(TimeSpan interval) => utcNow += interval;
    }
}

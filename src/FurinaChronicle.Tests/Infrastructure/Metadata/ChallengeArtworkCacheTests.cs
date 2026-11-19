// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Infrastructure.Metadata;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Infrastructure.Metadata;

public sealed class ChallengeArtworkCacheTests
{
    private const string Avatar = "https://enka.network/ui/UI_AvatarIcon_Tighnari.png";
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n7sAAAAASUVORK5CYII=");

    [Theory]
    [InlineData("https://enka.network/ui/UI_AvatarIcon_Tighnari.png", true)]
    [InlineData("https://act-webstatic.hoyoverse.com/some/icon.webp", true)]
    [InlineData("https://act-webstatic.mihoyo.com/some/icon.jpg", true)]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png", true)]
    [InlineData("https://api.hutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png", true)]
    [InlineData("https://static.snaphutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png", true)]
    [InlineData("https://api.snaphutaorp.org/private/UI_MonsterIcon_Slime.png", false)]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/subdir/icon.png", false)]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/other%2Ficon.png", false)]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/../private.png", false)]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/UI_Icon.jpg", false)]
    [InlineData("https://enka.network/ui/icon.png?authkey=synthetic", false)]
    [InlineData("https://enka.network/ui/icon.png#fragment", false)]
    [InlineData("https://user:synthetic@enka.network/ui/icon.png", false)]
    [InlineData("https://enka.network:444/ui/icon.png", false)]
    [InlineData("http://enka.network/ui/icon.png", false)]
    [InlineData("https://enka.network.evil.test/ui/icon.png", false)]
    [InlineData("https://example.test/ui/icon.png", false)]
    [InlineData("file:///C:/private.png", false)]
    [InlineData("https://enka.network/ui/icon.svg", false)]
    [InlineData(null, false)]
    public void PublicArtworkPolicyHasNarrowCredentialFreeBoundaries(string? value, bool accepted) =>
        Assert.Equal(accepted, ChallengeArtworkUri.Parse(value) is not null);

    [Fact]
    public async Task SamePublicImageUsesOneFileUntilThirtyDaysAndClearMakesItDownloadableAgain()
    {
        await using var h = new Harness(_ => Task.FromResult(Image()));
        var first = await h.Cache.GetOrRefreshAsync(new(Avatar));
        h.Clock.Advance(TimeSpan.FromDays(29));
        Assert.Equal(first, await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(first, h.Cache.GetCachedPath(new(Avatar)));
        Assert.Equal(1, h.Requests);
        Assert.NotNull(first);
        Assert.Equal(Png, await File.ReadAllBytesAsync(first));
        Assert.StartsWith(h.Directory, first, StringComparison.OrdinalIgnoreCase);
        await h.Cache.ClearAsync();
        Assert.False(File.Exists(first));
        Assert.Null(h.Cache.GetCachedPath(new(Avatar)));
        Assert.NotNull(await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(2, h.Requests);
    }

    [Fact]
    public async Task FailedStaleRefreshKeepsOldFileAndBacksOffForRepeatedPortraits()
    {
        int calls = 0;
        await using var h = new Harness(_ => Task.FromResult(
            Interlocked.Increment(ref calls) == 2 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Image()));
        var path = await h.Cache.GetOrRefreshAsync(new(Avatar));
        h.Clock.Advance(TimeSpan.FromDays(31));
        DateTime oldWrite = File.GetLastWriteTimeUtc(path!);
        Assert.Equal(path, await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(path, await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(2, h.Requests);
        Assert.Equal(oldWrite, File.GetLastWriteTimeUtc(path!));
        h.Clock.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(path, await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(3, h.Requests);
        Assert.True(File.GetLastWriteTimeUtc(path!) > oldWrite);
    }

    [Fact]
    public async Task ConcurrentMissingOccurrencesCauseOneFailedRequestThenManualClearResetsTheBackoff()
    {
        await using var h = new Harness(async _ =>
        {
            await Task.Delay(20);
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var results = await Task.WhenAll(Enumerable.Range(0, 24)
            .Select(_ => h.Cache.GetOrRefreshAsync(new(Avatar))));
        Assert.All(results, path => Assert.Null(path));
        Assert.Equal(1, h.Requests);
        Assert.Empty(Directory.EnumerateFiles(h.Directory, "*", SearchOption.AllDirectories));
        await h.Cache.ClearAsync();
        Assert.Null(await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(2, h.Requests);
    }

    [Fact]
    public async Task UntrustedUriIsRejectedEvenWhenCalledWithoutThePresentationFilter()
    {
        await using var h = new Harness(_ => Task.FromResult(Image()));
        Assert.Null(await h.Cache.GetOrRefreshAsync(new("https://example.test/secret.png")));
        Assert.Null(await h.Cache.GetOrRefreshAsync(new(Avatar + "?token=synthetic")));
        Assert.Null(h.Cache.GetCachedPath(new(Avatar + "?token=synthetic")));
        Assert.Equal(0, h.Requests);
    }

    [Fact]
    public async Task CallerCancellationLeavesNoPartialFileAndDoesNotPreventARetry()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        await using var h = new Harness(async ct =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                began.SetResult();
                await Task.Delay(Timeout.Infinite, ct);
            }
            return Image();
        });
        using var cancellation = new CancellationTokenSource();
        var pending = h.Cache.GetOrRefreshAsync(new(Avatar), cancellation.Token);
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(h.Cache.GetCachedPath(new(Avatar)));
        Assert.NotNull(await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(2, h.Requests);
        Assert.Empty(Directory.EnumerateFiles(h.Directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ClearWaitsForAnActiveDownloadThenRemovesItsCompletedFile()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = new Harness(async ct =>
        {
            began.TrySetResult();
            await release.Task.WaitAsync(ct);
            return Image();
        });
        var pending = h.Cache.GetOrRefreshAsync(new(Avatar));
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clear = h.Cache.ClearAsync();
        Assert.False(clear.IsCompleted);
        release.SetResult();
        var file = await pending;
        await clear.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(file));
        Assert.Null(h.Cache.GetCachedPath(new(Avatar)));
    }

    [Fact]
    public async Task DisposeCancelsAndDrainsPendingLoads()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var h = new Harness(async ct =>
        {
            began.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return Image();
        });
        var pending = h.Cache.GetOrRefreshAsync(new(Avatar));
        await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.Cache.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Null(h.Cache.GetCachedPath(new(Avatar)));
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("too-large")]
    [InlineData("redirect")]
    public async Task UnusableResponseNeverOverwritesAnOldImage(string failure)
    {
        int requests = 0;
        await using var h = new Harness(_ =>
        {
            if (Interlocked.Increment(ref requests) == 1) return Task.FromResult(Image());
            var response = failure switch
            {
                "invalid" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not an image</html>") },
                "too-large" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[2048]) },
                _ => new HttpResponseMessage(HttpStatusCode.Redirect),
            };
            if (failure == "redirect") response.Headers.Location = new("https://example.test/private.png");
            return Task.FromResult(response);
        }, maximumBytes: 1024);
        var path = await h.Cache.GetOrRefreshAsync(new(Avatar));
        h.Clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(path, await h.Cache.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(Png, await File.ReadAllBytesAsync(path!));
        Assert.Equal(2, h.Requests);
        Assert.Empty(Directory.EnumerateFiles(h.Directory, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task IsolatedOrOfflineHostUsesExistingStaleCacheWithoutARequest()
    {
        await using var h = new Harness(_ => Task.FromResult(Image()));
        var path = await h.Cache.GetOrRefreshAsync(new(Avatar));
        h.Clock.Advance(TimeSpan.FromDays(31));
        await using var offline = new FileChallengeArtworkCache(h.Directory, h.Clock, h.Client, allowDownloads: false);
        Assert.Equal(path, await offline.GetOrRefreshAsync(new(Avatar)));
        Assert.Equal(1, h.Requests);
    }

    [Fact]
    public async Task DifferentImagesNeverStartMoreThanFourDownloadsTogether()
    {
        var four = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int active = 0, maximum = 0;
        await using var h = new Harness(async ct =>
        {
            int current = Interlocked.Increment(ref active);
            lock (four) maximum = Math.Max(maximum, current);
            if (current == 4) four.TrySetResult();
            try { await release.Task.WaitAsync(ct); return Image(); }
            finally { Interlocked.Decrement(ref active); }
        });
        var downloads = Enumerable.Range(0, 12)
            .Select(index => h.Cache.GetOrRefreshAsync(new($"https://enka.network/ui/synthetic_{index}.png"))).ToArray();
        try
        {
            await four.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, h.Requests);
        }
        finally { release.TrySetResult(); }
        Assert.All(await Task.WhenAll(downloads), path => Assert.NotNull(path));
        Assert.Equal(12, h.Requests);
        Assert.Equal(4, maximum);
    }

    [Fact]
    public async Task CacheIoDoesNotPostContinuationsBackToTheCallersUiContext()
    {
        await using var h = new Harness(async ct =>
        {
            await Task.Delay(20, ct).ConfigureAwait(false);
            return Image();
        });
        var context = new RecordingContext();
        var previous = SynchronizationContext.Current;
        Task<string?> pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = h.Cache.GetOrRefreshAsync(new(Avatar));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
        Assert.NotNull(await pending);
        Assert.Equal(0, context.Posts);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        internal int Posts;
        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref Posts);
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private static HttpResponseMessage Image() =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        internal void Advance(TimeSpan duration) => now += duration;
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal string Directory { get; } = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        internal Clock Clock { get; } = new();
        internal HttpClient Client { get; }
        internal FileChallengeArtworkCache Cache { get; }
        internal int Requests;
        internal Harness(Func<CancellationToken, Task<HttpResponseMessage>> response, long maximumBytes = 1024 * 1024)
        {
            System.IO.Directory.CreateDirectory(Directory);
            Client = new(new Handler(async (request, ct) =>
            {
                Interlocked.Increment(ref Requests);
                Assert.Null(request.Headers.Authorization);
                Assert.False(request.Headers.Contains("Cookie"));
                Assert.Equal("", request.RequestUri!.Query);
                return await response(ct).ConfigureAwait(false);
            }));
            Cache = new(Directory, Clock, Client, maximumFileBytes: maximumBytes);
        }
        public async ValueTask DisposeAsync()
        {
            await Cache.DisposeAsync();
            Client.Dispose();
            System.IO.Directory.Delete(Directory, true);
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }
}

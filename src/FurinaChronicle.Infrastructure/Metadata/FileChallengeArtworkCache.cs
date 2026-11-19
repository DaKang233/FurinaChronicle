// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Infrastructure.Metadata;

/// <summary>Reuses the bounded, atomic image-file cache with an isolated directory and strict public URI policy.</summary>
public sealed class FileChallengeArtworkCache : IChallengeArtworkCache, IAsyncDisposable
{
    private const int Concurrency = 4;
    private readonly FileGachaItemIconCache files;
    private readonly HttpClient client;
    private readonly bool ownsClient;
    private readonly bool allowDownloads;
    private readonly TimeProvider clock;
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim operations = new(Concurrency, Concurrency);
    private readonly CancellationTokenSource shutdown = new();
    private bool disposed;

    public FileChallengeArtworkCache(string directory, TimeProvider clock, HttpClient? client = null,
        bool allowDownloads = true, long maximumFileBytes = 4 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(clock);
        var options = new GachaItemIconCacheOptions(directory, TimeSpan.FromDays(30), maximumFileBytes);
        this.clock = clock;
        this.allowDownloads = allowDownloads;
        this.client = client ?? new HttpClient(new ChallengeArtworkRedirectHandler(new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
        })) { Timeout = TimeSpan.FromSeconds(10) };
        ownsClient = client is null;
        files = new(options, clock, this.client);
    }

    public string? GetCachedPath(Uri? source) =>
        disposed || !ChallengeArtworkUri.IsSupported(source) ? null :
        files.GetCachedPath(GachaGame.GenshinImpact, source!.AbsoluteUri);

    public async Task<string?> GetOrRefreshAsync(Uri? source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed || !ChallengeArtworkUri.IsSupported(source)) return null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        await operations.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            string key = source!.AbsoluteUri;
            string? oldPath = files.GetCachedPath(GachaGame.GenshinImpact, key);
            if (!allowDownloads) return oldPath;
            Entry entry = entries.GetOrAdd(key, static _ => new());
            await entry.Gate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                string? fresh = files.GetCachedPath(GachaGame.GenshinImpact, key, freshOnly: true);
                if (fresh is not null) return fresh;
                if (entry.RetryAfter > clock.GetUtcNow()) return oldPath;
                string? result = await files.GetOrRefreshAsync(
                    GachaGame.GenshinImpact, key, source.AbsoluteUri, linked.Token).ConfigureAwait(false);
                // A failed first download or stale refresh should not repeat for every occurrence of an image.
                entry.RetryAfter = files.GetCachedPath(GachaGame.GenshinImpact, key, freshOnly: true) is null
                    ? clock.GetUtcNow().AddMinutes(10) : DateTimeOffset.MinValue;
                return result;
            }
            finally { entry.Gate.Release(); }
        }
        finally { operations.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (disposed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        int held = 0;
        try
        {
            for (; held < Concurrency; held++)
                await operations.WaitAsync(linked.Token).ConfigureAwait(false);
            await files.ClearAsync(linked.Token).ConfigureAwait(false);
            foreach (Entry entry in entries.Values) entry.Gate.Dispose();
            entries.Clear();
        }
        finally { if (held > 0) operations.Release(held); }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        shutdown.Cancel();
        // Drain all current and queued loads before disposing their gates or HTTP client.
        for (int i = 0; i < Concurrency; i++) await operations.WaitAsync().ConfigureAwait(false);
        files.Dispose();
        if (ownsClient) client.Dispose();
        foreach (Entry entry in entries.Values) entry.Gate.Dispose();
        entries.Clear();
        shutdown.Dispose();
        operations.Dispose();
    }

    private sealed class Entry
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal DateTimeOffset RetryAfter { get; set; }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed class FileGachaItemIconCache : IGachaItemIconCache, IDisposable
{
    private const int MaximumConcurrentDownloads = 4;
    private readonly GachaItemIconCacheOptions options;
    private readonly TimeProvider timeProvider;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly SemaphoreSlim downloadGate =
        new(MaximumConcurrentDownloads, MaximumConcurrentDownloads);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> itemGates =
        new(StringComparer.Ordinal);

    public FileGachaItemIconCache(
        GachaItemIconCacheOptions options,
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        this.options = options;
        this.timeProvider = timeProvider;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
    }

    public async Task<string?> GetOrRefreshAsync(
        GachaGame game,
        string itemId,
        string? sourceUrl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        string cachePath = GetCachePath(game, itemId.Trim());
        string gateKey = $"{(int)game}:{itemId.Trim()}";
        SemaphoreSlim itemGate = itemGates.GetOrAdd(
            gateKey,
            static _ => new SemaphoreSlim(1, 1));
        await itemGate.WaitAsync(cancellationToken);
        try
        {
            bool hasOldFile = File.Exists(cachePath);
            if (hasOldFile && IsFresh(cachePath))
            {
                return cachePath;
            }

            if (!TryCreateSourceUri(sourceUrl, out Uri? sourceUri))
            {
                return hasOldFile ? cachePath : null;
            }

            try
            {
                await DownloadAsync(
                    sourceUri,
                    cachePath,
                    cancellationToken);
                return cachePath;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested)
            {
                return hasOldFile ? cachePath : null;
            }
            catch (HttpRequestException)
            {
                return hasOldFile ? cachePath : null;
            }
            catch (InvalidDataException)
            {
                return hasOldFile ? cachePath : null;
            }
            catch (IOException)
            {
                return hasOldFile ? cachePath : null;
            }
            catch (UnauthorizedAccessException)
            {
                return hasOldFile ? cachePath : null;
            }
        }
        finally
        {
            itemGate.Release();
        }
    }

    public async Task ClearAsync(
        CancellationToken cancellationToken = default)
    {
        int acquiredPermits = 0;
        try
        {
            for (; acquiredPermits < MaximumConcurrentDownloads;
                acquiredPermits++)
            {
                await downloadGate.WaitAsync(cancellationToken);
            }

            if (!Directory.Exists(options.DirectoryPath))
            {
                return;
            }

            foreach (string file in Directory.EnumerateFiles(
                options.DirectoryPath,
                "*",
                SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Delete(file);
            }
        }
        finally
        {
            if (acquiredPermits > 0)
            {
                downloadGate.Release(acquiredPermits);
            }
        }
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
        downloadGate.Dispose();
        foreach (SemaphoreSlim gate in itemGates.Values)
        {
            gate.Dispose();
        }
    }

    private async Task DownloadAsync(
        Uri sourceUri,
        string cachePath,
        CancellationToken cancellationToken)
    {
        await downloadGate.WaitAsync(cancellationToken);
        string? temporaryPath = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, sourceUri);
            request.Headers.TryAddWithoutValidation(
                "User-Agent",
                "FurinaChronicle/1.0");
            using HttpResponseMessage response =
                await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length &&
                length > options.MaximumFileBytes)
            {
                throw new InvalidDataException("The icon file is too large.");
            }

            string? directory = Path.GetDirectoryName(cachePath);
            Directory.CreateDirectory(
                directory ?? throw new InvalidOperationException(
                    "The icon cache path has no directory."));
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(cachePath)}.{Guid.NewGuid():N}.tmp");

            await using Stream source =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read = await source.ReadAsync(
                        buffer,
                        cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > options.MaximumFileBytes)
                    {
                        throw new InvalidDataException(
                            "The icon file is too large.");
                    }
                    await target.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);
                }
            }

            if (!IsSupportedImage(temporaryPath))
            {
                throw new InvalidDataException(
                    "The downloaded file is not a supported image.");
            }

            File.Move(temporaryPath, cachePath, overwrite: true);
            temporaryPath = null;
            File.SetLastWriteTimeUtc(
                cachePath,
                timeProvider.GetUtcNow().UtcDateTime);
        }
        finally
        {
            if (temporaryPath is not null && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
            downloadGate.Release();
        }
    }

    private bool IsFresh(string path)
    {
        DateTimeOffset lastWrite = new(
            File.GetLastWriteTimeUtc(path),
            TimeSpan.Zero);
        DateTimeOffset age = timeProvider.GetUtcNow() - options.RefreshInterval;
        return lastWrite >= age;
    }

    private string GetCachePath(GachaGame game, string itemId)
    {
        string fileName = IsSafeFileName(itemId)
            ? itemId
            : Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(itemId)))
                .ToLowerInvariant();
        return Path.Combine(
            options.DirectoryPath,
            ((int)game).ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{fileName}.png");
    }

    private static bool IsSafeFileName(string value)
    {
        return value.Length is > 0 and <= 100 &&
            value.All(character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_');
    }

    private static bool TryCreateSourceUri(
        string? sourceUrl,
        out Uri sourceUri)
    {
        string? normalizedUrl = sourceUrl?.Trim();
        if (normalizedUrl?.StartsWith("//", StringComparison.Ordinal) == true)
        {
            normalizedUrl = $"https:{normalizedUrl}";
        }
        if (Uri.TryCreate(
                normalizedUrl,
                UriKind.Absolute,
                out Uri? parsedUri) &&
            parsedUri.Scheme is "https" or "http")
        {
            sourceUri = parsedUri;
            return true;
        }
        sourceUri = null!;
        return false;
    }

    private static bool IsSupportedImage(string path)
    {
        Span<byte> header = stackalloc byte[12];
        using FileStream stream = File.OpenRead(path);
        int read = stream.Read(header);
        bool png = read >= 8 &&
            header[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        bool jpeg = read >= 3 &&
            header[0] == 0xFF &&
            header[1] == 0xD8 &&
            header[2] == 0xFF;
        bool webp = read >= 12 &&
            header[..4].SequenceEqual("RIFF"u8) &&
            header[8..12].SequenceEqual("WEBP"u8);
        return png || jpeg || webp;
    }
}

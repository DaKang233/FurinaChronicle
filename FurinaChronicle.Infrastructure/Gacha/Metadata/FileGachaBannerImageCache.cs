// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed class FileGachaBannerImageCache :
    IGachaBannerImageCache,
    IDisposable
{
    private readonly FileGachaItemIconCache inner;

    public FileGachaBannerImageCache(
        GachaBannerImageCacheOptions options,
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        inner = new FileGachaItemIconCache(
            new GachaItemIconCacheOptions(
                options.DirectoryPath,
                options.RefreshInterval,
                options.MaximumFileBytes),
            timeProvider,
            httpClient);
    }

    public async Task<string?> GetOrRefreshAsync(
        GachaGame game,
        string bannerId,
        string? sourceUrl,
        string? backupSourceUrl,
        CancellationToken cancellationToken = default)
    {
        string? path = await inner.GetOrRefreshAsync(
            game,
            bannerId,
            sourceUrl,
            cancellationToken);
        if (path is not null ||
            string.Equals(sourceUrl, backupSourceUrl, StringComparison.Ordinal))
        {
            return path;
        }

        return await inner.GetOrRefreshAsync(
            game,
            bannerId,
            backupSourceUrl,
            cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default) =>
        inner.ClearAsync(cancellationToken);

    public void Dispose() => inner.Dispose();
}

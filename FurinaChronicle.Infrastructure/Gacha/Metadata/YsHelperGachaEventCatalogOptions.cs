// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed record YsHelperGachaEventCatalogOptions
{
    public static readonly Uri DefaultEndpoint = new(
        "https://api.yshelper.com/ys/getWishHistory.php?lang=zh-Hans");

    public YsHelperGachaEventCatalogOptions(
        string cacheFilePath,
        TimeSpan? refreshInterval = null,
        long maximumResponseBytes = 2 * 1024 * 1024,
        Uri? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(cacheFilePath))
        {
            throw new ArgumentException(
                "Gacha event metadata cache path is required.",
                nameof(cacheFilePath));
        }
        if (refreshInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        }
        if (maximumResponseBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumResponseBytes));
        }

        CacheFilePath = Path.GetFullPath(cacheFilePath);
        RefreshInterval = refreshInterval ?? TimeSpan.FromDays(1);
        MaximumResponseBytes = maximumResponseBytes;
        Endpoint = endpoint ?? DefaultEndpoint;
    }

    public string CacheFilePath { get; }

    public TimeSpan RefreshInterval { get; }

    public long MaximumResponseBytes { get; }

    public Uri Endpoint { get; }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed record GachaBannerImageCacheOptions
{
    public GachaBannerImageCacheOptions(
        string directoryPath,
        TimeSpan? refreshInterval = null,
        long maximumFileBytes = 20 * 1024 * 1024)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException(
                "Banner cache directory is required.",
                nameof(directoryPath));
        }
        if (refreshInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(refreshInterval));
        }
        if (maximumFileBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileBytes));
        }

        DirectoryPath = Path.GetFullPath(directoryPath);
        RefreshInterval = refreshInterval ?? TimeSpan.FromDays(30);
        MaximumFileBytes = maximumFileBytes;
    }

    public string DirectoryPath { get; }

    public TimeSpan RefreshInterval { get; }

    public long MaximumFileBytes { get; }
}

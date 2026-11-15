// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Metadata;

public sealed record SnapMetadataOptions
{
    public SnapMetadataOptions(
        string cacheDirectory,
        Uri? baseAddress = null,
        TimeSpan? manifestCheckInterval = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        if (manifestCheckInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(manifestCheckInterval),
                "The manifest interval must be positive.");
        }

        CacheDirectory = cacheDirectory;
        BaseAddress = baseAddress ?? new Uri(
            "https://raw.githubusercontent.com/" +
            "SnapHutaoRemasteringProject/Snap.Metadata/main/Genshin/CHS/");
        ManifestCheckInterval =
            manifestCheckInterval ?? TimeSpan.FromHours(24);
    }

    public string CacheDirectory { get; }

    public Uri BaseAddress { get; }

    public TimeSpan ManifestCheckInterval { get; }
}

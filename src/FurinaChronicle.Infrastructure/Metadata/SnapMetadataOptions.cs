// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Metadata;

public sealed record SnapMetadataOptions
{
    public SnapMetadataOptions(
        string cacheDirectory,
        Uri? baseAddress = null,
        TimeSpan? manifestCheckInterval = null,
        Uri? monsterIconBaseAddress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        if (manifestCheckInterval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(manifestCheckInterval),
                "The manifest interval must be positive.");
        }

        MonsterIconBaseAddress = monsterIconBaseAddress ?? new Uri(
            "https://static.snaphutaorp.org/static/raw/MonsterIcon/");
        if (!MonsterIconBaseAddress.IsAbsoluteUri || MonsterIconBaseAddress.Scheme != Uri.UriSchemeHttps ||
            !MonsterIconBaseAddress.IsDefaultPort || MonsterIconBaseAddress.UserInfo.Length > 0 ||
            MonsterIconBaseAddress.Query.Length > 0 || MonsterIconBaseAddress.Fragment.Length > 0 ||
            !MonsterIconBaseAddress.AbsolutePath.EndsWith('/'))
            throw new ArgumentException("The monster icon base must be an HTTPS directory URI.", nameof(monsterIconBaseAddress));
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

    public Uri MonsterIconBaseAddress { get; }
}

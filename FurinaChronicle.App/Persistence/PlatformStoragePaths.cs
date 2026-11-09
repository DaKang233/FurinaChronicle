// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.Persistence;

internal static class PlatformStoragePaths
{
    // Windows validation redirects every local storage category before MAUI's unpackaged
    // Preferences static initializer can open its default AppData directory.
    public static string AppDataDirectory => GetDirectory(cache: false);
    public static string CacheDirectory => GetDirectory(cache: true);

    private static string GetDirectory(bool cache)
    {
#if WINDOWS && (PHASE8C_VALIDATION || PHASE8B_VALIDATION)
#if PHASE8C_VALIDATION
        const string variable = "FURINA_PHASE8C6_DIRECTORY";
#else
        const string variable = "FURINA_PHASE8B7_DIRECTORY";
#endif
        string? root = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException($"{variable} must name an explicit absolute validation directory.");
        string directory = Path.Combine(Path.GetFullPath(root), cache ? "cache" : "app-data");
        Directory.CreateDirectory(directory);
        return directory;
#else
        return cache ? FileSystem.CacheDirectory : FileSystem.AppDataDirectory;
#endif
    }
}

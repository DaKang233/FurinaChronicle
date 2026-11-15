// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.App.Composition;

// MAUI host ports for the shared UI-4 settings state. Every path is the app-private
// storage location of this package; no production database or credential store is touched.
public sealed class MauiSettingsAppearanceStore(IPreferences preferences)
    : ISettingsAppearanceStore
{
    private const string ThemeKey = "ui5.settings.theme";

    public Task<SettingsTheme> LoadThemeAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LoadPersistedTheme());

    public Task SaveThemeAsync(
        SettingsTheme theme,
        CancellationToken cancellationToken = default)
    {
        preferences.Set(ThemeKey, theme.ToString());
        ApplyTheme(theme);
        return Task.CompletedTask;
    }

    public SettingsTheme LoadPersistedTheme()
    {
        string text = preferences.Get(ThemeKey, string.Empty);
        return Enum.TryParse(text, ignoreCase: true, out SettingsTheme theme) &&
            Enum.IsDefined(theme)
            ? theme
            : SettingsTheme.FollowSystem;
    }

    public void ApplyPersistedTheme() => ApplyTheme(LoadPersistedTheme());

    private static void ApplyTheme(SettingsTheme theme)
    {
        Application? application = Application.Current;
        if (application is null)
        {
            return;
        }

        application.UserAppTheme = theme switch
        {
            SettingsTheme.Light => AppTheme.Light,
            SettingsTheme.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };
    }
}

public sealed class MauiSettingsStoragePort(
    SqliteDatabaseOptions database,
    GachaItemIconCacheOptions iconCache,
    IGachaItemIconCache iconCacheService) : ISettingsStoragePort
{
    private const string DiagnosticsFolder = "diagnostics";

    public string DataRootPath => PlatformStoragePaths.AppDataDirectory;

    public string DatabasePath => database.DatabasePath;

    public string CacheDirectoryPath => iconCache.DirectoryPath;

    public long? GetDatabaseSizeBytes() => TryGetFileSize(database.DatabasePath);

    public long? GetCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(iconCache.DirectoryPath))
            {
                return 0;
            }

            long total = 0;
            foreach (string file in Directory.EnumerateFiles(
                iconCache.DirectoryPath,
                "*",
                SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    // A cache file can disappear between enumeration and stat.
                }
            }

            return total;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public Task ClearIconCacheAsync(CancellationToken cancellationToken = default) =>
        iconCacheService.ClearAsync(cancellationToken);

    public async Task<string> ExportDiagnosticsAsync(
        string versionText,
        string phaseLabel,
        string commit,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(
            PlatformStoragePaths.CacheDirectory,
            DiagnosticsFolder);
        Directory.CreateDirectory(directory);
        string fileName = $"android-ui45-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        string path = Path.Combine(directory, fileName);
        // Only identifiers and sizes: never record content, credential values,
        // device identifiers or full user paths.
        string content =
            "FurinaChronicle Android isolated diagnostics" + Environment.NewLine +
            $"Version: {versionText}" + Environment.NewLine +
            $"Phase: {phaseLabel}" + Environment.NewLine +
            $"Commit: {commit}" + Environment.NewLine +
            $"DatabaseFile: {Path.GetFileName(database.DatabasePath)}" + Environment.NewLine +
            $"DatabaseBytes: {GetDatabaseSizeBytes()?.ToString() ?? "unknown"}" + Environment.NewLine +
            $"CacheBytes: {GetCacheSizeBytes()?.ToString() ?? "unknown"}" + Environment.NewLine +
            "DataContent: omitted" + Environment.NewLine +
            "Credentials: not used by this host" + Environment.NewLine +
            "ProductionDatabase: not discovered" + Environment.NewLine;
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return fileName;
    }

    private static long? TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public sealed class MauiSettingsApplicationPort : ISettingsApplicationPort
{
    private const string LicenseResource = "FurinaChronicle.LICENSE.txt";
    private const string ThirdPartyResource =
        "FurinaChronicle.THIRD-PARTY-NOTICES.txt";

    public MauiSettingsApplicationPort(ApplicationVersionInfo version)
    {
        ArgumentNullException.ThrowIfNull(version);
        VersionText = version.UserVisibleName;
        PhaseLabel = version.PhaseLabel;
        Commit = version.Commit;
        LicenseText = ReadEmbeddedText(
            LicenseResource,
            "MIT License。完整文本请查看仓库根目录 LICENSE.txt。");
        ThirdPartyNoticesText = ReadEmbeddedText(
            ThirdPartyResource,
            "第三方声明未随包提供。请查看仓库根目录 THIRD-PARTY-NOTICES.txt。");
    }

    public string VersionText { get; }

    public string PhaseLabel { get; }

    public string Commit { get; }

    public string LicenseText { get; }

    public string ThirdPartyNoticesText { get; }

    private static string ReadEmbeddedText(string resourceName, string fallback)
    {
        try
        {
            using Stream? stream = typeof(MauiSettingsApplicationPort)
                .Assembly
                .GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                return fallback;
            }

            using var reader = new StreamReader(stream);
            string text = reader.ReadToEnd();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

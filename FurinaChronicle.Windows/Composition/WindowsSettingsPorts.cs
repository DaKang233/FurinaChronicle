// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Windows.Shell;

namespace FurinaChronicle.Windows.Composition;

// Isolated-host settings persistence. Appearance is stored in the preview settings directory,
// never in the shared MAUI Preferences keys, so the two front ends cannot silently share state.
public sealed class JsonAppearanceSettingsStore(WindowsPaths paths) : ISettingsAppearanceStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly string filePath = Path.Combine(
        paths.SettingsDirectory,
        "ui-preferences.json");

    public async Task<SettingsTheme> LoadThemeAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath)) return SettingsTheme.FollowSystem;
            try
            {
                string json = await File.ReadAllTextAsync(filePath, cancellationToken);
                PreferencesDto? dto = JsonSerializer.Deserialize<PreferencesDto>(json);
                return dto?.Theme is { } text &&
                    Enum.TryParse(text, ignoreCase: true, out SettingsTheme theme) &&
                    Enum.IsDefined(theme)
                    ? theme
                    : SettingsTheme.FollowSystem;
            }
            catch (Exception)
            {
                return SettingsTheme.FollowSystem;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task SaveThemeAsync(
        SettingsTheme theme,
        CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(paths.SettingsDirectory);
            string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(new PreferencesDto(theme.ToString())),
                cancellationToken);
            File.Move(temporary, filePath, overwrite: true);
        }
        finally
        {
            gate.Release();
        }
    }

    private sealed record PreferencesDto(string? Theme);
}

// Storage/application ports for the settings page. Only cache directories are deleted; database
// facts, metadata and credentials are never touched. Diagnostics deliberately contain no record
// content, credential values or full user paths.
public sealed class WindowsSettingsStoragePort(
    WindowsPaths paths,
    WindowsHostEnvironment environment) : ISettingsStoragePort
{
    public string DataRootPath => environment.RootDirectory;

    public string DatabasePath => environment.DatabasePath;

    public string CacheDirectoryPath => environment.ImageCacheDirectory;

    public long? GetDatabaseSizeBytes()
    {
        try
        {
            var info = new FileInfo(environment.DatabasePath);
            return info.Exists ? info.Length : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public long? GetCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(environment.ImageCacheDirectory)) return 0;
            long total = 0;
            foreach (string file in Directory.EnumerateFiles(
                environment.ImageCacheDirectory,
                "*",
                SearchOption.AllDirectories))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (IOException)
                {
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
        environment.GachaIconCache.ClearAsync(cancellationToken);

    public async Task<string> ExportDiagnosticsAsync(
        string versionText,
        string phaseLabel,
        string commit,
        CancellationToken cancellationToken = default)
    {
        string directory = Path.Combine(paths.SettingsDirectory, "diagnostics");
        Directory.CreateDirectory(directory);
        string fileName = $"ui4-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.txt";
        string path = Path.Combine(directory, fileName);
        string content =
            "FurinaChronicle isolated WinUI diagnostics" + Environment.NewLine +
            $"Version: {versionText}" + Environment.NewLine +
            $"Phase: {phaseLabel}" + Environment.NewLine +
            $"Commit: {commit}" + Environment.NewLine +
            $"Database: {Path.GetFileName(environment.DatabasePath)}" + Environment.NewLine +
            $"DatabaseBytes: {GetDatabaseSizeBytes()?.ToString() ?? "unknown"}" + Environment.NewLine +
            $"CacheBytes: {GetCacheSizeBytes()?.ToString() ?? "unknown"}" + Environment.NewLine +
            "DataContent: omitted" + Environment.NewLine +
            "Credentials: DPAPI local (values omitted)" + Environment.NewLine +
            $"DatabaseMode: {(environment.IsSynthetic ? "Isolated" : "Production")}" + Environment.NewLine +
            "ProductionDatabase: same-as-MAUI (path omitted)" + Environment.NewLine;
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return fileName;
    }
}

public sealed class WindowsSettingsApplicationPort : ISettingsApplicationPort
{
    private readonly string licenseText;
    private readonly string thirdPartyText;

    public WindowsSettingsApplicationPort(WindowsVersionInfo version)
    {
        VersionText = version.UserVisibleName;
        PhaseLabel = version.PhaseLabel;
        Commit = version.Commit;
        licenseText = ReadNotice("LICENSE.txt", "MIT License。完整文本请查看仓库根目录 LICENSE.txt。");
        thirdPartyText = ReadNotice(
            "THIRD-PARTY-NOTICES.txt",
            "第三方声明未随包提供。请查看仓库根目录 THIRD-PARTY-NOTICES.txt。");
    }

    public string VersionText { get; }

    public string PhaseLabel { get; }

    public string Commit { get; }

    public string LicenseText => licenseText;

    public string ThirdPartyNoticesText => thirdPartyText;

    private static string ReadNotice(string fileName, string fallback)
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

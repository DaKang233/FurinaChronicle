// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Windows.Composition;

// Resolves the one data root the Windows host may open. There are exactly two modes:
//  - explicit FURINA_UI1_DIRECTORY => isolated synthetic host (never probes production);
//  - otherwise => the MAUI production app-data root and the same furinachronicle.db3 file.
// No file contents are read here; only path composition and metadata are used.
public sealed class WindowsPaths
{
    public const string PreviewDirectoryVariable = "FURINA_UI1_DIRECTORY";
    public const string ProductionDirectoryVariable = "FURINA_UI6_PRODUCTION_DIRECTORY";

    public WindowsPaths()
    {
        string? isolated = Environment.GetEnvironmentVariable(PreviewDirectoryVariable);
        string? production = Environment.GetEnvironmentVariable(ProductionDirectoryVariable);
        IsIsolated = !string.IsNullOrWhiteSpace(isolated);
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "User Name",
            "com.companyname.furinachronicle.app");
        string configured = IsIsolated ? isolated! : string.IsNullOrWhiteSpace(production)
            ? fallback
            : production;
        try
        {
            BaseDirectory = Path.GetFullPath(configured);
        }
        catch (Exception)
        {
            BaseDirectory = Path.GetFullPath(fallback);
            ConfigurationError = IsIsolated
                ? $"{PreviewDirectoryVariable} 不是有效路径；请指定一个绝对可写目录。"
                : $"{ProductionDirectoryVariable} 不是有效路径；请指定一个绝对目录。";
        }

        if (IsIsolated)
        {
            DataRoot = Path.Combine(BaseDirectory, "UI1", "WinUI");
            DatabasePath = Path.Combine(DataRoot, "ui1-preview.db3");
            MetadataDirectory = Path.Combine(DataRoot, "metadata");
            ImageCacheDirectory = Path.Combine(DataRoot, "images");
            TemporaryDirectory = Path.Combine(DataRoot, "temp");
        }
        else
        {
            DataRoot = BaseDirectory;
            DatabasePath = Path.Combine(DataRoot, "Data", "furinachronicle.db3");
            MetadataDirectory = Path.Combine(DataRoot, "Data", "metadata");
            ImageCacheDirectory = Path.Combine(DataRoot, "Cache", "images");
            TemporaryDirectory = Path.Combine(DataRoot, "Cache", "ui6-temp");
        }

        SettingsDirectory = Path.Combine(BaseDirectory, "UI4", "WinUI", "settings");
        SelectionFilePath = Path.Combine(SettingsDirectory, "archive-selection.json");
        CredentialDirectory = Path.Combine(BaseDirectory, "UI4", "WinUI", "credentials");
        CredentialFilePath = Path.Combine(CredentialDirectory, "credentials.bin");
        BackupsDirectory = Path.Combine(DataRoot, "Backups");
        AdoptionMarkerPath = Path.Combine(DataRoot, "UI6", "host-adoption.json");
        SessionLockPath = Path.Combine(DataRoot, "session.lock");
    }

    // Kept for callers that need the configured root (isolated root or production data root).
    public string BaseDirectory { get; }

    // The actual single-writer/data root: production app-data root or UI1/WinUI.
    public string DataRoot { get; }

    public bool IsIsolated { get; }

    public bool IsProduction => !IsIsolated;

    public string DatabasePath { get; }

    public string MetadataDirectory { get; }

    public string ImageCacheDirectory { get; }

    public string TemporaryDirectory { get; }

    public string SettingsDirectory { get; }

    public string SelectionFilePath { get; }

    public string CredentialDirectory { get; }

    public string CredentialFilePath { get; }

    public string BackupsDirectory { get; }

    public string AdoptionMarkerPath { get; }

    public string SessionLockPath { get; }

    public string? ConfigurationError { get; }
}

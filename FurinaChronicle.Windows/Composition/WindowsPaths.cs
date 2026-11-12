// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Windows.Composition;

// Resolves only the isolated preview root. Production database discovery is deliberately absent
// (ADR 0020): UI-4 still runs exclusively on the synthetic UI-1 database.
public sealed class WindowsPaths
{
    public const string PreviewDirectoryVariable = "FURINA_UI1_DIRECTORY";

    public WindowsPaths()
    {
        string? configured = Environment.GetEnvironmentVariable(PreviewDirectoryVariable);
        string fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FurinaChronicle");
        try
        {
            BaseDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
                ? fallback
                : configured);
        }
        catch (Exception)
        {
            // Never throw from Application.OnLaunched for a malformed environment variable;
            // the startup guard reports it on the failure page instead.
            BaseDirectory = Path.GetFullPath(fallback);
            ConfigurationError =
                $"{PreviewDirectoryVariable} 不是有效路径；请指定一个绝对可写目录。";
        }

        SettingsDirectory = Path.Combine(BaseDirectory, "UI4", "WinUI", "settings");
        SelectionFilePath = Path.Combine(SettingsDirectory, "archive-selection.json");
        CredentialDirectory = Path.Combine(BaseDirectory, "UI4", "WinUI", "credentials");
        CredentialFilePath = Path.Combine(CredentialDirectory, "credentials.bin");
        Ui6Directory = Path.Combine(BaseDirectory, "UI6");
        MigrationTargetDirectory = Path.Combine(Ui6Directory, "Data");
        MigrationTargetDatabasePath = Path.Combine(
            MigrationTargetDirectory,
            "furinachronicle.db3");
        MigrationBackupDirectory = Path.Combine(Ui6Directory, "backups");
        MigrationMarkerPath = Path.Combine(Ui6Directory, "migration.json");
        SessionLockPath = Path.Combine(BaseDirectory, "session.lock");
    }

    public string BaseDirectory { get; }

    public string SettingsDirectory { get; }

    public string SelectionFilePath { get; }

    public string CredentialDirectory { get; }

    public string CredentialFilePath { get; }

    public string Ui6Directory { get; }

    public string MigrationTargetDirectory { get; }

    public string MigrationTargetDatabasePath { get; }

    public string MigrationBackupDirectory { get; }

    public string MigrationMarkerPath { get; }

    public string SessionLockPath { get; }

    public string? ConfigurationError { get; }
}

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
        BaseDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FurinaChronicle")
            : configured);
        SettingsDirectory = Path.Combine(BaseDirectory, "UI4", "WinUI", "settings");
        SelectionFilePath = Path.Combine(SettingsDirectory, "archive-selection.json");
    }

    public string BaseDirectory { get; }

    public string SettingsDirectory { get; }

    public string SelectionFilePath { get; }
}

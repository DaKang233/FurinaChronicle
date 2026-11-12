// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace FurinaChronicle.Windows.Shell;

// Windows-host view of the shared version metadata (no MAUI dependency). Used for the window
// title, startup diagnostics and the settings placeholder.
public sealed record WindowsVersionInfo(
    string Version,
    string DisplayVersion,
    string PhaseLabel,
    string Commit,
    string Dirty,
    string Source,
    bool IsReliable)
{
    public const string ProductName = "FurinaChronicle";

    public string UserVisibleName => $"{ProductName} · {DisplayVersion}";

    public string DiagnosticText =>
        $"Version: {Version}{Environment.NewLine}" +
        $"Phase: {PhaseLabel}{Environment.NewLine}" +
        $"Commit: {Commit}{Environment.NewLine}" +
        $"Dirty: {Dirty}";

    public static WindowsVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        Dictionary<string, string> metadata = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .GroupBy(attribute => attribute.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value ?? string.Empty,
                StringComparer.Ordinal);
        string GetValue(string key, string fallback) =>
            metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : fallback;
        string assemblyVersion = assembly.GetName().Version?.ToString() ?? "unknown";
        string version = GetValue("FurinaVersion", assemblyVersion);
        return new WindowsVersionInfo(
            version,
            GetValue("FurinaVersionPrefix", version.Split('+', StringSplitOptions.RemoveEmptyEntries)[0]),
            GetValue("FurinaPhaseLabel", "unknown"),
            GetValue("FurinaGitCommit", "unknown"),
            GetValue("FurinaGitDirty", "unknown"),
            GetValue("FurinaVersionSource", "unknown"),
            bool.TryParse(GetValue("FurinaVersionReliable", "false"), out bool reliable) && reliable);
    }
}

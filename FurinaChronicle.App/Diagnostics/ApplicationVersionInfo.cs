// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Reflection;

namespace FurinaChronicle.App.Diagnostics;

public sealed record ApplicationVersionInfo(
    string Version,
    string DisplayVersion,
    string PhaseLabel,
    string Commit,
    string Dirty,
    string Source,
    bool IsReliable)
{
    public string DiagnosticText =>
        $"Version: {Version}{Environment.NewLine}" +
        $"Phase: {PhaseLabel}{Environment.NewLine}" +
        $"Commit: {Commit}{Environment.NewLine}" +
        $"Dirty: {Dirty}";

    public static ApplicationVersionInfo FromAssembly(Assembly assembly)
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
        string displayVersion = GetValue(
            "FurinaVersionPrefix",
            version.Split('+', StringSplitOptions.RemoveEmptyEntries)[0]);
        string reliableText = GetValue("FurinaVersionReliable", "false");

        return new ApplicationVersionInfo(
            version,
            displayVersion,
            GetValue("FurinaPhaseLabel", "unknown"),
            GetValue("FurinaGitCommit", "unknown"),
            GetValue("FurinaGitDirty", "unknown"),
            GetValue("FurinaVersionSource", "unknown"),
            bool.TryParse(reliableText, out bool reliable) && reliable);
    }
}

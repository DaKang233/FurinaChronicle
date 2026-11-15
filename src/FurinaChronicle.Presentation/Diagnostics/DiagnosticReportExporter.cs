// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;

namespace FurinaChronicle.Presentation.Diagnostics;

public interface IDiagnosticReportExporter
{
    /// <summary>Lets the user preview/save the local diagnostic report; null when cancelled.</summary>
    Task<string?> ExportAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Builds the local diagnostic report text. It contains only the persisted/sanitized
/// reports and log file names; it never reads credentials or business database rows.
/// </summary>
public static class DiagnosticReportFormatter
{
    public static string Build(
        IDiagnosticService diagnostics,
        string logDirectory,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var builder = new StringBuilder();
        DateTimeOffset generated = now ?? DateTimeOffset.UtcNow;
        builder.AppendLine("FurinaChronicle Windows diagnostic report");
        builder.AppendLine($"Generated: {generated:O}");
        builder.AppendLine($"Log directory: {logDirectory}");
        builder.AppendLine();
        builder.AppendLine("Recent diagnostics (newest first):");
        IReadOnlyList<DiagnosticReport> reports = diagnostics.Recent;
        if (reports.Count == 0)
        {
            builder.AppendLine("(none)");
        }
        else
        {
            foreach (DiagnosticReport report in reports)
            {
                builder.AppendLine(
                    $"[{report.DiagnosticId}] {report.OccurredAt:O} " +
                    $"{report.Severity}/{report.Category}/{report.Operation} " +
                    $"{report.Title}: {report.Message}");
                if (!string.IsNullOrWhiteSpace(report.ExceptionType))
                {
                    builder.AppendLine($"  exception: {report.ExceptionType}");
                }
                if (!string.IsNullOrWhiteSpace(report.StackTrace))
                {
                    foreach (string line in report.StackTrace.Split('\n'))
                    {
                        builder.AppendLine("  " + line.TrimEnd('\r'));
                    }
                }
                if (report.LogWriteFailed)
                {
                    builder.AppendLine("  log-write: failed");
                }
            }
        }

        builder.AppendLine();
        builder.AppendLine("Log files:");
        try
        {
            if (Directory.Exists(logDirectory))
            {
                foreach (string file in Directory
                    .EnumerateFiles(logDirectory, "diagnostics*.jsonl")
                    .OrderBy(path => path, StringComparer.Ordinal))
                {
                    builder.AppendLine($"  {Path.GetFileName(file)}");
                }
            }
            else
            {
                builder.AppendLine("  (log directory does not exist)");
            }
        }
        catch (Exception)
        {
            builder.AppendLine("  (log files unavailable)");
        }

        return builder.ToString();
    }
}

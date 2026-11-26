// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Encodings.Web;
using System.Text.Json;

namespace FurinaChronicle.Presentation.Diagnostics;

/// <summary>
/// Append-only JSON-lines diagnostic log with size-based rotation and a bounded number
/// of archive files. The reports are already sanitized by <see cref="DiagnosticService"/>.
/// </summary>
public sealed class FileDiagnosticLog : IDiagnosticLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private readonly object gate = new();
    private readonly string directory;
    private readonly long maxBytes;
    private readonly int maxFiles;

    public FileDiagnosticLog(string directory, long maxBytes = 1_000_000, int maxFiles = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        }
        if (maxFiles <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFiles));
        }
        this.directory = Path.GetFullPath(directory);
        this.maxBytes = maxBytes;
        this.maxFiles = maxFiles;
    }

    public string DirectoryPath => directory;

    public string ActiveFilePath => Path.Combine(directory, "diagnostics.jsonl");

    public void Write(DiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        string line = JsonSerializer.Serialize(report, JsonOptions) + "\n";
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(line);
        lock (gate)
        {
            Directory.CreateDirectory(directory);
            string active = ActiveFilePath;
            if (File.Exists(active) && new FileInfo(active).Length + bytes.Length > maxBytes)
            {
                RotateLocked(active);
            }
            using var stream = new FileStream(
                active,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
    }

    private void RotateLocked(string active)
    {
        string stamp = DateTimeOffset.UtcNow.ToString(
            "yyyyMMdd-HHmmss-fff",
            System.Globalization.CultureInfo.InvariantCulture);
        string archive = Path.Combine(directory, $"diagnostics-{stamp}.jsonl");
        for (int suffix = 1; ; suffix++)
        {
            try
            {
                File.Move(active, archive, overwrite: false);
                break;
            }
            catch (IOException) when (File.Exists(archive))
            {
                archive = Path.Combine(directory, $"diagnostics-{stamp}_{suffix:D6}.jsonl");
            }
        }
        string[] archives = Directory
            .EnumerateFiles(directory, "diagnostics-*.jsonl")
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        for (int index = 0; index < archives.Length - (maxFiles - 1); index++)
        {
            File.Delete(archives[index]);
        }
    }
}

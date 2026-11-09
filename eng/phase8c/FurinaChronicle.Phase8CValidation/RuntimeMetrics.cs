// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;

internal sealed class RuntimeMetrics(
    long baselineWorkingSet,
    Func<string, IEnumerable<string>>? enumerateFiles = null,
    Func<string, long>? readLength = null)
{
    private readonly object gate = new();
    private readonly Func<string, IEnumerable<string>> enumerate =
        enumerateFiles ?? (root =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
    private readonly Func<string, long> length =
        readLength ?? (path => new FileInfo(path).Length);

    public long BaselineWorkingSet { get; } = baselineWorkingSet;
    public long PeakWorkingSet { get; private set; } = baselineWorkingSet;
    public long PeakTemporaryBytes { get; private set; }

    public void Sample(string root)
    {
        lock (gate)
        {
            PeakWorkingSet = Math.Max(
                PeakWorkingSet,
                Process.GetCurrentProcess().WorkingSet64);
            long temporaryBytes = 0;
            try
            {
                foreach (string path in enumerate(root))
                {
                    try
                    {
                        temporaryBytes = checked(temporaryBytes + length(path));
                    }
                    catch (FileNotFoundException)
                    {
                        // SQLite may remove its journal between enumeration and stat.
                    }
                    catch (DirectoryNotFoundException)
                    {
                        // A staged session may be disposed during a sample.
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // Enumeration is lazy; a directory can disappear during MoveNext.
            }
            PeakTemporaryBytes = Math.Max(PeakTemporaryBytes, temporaryBytes);
        }
    }
}

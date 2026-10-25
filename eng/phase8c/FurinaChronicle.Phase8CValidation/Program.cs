// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using System.Text;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Services.Archives.Portable;

return await Phase8CValidationTool.RunAsync(args);

internal static class Phase8CValidationTool
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 3 &&
            string.Equals(
                args[0],
                "generate",
                StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateAsync(args[1], args[2]);
        }
        if (args.Length is < 3 or > 5 ||
            !string.Equals(args[0], "compare", StringComparison.OrdinalIgnoreCase))
        {
            WriteUsage();
            return 64;
        }

        string expectedPath = Path.GetFullPath(args[1]);
        string actualPath = Path.GetFullPath(args[2]);
        string? reportPath = null;
        if (args.Length > 3)
        {
            if (args.Length != 5 ||
                !string.Equals(args[3], "--report", StringComparison.Ordinal))
            {
                WriteUsage();
                return 64;
            }
            reportPath = Path.GetFullPath(args[4]);
        }

        if (!File.Exists(expectedPath) || !File.Exists(actualPath))
        {
            Console.Error.WriteLine("One or both Archive files do not exist.");
            return 66;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            ValidationReport report = await CompareAsync(
                expectedPath,
                actualPath,
                cancellation.Token);
            string markdown = report.ToMarkdown();
            Console.WriteLine(markdown);
            if (reportPath is not null)
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(reportPath) ??
                    throw new InvalidOperationException(
                        "The report path has no parent directory."));
                await File.WriteAllTextAsync(
                    reportPath,
                    markdown,
                    new UTF8Encoding(false),
                    cancellation.Token);
                Console.WriteLine($"Report: {reportPath}");
            }
            return report.Comparison.IsEquivalent ? 0 : 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Archive validation was cancelled.");
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"{exception.GetType().Name}: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> GenerateAsync(
        string origin,
        string output)
    {
        if (origin is not ("windows" or "android"))
        {
            Console.Error.WriteLine(
                "Synthetic origin must be windows or android.");
            return 64;
        }

        string outputPath = Path.GetFullPath(output);
        Directory.CreateDirectory(
            Path.GetDirectoryName(outputPath) ??
            throw new InvalidOperationException(
                "The output path has no parent directory."));
        await using SyntheticArchiveSnapshot snapshot =
            SyntheticArchiveSnapshot.Create(origin);
        await using var destination = new FileStream(
            outputPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        ArchiveWriteResult result = await new ArchivePackageWriter().WriteAsync(
            destination,
            snapshot,
            new ArchiveGeneratorInfo(
                "FurinaChronicle 8C.6 Validation",
                "0.8.15",
                "8C.6",
                "synthetic",
                origin));
        Console.WriteLine($"Archive: {outputPath}");
        Console.WriteLine($"SHA-256: {result.PackageSha256}");
        Console.WriteLine($"Container bytes: {destination.Length}");
        Console.WriteLine(
            $"Declared uncompressed bytes: {result.Summary.UncompressedLength}");
        return 0;
    }

    private static async Task<ValidationReport> CompareAsync(
        string expectedPath,
        string actualPath,
        CancellationToken cancellationToken)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicle",
            "phase8c6",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var monitorCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var metrics = new RuntimeMetrics(
            Process.GetCurrentProcess().WorkingSet64);
        Task monitor = MonitorAsync(
            root,
            metrics,
            monitorCancellation.Token);
        var timer = Stopwatch.StartNew();
        try
        {
            var stager = new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(),
                new ArchiveRestoreStorageOptions(root));
            await using FileStream expectedStream = OpenRead(expectedPath);
            await using IArchiveRestoreStagedPackage expected =
                await stager.StageAsync(expectedStream, cancellationToken);
            await using FileStream actualStream = OpenRead(actualPath);
            await using IArchiveRestoreStagedPackage actual =
                await stager.StageAsync(actualStream, cancellationToken);
            ArchiveSemanticComparison comparison =
                await new ArchiveSemanticComparer().CompareAsync(
                    expected,
                    actual,
                    cancellationToken);
            timer.Stop();
            metrics.Sample(root);
            return new ValidationReport(
                expectedPath,
                actualPath,
                new PackageMetrics(
                    expected.PackageFingerprint,
                    expected.ContainerLength,
                    expected.Summary.UncompressedLength,
                    expected.StagingLength),
                new PackageMetrics(
                    actual.PackageFingerprint,
                    actual.ContainerLength,
                    actual.Summary.UncompressedLength,
                    actual.StagingLength),
                metrics.BaselineWorkingSet,
                metrics.PeakWorkingSet,
                metrics.PeakTemporaryBytes,
                timer.Elapsed,
                comparison);
        }
        finally
        {
            monitorCancellation.Cancel();
            try
            {
                await monitor;
            }
            catch (OperationCanceledException)
            {
            }
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        128 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static async Task MonitorAsync(
        string root,
        RuntimeMetrics metrics,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            metrics.Sample(root);
            await Task.Delay(20, cancellationToken);
        }
    }

    private static void WriteUsage()
    {
        Console.Error.WriteLine(
            "Usage: dotnet run --project " +
            "eng/phase8c/FurinaChronicle.Phase8CValidation -- " +
            "generate <windows|android> <output.fca>");
        Console.Error.WriteLine(
            "Usage: dotnet run --project " +
            "eng/phase8c/FurinaChronicle.Phase8CValidation -- " +
            "compare <expected.fca> <actual.fca> [--report <report.md>]");
    }
}

internal sealed class RuntimeMetrics(long baselineWorkingSet)
{
    public long BaselineWorkingSet { get; } = baselineWorkingSet;

    public long PeakWorkingSet { get; private set; } = baselineWorkingSet;

    public long PeakTemporaryBytes { get; private set; }

    public void Sample(string root)
    {
        PeakWorkingSet = Math.Max(
            PeakWorkingSet,
            Process.GetCurrentProcess().WorkingSet64);
        long temporaryBytes = Directory.Exists(root)
            ? Directory.EnumerateFiles(
                    root,
                    "*",
                    SearchOption.AllDirectories)
                .Sum(path => new FileInfo(path).Length)
            : 0;
        PeakTemporaryBytes = Math.Max(PeakTemporaryBytes, temporaryBytes);
    }
}

internal sealed record PackageMetrics(
    string Sha256,
    long ContainerBytes,
    long DeclaredUncompressedBytes,
    long StagingBytes);

internal sealed record ValidationReport(
    string ExpectedPath,
    string ActualPath,
    PackageMetrics Expected,
    PackageMetrics Actual,
    long BaselineWorkingSetBytes,
    long PeakWorkingSetBytes,
    long PeakTemporaryBytes,
    TimeSpan Elapsed,
    ArchiveSemanticComparison Comparison)
{
    public string ToMarkdown()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# FurinaChronicle Phase 8C.6 Archive validation");
        builder.AppendLine();
        builder.AppendLine(
            $"- Result: **{(Comparison.IsEquivalent ? "Equivalent" : "Different")}**");
        builder.AppendLine($"- Expected: `{ExpectedPath}`");
        builder.AppendLine($"- Actual: `{ActualPath}`");
        builder.AppendLine($"- Elapsed: {Elapsed.TotalMilliseconds:F0} ms");
        builder.AppendLine(
            $"- Process working set: {FormatBytes(BaselineWorkingSetBytes)} " +
            $"baseline / {FormatBytes(PeakWorkingSetBytes)} sampled peak");
        builder.AppendLine(
            $"- Temporary storage sampled peak: {FormatBytes(PeakTemporaryBytes)}");
        builder.AppendLine();
        builder.AppendLine("| Side | SHA-256 | Container | Declared uncompressed | Staging |");
        builder.AppendLine("|---|---|---:|---:|---:|");
        AppendPackage(builder, "Expected", Expected);
        AppendPackage(builder, "Actual", Actual);
        builder.AppendLine();
        builder.AppendLine("## Semantic differences");
        builder.AppendLine();
        if (Comparison.Differences.Count == 0)
        {
            builder.AppendLine("None.");
        }
        else
        {
            builder.AppendLine("| Path | Expected | Actual | Value digest |");
            builder.AppendLine("|---|---:|---:|---|");
            foreach (ArchiveSemanticDifference difference in
                     Comparison.Differences)
            {
                builder.Append("| `")
                    .Append(difference.Path.Replace("|", "`|"))
                    .Append("` | ")
                    .Append(difference.ExpectedCount)
                    .Append(" | ")
                    .Append(difference.ActualCount)
                    .Append(" | `")
                    .Append(difference.ValueDigest)
                    .AppendLine("` |");
            }
        }
        builder.AppendLine();
        builder.AppendLine(
            "> Different container hashes are expected when export metadata or " +
            "target-local identifiers differ. Equivalence is determined by the " +
            "strict logical comparer.");
        return builder.ToString();
    }

    private static void AppendPackage(
        StringBuilder builder,
        string name,
        PackageMetrics metrics)
    {
        builder.Append("| ")
            .Append(name)
            .Append(" | `")
            .Append(metrics.Sha256)
            .Append("` | ")
            .Append(FormatBytes(metrics.ContainerBytes))
            .Append(" | ")
            .Append(FormatBytes(metrics.DeclaredUncompressedBytes))
            .Append(" | ")
            .Append(FormatBytes(metrics.StagingBytes))
            .AppendLine(" |");
    }

    private static string FormatBytes(long value)
    {
        string[] suffixes = ["B", "KiB", "MiB", "GiB"];
        double display = value;
        int suffix = 0;
        while (display >= 1024 && suffix < suffixes.Length - 1)
        {
            display /= 1024;
            suffix++;
        }
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{display:F2} {suffixes[suffix]}");
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Tests.Validation;

public sealed class RuntimeMetricsTests
{
    [Fact]
    public void RemovedFileBetweenEnumerationAndStatDoesNotAbortComparisonMetrics()
    {
        var metrics = new RuntimeMetrics(1,
            _ => ["staging.db", "staging.db-journal", "other.db"],
            path => path == "staging.db-journal"
                ? throw new FileNotFoundException()
                : 4096);

        metrics.Sample("synthetic");

        Assert.Equal(8192, metrics.PeakTemporaryBytes);
        Assert.True(metrics.PeakWorkingSet > 0);
    }

    [Fact]
    public void RemovedDirectoryDuringLazyEnumerationKeepsPartialSample()
    {
        var metrics = new RuntimeMetrics(1, _ => Paths(), _ => 8192);
        metrics.Sample("synthetic");
        Assert.Equal(8192, metrics.PeakTemporaryBytes);

        static IEnumerable<string> Paths()
        {
            yield return "staging.db";
            throw new DirectoryNotFoundException();
        }
    }

    [Fact]
    public void RemovedDirectoryDuringStatIsSkippedAndPeakRemainsMonotonic()
    {
        bool removed = false;
        var metrics = new RuntimeMetrics(1, _ => ["staging.db"],
            _ => removed ? throw new DirectoryNotFoundException() : 4096);
        metrics.Sample("synthetic");
        removed = true;
        metrics.Sample("synthetic");
        Assert.Equal(4096, metrics.PeakTemporaryBytes);
    }

    [Fact]
    public void InaccessibleFilesAreNotSilentlyReportedAsZeroUsage()
    {
        var metrics = new RuntimeMetrics(1, _ => ["staging.db"],
            _ => throw new UnauthorizedAccessException());
        Assert.Throws<UnauthorizedAccessException>(() => metrics.Sample("synthetic"));
    }
}

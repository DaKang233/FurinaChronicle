// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Tests.Presentation;

// D5 regression coverage: the isolated startup guard must classify an existing file used as the
// data root before any database object is constructed.
public sealed class IsolatedStartupGuardTests
{
    private const string Variable = "FURINA_UI1_DIRECTORY";

    [Fact]
    public void WritableDirectoryPassesAndLeavesNoProbeFile()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            StartupProbeResult result = IsolatedStartupGuard.CheckAndPrepare(directory, Variable);

            Assert.True(result.Succeeded);
            Assert.True(Directory.Exists(directory));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ExistingFilePathIsRejectedWithVariableHint()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "unwritable-target.txt");
        File.WriteAllText(file, "not a directory");
        try
        {
            StartupProbeResult result = IsolatedStartupGuard.CheckAndPrepare(file, Variable);

            Assert.False(result.Succeeded);
            Assert.Equal(StartupFailureKind.DirectoryNotWritable, result.FailureKind);
            Assert.Contains("隔离数据目录不可写", result.Status);
            Assert.Contains(Variable, result.Detail);
            Assert.Contains("指向文件", result.Detail);
            Assert.True(File.Exists(file));
            Assert.Empty(Directory.EnumerateDirectories(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task GuardFailureFlowsThroughStartupStateAndRemainsRetryable()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "unwritable-target.txt");
        File.WriteAllText(file, "not a directory");
        try
        {
            StartupProbeResult guardFailure = IsolatedStartupGuard.CheckAndPrepare(file, Variable);
            var state = new StartupState(new StaticProbe(guardFailure));

            await state.InitializeAsync();

            Assert.True(state.Snapshot.HasFailed);
            Assert.Equal(StartupFailureKind.DirectoryNotWritable, state.Snapshot.FailureKind);
            Assert.True(state.Snapshot.CanRetry);
            Assert.Contains(Variable, state.Snapshot.Detail);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StaticProbe(StartupProbeResult result) : IStartupProbe
    {
        public Task<StartupProbeResult> RunAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }
}

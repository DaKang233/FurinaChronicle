// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Tests.Presentation;

public sealed class SessionLockTests
{
    [Fact]
    public async Task LiveHolderIsRejectedAndNormalReleaseRemovesLock()
    {
        if (!OperatingSystem.IsWindows())
        {
            // FileShare-based single-writer rejection is enforced by Windows, and the WinUI host
            // itself is Windows-only. Stale takeover and idempotency remain covered on every OS.
            return;
        }

        string directory = TempDirectory();
        string path = Path.Combine(directory, "session.lock");
        var process = new FakeProcess(100, DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            OtherPid = 200,
            OtherAlive = true,
            OtherStart = DateTimeOffset.UtcNow
        };
        try
        {
            var first = new SessionLockManager(process);
            SessionLockResult acquired = await first.AcquireAsync(path, "1.0", "WinUI");
            Assert.Equal(SessionLockState.Acquired, acquired.State);
            Assert.True(File.Exists(path));

            var second = new SessionLockManager(process);
            SessionLockResult rejected = await second.AcquireAsync(path, "1.0", "WinUI");
            Assert.False(rejected.Acquired);
            Assert.Equal(SessionLockState.HeldByOther, rejected.State);
            Assert.Contains("占用", rejected.Message);

            await second.DisposeAsync();
            await first.DisposeAsync();
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task StaleLockIsTakenOverWithWarning()
    {
        string directory = TempDirectory();
        string path = Path.Combine(directory, "session.lock");
        var process = new FakeProcess(100, DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            OtherPid = 200,
            OtherAlive = false
        };
        try
        {
            await File.WriteAllTextAsync(
                path,
                """{"processId":200,"startedUtc":"2020-01-01T00:00:00+00:00","machineName":"OTHER","version":"0.9","hostKind":"Maui"}""");
            var manager = new SessionLockManager(process);

            SessionLockResult result = await manager.AcquireAsync(path, "1.0", "WinUI");

            Assert.Equal(SessionLockState.RecoveredStale, result.State);
            Assert.Contains("陈旧", result.Message);
            Assert.Equal(100, manager.Held!.ProcessId);
            await manager.DisposeAsync();
            Assert.False(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AcquireIsIdempotentForTheSameManager()
    {
        string directory = TempDirectory();
        string path = Path.Combine(directory, "session.lock");
        var manager = new SessionLockManager(
            new FakeProcess(100, DateTimeOffset.UtcNow.AddMinutes(-1)));
        try
        {
            await manager.AcquireAsync(path, "1.0", "WinUI");
            SessionLockResult again = await manager.AcquireAsync(path, "1.0", "WinUI");
            Assert.Equal(SessionLockState.AlreadyHeld, again.State);
            await manager.DisposeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleSessionLock",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeProcess(int currentPid, DateTimeOffset currentStart)
        : IProcessIdentityPort
    {
        public int CurrentProcessId => currentPid;
        public DateTimeOffset CurrentProcessStartedUtc => currentStart;
        public string MachineName => "TEST";
        public int OtherPid { get; init; }
        public bool OtherAlive { get; init; }
        public DateTimeOffset? OtherStart { get; init; }

        public bool IsProcessAlive(int processId) =>
            processId == currentPid || (processId == OtherPid && OtherAlive);

        public DateTimeOffset? GetProcessStartedUtc(int processId) =>
            processId == currentPid
                ? currentStart
                : processId == OtherPid ? OtherStart : null;
    }
}

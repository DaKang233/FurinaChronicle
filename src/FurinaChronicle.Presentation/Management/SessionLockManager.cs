// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace FurinaChronicle.Presentation.Management;

public sealed record SessionLockInfo(
    int ProcessId,
    DateTimeOffset StartedUtc,
    string MachineName,
    string Version,
    string HostKind);

public enum SessionLockState
{
    Acquired = 1,
    RecoveredStale = 2,
    HeldByOther = 3,
    Failed = 4,
    AlreadyHeld = 5
}

public sealed record SessionLockResult(
    SessionLockState State,
    SessionLockInfo? Existing,
    string Message)
{
    public bool Acquired => State is SessionLockState.Acquired or
        SessionLockState.RecoveredStale or SessionLockState.AlreadyHeld;
}

public interface IProcessIdentityPort
{
    int CurrentProcessId { get; }

    DateTimeOffset CurrentProcessStartedUtc { get; }

    string MachineName { get; }

    bool IsProcessAlive(int processId);

    DateTimeOffset? GetProcessStartedUtc(int processId);
}

// Exclusive single-writer lock for a host data directory. The file itself is the lock; metadata
// identifies the owner so a dead/stale owner can be taken over safely and a live owner is refused.
public sealed class SessionLockManager(IProcessIdentityPort process) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private FileStream? stream;
    private SessionLockInfo? held;

    public SessionLockInfo? Held => held;

    public async Task<SessionLockResult> AcquireAsync(
        string lockPath,
        string version,
        string hostKind,
        CancellationToken cancellationToken = default)
    {
        if (stream is not null)
        {
            return new(SessionLockState.AlreadyHeld, held, "当前进程已持有数据目录锁。");
        }

        string fullPath = Path.GetFullPath(lockPath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        SessionLockInfo intended = new(
            process.CurrentProcessId,
            process.CurrentProcessStartedUtc,
            process.MachineName,
            version,
            hostKind);

        try
        {
            stream = new FileStream(
                fullPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
        }
        catch (IOException)
        {
            SessionLockInfo? existing = await TryReadAsync(fullPath, cancellationToken);
            if (existing is not null && process.IsProcessAlive(existing.ProcessId))
            {
                DateTimeOffset? started = process.GetProcessStartedUtc(existing.ProcessId);
                bool sameProcess = started is null ||
                    Math.Abs((started.Value - existing.StartedUtc).TotalSeconds) < 2;
                if (sameProcess)
                {
                    return new(
                        SessionLockState.HeldByOther,
                        existing,
                        $"数据目录已被进程 {existing.ProcessId}（{existing.HostKind}）占用；" +
                        "请先关闭该实例。");
                }
            }

            return new(
                SessionLockState.HeldByOther,
                existing,
                "数据目录锁无法获取（可能被其他进程占用）；未写入任何数据。");
        }
        catch (Exception)
        {
            return new(
                SessionLockState.Failed,
                null,
                "数据目录锁无法创建；请检查目录访问权限。");
        }

        SessionLockInfo? previous = await TryReadAsync(fullPath, cancellationToken);
        try
        {
            stream.SetLength(0);
            await JsonSerializer.SerializeAsync(stream, intended, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Position = 0;
            held = intended;
            bool stale = previous is not null;
            return new(
                stale ? SessionLockState.RecoveredStale : SessionLockState.Acquired,
                previous,
                stale
                    ? $"已安全接管陈旧 session.lock（原 PID {previous!.ProcessId}，主机 {previous.MachineName}）；" +
                        "已写入当前进程锁。"
                    : "已获取数据目录独占锁。");
        }
        catch (Exception)
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception)
            {
            }

            stream = null;
            return new(
                SessionLockState.Failed,
                previous,
                "数据目录锁写入失败；请检查目录访问权限。");
        }
    }

    private static async Task<SessionLockInfo?> TryReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var input = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            return await JsonSerializer.DeserializeAsync<SessionLockInfo>(
                input,
                JsonOptions,
                cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        FileStream? current = stream;
        stream = null;
        held = null;
        if (current is null) return;
        string path = current.Name;
        try
        {
            await current.DisposeAsync();
        }
        catch (Exception)
        {
        }

        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception)
        {
        }
    }
}

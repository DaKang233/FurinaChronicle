// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Presentation.Management;

public enum StartupFailureKind
{
    None = 0,
    DirectoryNotWritable = 1,
    DatabaseOpenFailed = 2,
    SchemaIncompatible = 3,
    Unknown = 4,
    SessionBusy = 5,
    ProductionAdoptionRequired = 6
}

public sealed record StartupProbeResult(
    bool Succeeded,
    StartupFailureKind FailureKind,
    string Status,
    string? Detail)
{
    public string? Warning { get; init; }
}

// Platform hosts implement this probe. The isolated WinUI host checks the writable preview
// directory and opens/validates the isolated SQLite database; it never discovers production
// paths or credentials.
public interface IStartupProbe
{
    Task<StartupProbeResult> RunAsync(CancellationToken cancellationToken = default);
}

public sealed record StartupSnapshot(
    bool IsReady,
    bool IsBusy,
    StartupFailureKind FailureKind,
    string Status,
    string? Detail,
    bool CanRetry)
{
    public string? Warning { get; init; }

    public bool HasFailed => !IsReady && !IsBusy && FailureKind != StartupFailureKind.None;

    public string FailureKindLabel => FailureKind switch
    {
        StartupFailureKind.DirectoryNotWritable => "数据目录不可写",
        StartupFailureKind.DatabaseOpenFailed => "隔离数据库无法打开",
        StartupFailureKind.SchemaIncompatible => "数据库版本不兼容",
        StartupFailureKind.Unknown => "未知启动错误",
        StartupFailureKind.SessionBusy => "数据目录被占用",
        StartupFailureKind.ProductionAdoptionRequired => "需要接管现有数据",
        _ => "已就绪"
    };
}

// Startup is a state machine: initialize, classify failures, allow explicit retry. Metadata or
// network failures are intentionally absent from this gate so local pages stay usable offline.
public sealed class StartupState(IStartupProbe probe)
{
    public StartupSnapshot Snapshot { get; private set; } =
        new(false, false, StartupFailureKind.None, "正在准备隔离数据库…", null, false);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Snapshot = Snapshot with
        {
            IsBusy = true,
            IsReady = false,
            FailureKind = StartupFailureKind.None,
            Status = "正在检查数据目录并打开隔离数据库…",
            Detail = null,
            CanRetry = false
        };
        try
        {
            StartupProbeResult result = await probe.RunAsync(cancellationToken);
            Snapshot = new StartupSnapshot(
                result.Succeeded,
                false,
                result.FailureKind,
                result.Status,
                result.Detail,
                !result.Succeeded)
            {
                Warning = result.Warning
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            Snapshot = new StartupSnapshot(
                false,
                false,
                StartupFailureKind.Unknown,
                "启动未完成；本地数据未被修改。",
                "初始化过程出现未预期错误，请重试。",
                true);
        }
    }
}

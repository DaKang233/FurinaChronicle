// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Windows.Shell;

namespace FurinaChronicle.Windows.Composition;

// Startup gate for both host modes:
//  - explicit isolated root: writable guard + session lock + synthetic environment;
//  - production: read-only metadata/adoption gate first; only after a verified adoption marker
//    does it take the same database root's lock and open the same furinachronicle.db3 in place.
// The production path is never opened before the user confirms adoption.
public sealed class WindowsStartupProbe(
    WindowsPaths paths,
    EnvironmentHolder holder,
    SessionLockManager sessionLock,
    HostAdoptionState adoption,
    WindowsVersionInfo version) : IStartupProbe
{
    public async Task<StartupProbeResult> RunAsync(
        CancellationToken cancellationToken = default)
    {
        if (paths.ConfigurationError is { } configurationError)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "数据目录配置无效。",
                configurationError);
        }

        string? adoptionWarning;
        if (paths.IsIsolated)
        {
            StartupProbeResult pathCheck = IsolatedStartupGuard.CheckAndPrepare(
                paths.DataRoot,
                WindowsPaths.PreviewDirectoryVariable);
            if (!pathCheck.Succeeded) return pathCheck;
            adoptionWarning = null;
        }
        else
        {
            await adoption.EnterAsync(cancellationToken);
            if (adoption.Snapshot.LegacyHostRunning)
            {
                return new(
                    false,
                    StartupFailureKind.SessionBusy,
                    "旧 MAUI 宿主可能正在运行。",
                    "请先完全关闭旧 MAUI 宿主；WinUI 与 MAUI 不可同时打开同一数据库。未写入任何数据。");
            }

            if (adoption.Snapshot.RequiresAction)
            {
                return new(
                    false,
                    StartupFailureKind.ProductionAdoptionRequired,
                    adoption.Snapshot.Status,
                    "需要用户确认后才能接管或初始化生产数据库；在此之前不会打开/修改该库。")
                {
                    Warning = adoption.Snapshot.Error
                };
            }

            adoptionWarning = null;
        }

        SessionLockResult lockResult = await sessionLock.AcquireAsync(
            paths.SessionLockPath,
            version.Version,
            "WinUI",
            cancellationToken);
        if (!lockResult.Acquired)
        {
            return new(
                false,
                StartupFailureKind.SessionBusy,
                "数据目录被其他实例占用。",
                lockResult.Message);
        }

        string? lockWarning = lockResult.State == SessionLockState.RecoveredStale
            ? lockResult.Message
            : null;

        WindowsHostEnvironment environment;
        try
        {
            environment = await holder.GetOrCreateAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "数据目录不可写。",
                "无法在该目录创建数据库结构；请检查数据目录访问权限后重试。");
        }
        catch (IOException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "数据库无法创建。",
                "目录可能被其他进程占用或路径不可用。");
        }
        catch (Exception)
        {
            return new(
                false,
                StartupFailureKind.Unknown,
                "数据库环境初始化失败。",
                "创建数据环境时出现未预期错误；请重试。");
        }

        try
        {
            await environment.InitializeAsync(cancellationToken);
            return new(
                true,
                StartupFailureKind.None,
                paths.IsIsolated ? "隔离数据库已就绪。" : "生产数据库已就绪。",
                $"数据库：{environment.DatabasePath}")
            {
                Warning = lockWarning ?? adoptionWarning
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FurinaDatabaseCompatibilityException exception)
        {
            return new(
                false,
                StartupFailureKind.SchemaIncompatible,
                "数据库版本不兼容。",
                $"实际 application_id 0x{exception.ActualApplicationId:X8} / Schema " +
                $"{exception.ActualSchemaVersion}；当前构建支持 0x{exception.SupportedApplicationId:X8} / " +
                $"Schema {exception.SupportedSchemaVersion}。原数据库未被修改。");
        }
        catch (InvalidDataException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "数据库结构不完整。",
                "数据库结构不完整或无法由当前版本打开；原数据库未被修改。");
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "数据库无法打开。",
                "请检查数据库文件与所在目录的访问权限后重试。");
        }
        catch (IOException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "数据库无法打开。",
                "数据库文件可能被其他进程占用或已损坏；未修改任何数据。");
        }
        catch (Exception)
        {
            return new(
                false,
                StartupFailureKind.Unknown,
                "启动初始化失败。",
                "初始化过程出现未预期错误；本地数据未被修改，请重试。");
        }
    }
}

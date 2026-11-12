// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Windows.Shell;

namespace FurinaChronicle.Windows.Composition;

// Isolated startup probe: writable preview root -> single-writer session lock -> create the
// environment lazily -> open and validate the isolated SQLite database. It never discovers
// production paths or credentials.
public sealed class WindowsStartupProbe(
    WindowsPaths paths,
    EnvironmentHolder holder,
    SessionLockManager sessionLock,
    WindowsVersionInfo version) : IStartupProbe
{
    public async Task<StartupProbeResult> RunAsync(CancellationToken cancellationToken = default)
    {
        if (paths.ConfigurationError is { } configurationError)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录配置无效。",
                configurationError);
        }

        StartupProbeResult pathCheck = IsolatedStartupGuard.CheckAndPrepare(
            paths.BaseDirectory,
            WindowsPaths.PreviewDirectoryVariable);
        if (!pathCheck.Succeeded)
        {
            return pathCheck;
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

        Ui1PreviewEnvironment environment;
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
                "隔离数据目录不可写。",
                $"无法在该目录创建隔离数据库结构；请检查 {WindowsPaths.PreviewDirectoryVariable} " +
                "的访问权限后重试。");
        }
        catch (IOException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "隔离数据库无法创建。",
                "目录可能被其他进程占用或路径不可用；未发现生产库路径。");
        }
        catch (Exception)
        {
            return new(
                false,
                StartupFailureKind.Unknown,
                "隔离数据库初始化失败。",
                "创建隔离环境时出现未预期错误；请重试。");
        }

        try
        {
            await environment.InitializeAsync(cancellationToken);
            return new(
                true,
                StartupFailureKind.None,
                "隔离数据库已就绪。",
                $"数据库：{environment.DatabasePath}")
            {
                Warning = lockWarning
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
                "隔离数据库版本不兼容。",
                $"实际 application_id 0x{exception.ActualApplicationId:X8} / Schema " +
                $"{exception.ActualSchemaVersion}；当前构建支持 0x{exception.SupportedApplicationId:X8} / " +
                $"Schema {exception.SupportedSchemaVersion}。原数据库未被修改。");
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "隔离数据库无法打开。",
                "请检查数据库文件与所在目录的访问权限后重试。");
        }
        catch (IOException)
        {
            return new(
                false,
                StartupFailureKind.DatabaseOpenFailed,
                "隔离数据库无法打开。",
                "数据库文件可能被其他进程占用或已损坏；未发现生产库路径。");
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

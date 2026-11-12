// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Windows.Composition;

// Isolated startup probe: writable preview root -> open/validate isolated SQLite. It never
// discovers production paths or credentials, and metadata/network availability is not part of
// this gate so local pages stay usable offline.
public sealed class WindowsStartupProbe(
    Ui1PreviewEnvironment environment,
    WindowsPaths paths) : IStartupProbe
{
    public async Task<StartupProbeResult> RunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(paths.BaseDirectory);
            string probePath = Path.Combine(
                paths.BaseDirectory,
                $".ui4-write-probe-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probePath, "ok", cancellationToken);
            File.Delete(probePath);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可写。",
                $"无法写入 {paths.BaseDirectory}；请检查 FURINA_UI1_DIRECTORY 的访问权限。");
        }
        catch (IOException exception) when (exception is not DirectoryNotFoundException)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可写或磁盘不可用。",
                $"无法写入 {paths.BaseDirectory}；请检查目录与磁盘状态。");
        }

        try
        {
            await environment.InitializeAsync(cancellationToken);
            return new(
                true,
                StartupFailureKind.None,
                "隔离数据库已就绪。",
                $"数据库：{environment.DatabasePath}");
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

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Presentation.Management;

// Pure path pre-flight for the isolated preview root. It must run before the database object is
// constructed: FurinaDatabase creates its parent directory in its constructor, and an unprevented
// IOException there (for example when FURINA_UI1_DIRECTORY points at an existing file) escapes
// the WinUI launch path and terminates the process before the startup page can render.
public static class IsolatedStartupGuard
{
    public static StartupProbeResult CheckAndPrepare(
        string rootDirectory,
        string directoryVariableName)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可用。",
                $"{directoryVariableName} 为空；请指定一个可写目录。");
        }

        if (File.Exists(rootDirectory))
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可写。",
                $"{directoryVariableName} 指向文件“{rootDirectory}”，而不是目录；" +
                "请改为一个可写目录。");
        }

        try
        {
            Directory.CreateDirectory(rootDirectory);
            string probePath = Path.Combine(
                rootDirectory,
                $".ui4-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return new(true, StartupFailureKind.None, "隔离数据目录可写。", null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可写。",
                $"无法写入“{rootDirectory}”；请检查 {directoryVariableName} 的访问权限。");
        }
        catch (IOException)
        {
            return new(
                false,
                StartupFailureKind.DirectoryNotWritable,
                "隔离数据目录不可写或磁盘不可用。",
                $"无法写入“{rootDirectory}”；请检查 {directoryVariableName} 与磁盘状态。");
        }
        catch (Exception)
        {
            return new(
                false,
                StartupFailureKind.Unknown,
                "隔离数据目录检查失败。",
                $"无法验证“{rootDirectory}”；未创建数据库，请重试。");
        }
    }
}

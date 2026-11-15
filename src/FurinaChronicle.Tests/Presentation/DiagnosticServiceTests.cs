// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.Tests.Presentation;

public sealed class DiagnosticServiceTests
{
    [Fact]
    public void ReportWritesLogAndNotificationWithTheSameDiagnosticId()
    {
        string directory = TempDirectory();
        try
        {
            var center = CreateCenter();
            var service = new DiagnosticService(
                new FileDiagnosticLog(directory),
                center);

            DiagnosticReport report = service.Report(new DiagnosticEvent(
                NotificationCategory.Gacha,
                "refresh",
                "刷新失败",
                "网络不可用",
                DiagnosticSeverity.Error));

            AppNotification notification = Assert.Single(center.Items);
            Assert.Contains(report.DiagnosticId, notification.Message);
            Assert.Equal(report.DiagnosticId, notification.DiagnosticId);
            Assert.Equal(NotificationSeverity.Error, notification.Severity);
            string active = Path.Combine(directory, "diagnostics.jsonl");
            Assert.True(File.Exists(active));
            string log = File.ReadAllText(active);
            Assert.Contains(report.DiagnosticId, log);
            Assert.Contains("refresh", log);
            Assert.False(report.LogWriteFailed);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ReportSanitizesPathsUrlsAndSecrets()
    {
        string directory = TempDirectory();
        try
        {
            var center = CreateCenter();
            var service = new DiagnosticService(new FileDiagnosticLog(directory), center);

            service.Report(new DiagnosticEvent(
                NotificationCategory.Passport,
                "login",
                "登录失败",
                @"C:\Users\someone\secret.txt https://example.com/path?token=abc123 stoken=xyz",
                DiagnosticSeverity.Error));

            string log = File.ReadAllText(Path.Combine(directory, "diagnostics.jsonl"));
            Assert.DoesNotContain(@"C:\Users\someone", log);
            Assert.DoesNotContain("example.com", log);
            Assert.DoesNotContain("abc123", log);
            Assert.DoesNotContain("xyz", log);
            Assert.Contains("<path>", log);
            Assert.Contains("<url>", log);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LogFailureKeepsTheNotificationAndDoesNotThrow()
    {
        var center = CreateCenter();
        var service = new DiagnosticService(new ThrowingLog(), center);

        DiagnosticReport report = service.Report(new DiagnosticEvent(
            NotificationCategory.Settings,
            "save",
            "保存失败",
            "磁盘不可写",
            DiagnosticSeverity.Warning));

        Assert.True(report.LogWriteFailed);
        AppNotification notification = Assert.Single(center.Items);
        Assert.Contains("本地日志写入失败", notification.Message);
        Assert.Contains(report.DiagnosticId, notification.Message);
        Assert.Equal(NotificationSeverity.Warning, notification.Severity);
    }

    [Fact]
    public void RecentKeepsNewestFirstAndIsBounded()
    {
        var center = CreateCenter();
        var service = new DiagnosticService(new NullLog(), center);
        for (int index = 0; index < 105; index++)
        {
            service.Report(new DiagnosticEvent(
                NotificationCategory.System,
                "op" + index,
                "标题",
                "消息",
                DiagnosticSeverity.Warning));
        }

        Assert.Equal(100, service.Recent.Count);
        Assert.Equal("op104", service.Recent[0].Operation);
    }

    [Fact]
    public void FileLogRotatesAndKeepsABoundedNumberOfFiles()
    {
        string directory = TempDirectory();
        try
        {
            var log = new FileDiagnosticLog(directory, maxBytes: 220, maxFiles: 2);
            for (int index = 0; index < 8; index++)
            {
                log.Write(new DiagnosticReport(
                    "FCD-" + index,
                    DateTimeOffset.UtcNow,
                    DiagnosticSeverity.Warning,
                    NotificationCategory.System,
                    "op",
                    "title",
                    new string('x', 80),
                    null,
                    null,
                    LogWriteFailed: false));
            }

            string[] files = Directory.GetFiles(directory, "diagnostics*.jsonl");
            Assert.True(files.Length <= 3, $"unexpected log file count: {files.Length}");
            Assert.True(File.Exists(log.ActiveFilePath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FormatterIncludesDiagnosticsAndLogFiles()
    {
        string directory = TempDirectory();
        try
        {
            var center = CreateCenter();
            var service = new DiagnosticService(new FileDiagnosticLog(directory), center);
            DiagnosticReport report = service.Report(new DiagnosticEvent(
                NotificationCategory.Gacha,
                "export",
                "导出失败",
                "无法写入文件",
                DiagnosticSeverity.Error));

            string text = DiagnosticReportFormatter.Build(service, directory);

            Assert.Contains("FurinaChronicle Windows diagnostic report", text);
            Assert.Contains(report.DiagnosticId, text);
            Assert.Contains("diagnostics.jsonl", text);
            Assert.Contains("导出失败", text);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static NotificationCenter CreateCenter() =>
        new(new InlineDispatcher());

    private static string TempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "furina-diagnostics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class NullLog : IDiagnosticLog
    {
        public void Write(DiagnosticReport report)
        {
        }
    }

    private sealed class ThrowingLog : IDiagnosticLog
    {
        public void Write(DiagnosticReport report) =>
            throw new IOException("disk unavailable");
    }
}

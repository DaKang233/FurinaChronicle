// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.Presentation.Diagnostics;

/// <summary>
/// Single diagnostic entry point for the WinUI host. A warning or handleable error is
/// written to the local log and published once to the notification center; both carry
/// the same diagnostic id. A failing log write keeps the notification available and
/// never re-enters the diagnostic pipeline.
/// </summary>
public sealed class DiagnosticService : IDiagnosticService
{
    private const int MaxRecent = 100;
    private readonly IDiagnosticLog log;
    private readonly INotificationCenter notifications;
    private readonly TimeProvider timeProvider;
    private readonly object gate = new();
    private readonly List<DiagnosticReport> recent = [];

    public DiagnosticService(
        IDiagnosticLog log,
        INotificationCenter notifications,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(notifications);
        this.log = log;
        this.notifications = notifications;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    public IReadOnlyList<DiagnosticReport> Recent
    {
        get
        {
            lock (gate)
            {
                return [.. recent];
            }
        }
    }

    public DiagnosticReport Report(DiagnosticEvent diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        DateTimeOffset now = timeProvider.GetUtcNow();
        string id = string.Create(
            CultureInfo.InvariantCulture,
            $"FCD-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..4].ToUpperInvariant()}");
        string message = LogSanitizer.Sanitize(diagnostic.Message);
        string? exceptionType = diagnostic.Exception?.GetType().FullName;
        string? stack = diagnostic.Exception is null
            ? null
            : LogSanitizer.SanitizeException(diagnostic.Exception, diagnostic.Operation);
        bool logFailed = false;
        DiagnosticReport report = new(
            id,
            now,
            diagnostic.Severity,
            diagnostic.Category,
            diagnostic.Operation,
            diagnostic.Title,
            message,
            exceptionType,
            stack,
            LogWriteFailed: false);
        try
        {
            log.Write(report);
        }
        catch (Exception)
        {
            logFailed = true;
            report = report with { LogWriteFailed = true };
        }

        lock (gate)
        {
            recent.Insert(0, report);
            if (recent.Count > MaxRecent)
            {
                recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
            }
        }

        NotificationSeverity severity = diagnostic.Severity == DiagnosticSeverity.Warning
            ? NotificationSeverity.Warning
            : NotificationSeverity.Error;
        string notificationMessage = logFailed
            ? $"{message}{Environment.NewLine}本地日志写入失败；诊断编号 {id}"
            : $"{message}{Environment.NewLine}诊断编号 {id}";
        _ = notifications.Publish(new NotificationRequest(
            severity,
            diagnostic.Category,
            diagnostic.Title,
            notificationMessage)
        {
            TaskKey = diagnostic.TaskKey,
            IsInProgress = false,
            DiagnosticId = id,
        });
        return report;
    }
}

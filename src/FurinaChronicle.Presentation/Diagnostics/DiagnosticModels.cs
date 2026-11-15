// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.Presentation.Diagnostics;

public enum DiagnosticSeverity
{
    Warning = 0,
    Error = 1,
    Fatal = 2,
}

/// <summary>
/// A warning, handleable error or fatal condition raised by the host. The diagnostic
/// service owns the shared diagnostic id, the local log write and the notification.
/// </summary>
public sealed record DiagnosticEvent(
    NotificationCategory Category,
    string Operation,
    string Title,
    string Message,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error,
    Exception? Exception = null,
    string? TaskKey = null);

public sealed record DiagnosticReport(
    string DiagnosticId,
    DateTimeOffset OccurredAt,
    DiagnosticSeverity Severity,
    NotificationCategory Category,
    string Operation,
    string Title,
    string Message,
    string? ExceptionType,
    string? StackTrace,
    bool LogWriteFailed);

public interface IDiagnosticLog
{
    void Write(DiagnosticReport report);
}

public interface IDiagnosticService
{
    IReadOnlyList<DiagnosticReport> Recent { get; }

    DiagnosticReport Report(DiagnosticEvent diagnostic);
}

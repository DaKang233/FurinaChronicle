// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;

namespace FurinaChronicle.Presentation.Notifications;

public enum NotificationSeverity
{
    Information = 0,
    Success = 1,
    Warning = 2,
    Error = 3,
}

public enum NotificationCategory
{
    System = 0,
    Archive = 1,
    Collection = 2,
    Passport = 3,
    Gacha = 4,
    Chronicle = 5,
    Settings = 6,
}

/// <summary>
/// A request from a page or service. The center owns identity, timestamps,
/// deduplication and retention; callers only describe what happened.
/// </summary>
public sealed record NotificationRequest(
    NotificationSeverity Severity,
    NotificationCategory Category,
    string Title,
    string Message)
{
    public string? ActionLabel { get; init; }

    public Action? Action { get; init; }

    public string? Fingerprint { get; init; }
}

public sealed record AppNotification(
    Guid Id,
    NotificationSeverity Severity,
    NotificationCategory Category,
    string Title,
    string Message,
    DateTimeOffset OccurredAt,
    bool IsRead,
    int RepeatCount,
    string? ActionLabel,
    Action? Action,
    string Fingerprint);

public sealed record NotificationCenterOptions
{
    public int MaxItems { get; init; } = 200;

    public TimeSpan DedupeWindow { get; init; } = TimeSpan.FromSeconds(5);
}

public interface INotificationCenter
{
    ReadOnlyObservableCollection<AppNotification> Items { get; }

    int UnreadCount { get; }

    event EventHandler? Changed;

    AppNotification Publish(NotificationRequest request);

    AppNotification? Latest();

    bool MarkRead(Guid id);

    void MarkAllRead();

    bool Remove(Guid id);

    void Clear();
}

public sealed record NotificationSnapshot(
    IReadOnlyList<AppNotification> Items,
    int UnreadCount);

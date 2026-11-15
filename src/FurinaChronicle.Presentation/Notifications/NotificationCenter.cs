// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using FurinaChronicle.Presentation.Gacha;

namespace FurinaChronicle.Presentation.Notifications;

/// <summary>
/// Single notification pipeline for the WinUI host. Publishing is
/// thread-safe; the observable collection and change event are only touched
/// on the host UI thread through <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class NotificationCenter : INotificationCenter
{
    private readonly object gate = new();
    private readonly List<AppNotification> history = [];
    private readonly Dictionary<string, Guid> tasks = new(StringComparer.Ordinal);
    private readonly ObservableCollection<AppNotification> items = [];
    private readonly ReadOnlyObservableCollection<AppNotification> readOnlyItems;
    private readonly IUiDispatcher dispatcher;
    private readonly TimeProvider timeProvider;
    private readonly NotificationCenterOptions options;

    public NotificationCenter(
        IUiDispatcher dispatcher,
        TimeProvider? timeProvider = null,
        NotificationCenterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        this.dispatcher = dispatcher;
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.options = options ?? new NotificationCenterOptions();
        if (this.options.MaxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "MaxItems must be positive.");
        }
        readOnlyItems = new ReadOnlyObservableCollection<AppNotification>(items);
    }

    public ReadOnlyObservableCollection<AppNotification> Items => readOnlyItems;

    public int UnreadCount
    {
        get
        {
            lock (gate)
            {
                return history.Count(item => !item.IsRead);
            }
        }
    }

    public event EventHandler? Changed;

    public AppNotification Publish(NotificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Title) &&
            string.IsNullOrWhiteSpace(request.Message))
        {
            throw new ArgumentException(
                "A notification needs a title or a message.", nameof(request));
        }

        string? taskKey = string.IsNullOrWhiteSpace(request.TaskKey)
            ? null
            : request.TaskKey.Trim();
        AppNotification notification;
        lock (gate)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (taskKey is not null &&
                tasks.TryGetValue(taskKey, out Guid taskId) &&
                history.FindIndex(item => item.Id == taskId) is int index and >= 0)
            {
                AppNotification existing = history[index];
                AppNotification updated = existing with
                {
                    Severity = request.Severity,
                    Category = request.Category,
                    Title = request.Title.Trim(),
                    Message = request.Message.Trim(),
                    OccurredAt = now,
                    // Progress updates should not keep re-raising the unread badge; a
                    // completed/failed result becomes unread once.
                    IsRead = request.IsInProgress && existing.IsRead,
                    ActionLabel = request.ActionLabel,
                    Action = request.Action,
                    IsInProgress = request.IsInProgress,
                    DiagnosticId = request.DiagnosticId,
                };
                history[index] = updated;
                notification = updated;
            }
            else
            {
                string fingerprint = request.Fingerprint ??
                    $"{request.Category}:{request.Title}:{request.Message}";
                AppNotification? latest = history.FirstOrDefault();
                if (latest is not null &&
                    !latest.IsRead &&
                    latest.Fingerprint == fingerprint &&
                    latest.TaskKey is null &&
                    now - latest.OccurredAt <= options.DedupeWindow)
                {
                    AppNotification merged = latest with
                    {
                        OccurredAt = now,
                        RepeatCount = latest.RepeatCount + 1,
                    };
                    history[0] = merged;
                    notification = merged;
                }
                else
                {
                    notification = new AppNotification(
                        Guid.NewGuid(),
                        request.Severity,
                        request.Category,
                        request.Title.Trim(),
                        request.Message.Trim(),
                        now,
                        // Running progress does not raise the unread badge; the final result does.
                        IsRead: request.IsInProgress,
                        RepeatCount: 1,
                        request.ActionLabel,
                        request.Action,
                        fingerprint,
                        taskKey,
                        request.IsInProgress,
                        request.DiagnosticId);
                    history.Insert(0, notification);
                    if (taskKey is not null)
                    {
                        tasks[taskKey] = notification.Id;
                    }
                    TrimExcessLocked();
                }
            }
        }

        PublishSnapshot();
        return notification;
    }

    public bool MarkRead(Guid id)
    {
        bool changed;
        lock (gate)
        {
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0 || history[index].IsRead)
            {
                return false;
            }
            history[index] = history[index] with { IsRead = true };
            changed = true;
        }
        if (changed)
        {
            PublishSnapshot();
        }
        return true;
    }

    public void MarkAllRead()
    {
        bool changed;
        lock (gate)
        {
            changed = false;
            for (int index = 0; index < history.Count; index++)
            {
                if (!history[index].IsRead)
                {
                    history[index] = history[index] with { IsRead = true };
                    changed = true;
                }
            }
        }
        if (changed)
        {
            PublishSnapshot();
        }
    }

    public bool Remove(Guid id)
    {
        bool removed;
        lock (gate)
        {
            int index = history.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }
            removed = true;
            RemoveTaskMappingLocked(history[index]);
            history.RemoveAt(index);
        }
        if (removed)
        {
            PublishSnapshot();
        }
        return removed;
    }

    public void Clear()
    {
        lock (gate)
        {
            history.Clear();
            tasks.Clear();
        }
        PublishSnapshot();
    }

    /// <summary>
    /// Lets hosts render a transient banner without duplicating retention
    /// logic. Callers can compare the newest id they have already shown.
    /// </summary>
    public AppNotification? Latest()
    {
        lock (gate)
        {
            return history.FirstOrDefault();
        }
    }

    public AppNotification? FindByTask(string taskKey)
    {
        if (string.IsNullOrWhiteSpace(taskKey))
        {
            return null;
        }
        lock (gate)
        {
            return tasks.TryGetValue(taskKey.Trim(), out Guid id)
                ? history.FirstOrDefault(item => item.Id == id)
                : null;
        }
    }

    private void TrimExcessLocked()
    {
        if (history.Count <= options.MaxItems)
        {
            return;
        }
        for (int index = options.MaxItems; index < history.Count; index++)
        {
            RemoveTaskMappingLocked(history[index]);
        }
        history.RemoveRange(
            options.MaxItems,
            history.Count - options.MaxItems);
    }

    private void RemoveTaskMappingLocked(AppNotification notification)
    {
        if (notification.TaskKey is { Length: > 0 } key &&
            tasks.TryGetValue(key, out Guid id) &&
            id == notification.Id)
        {
            tasks.Remove(key);
        }
    }

    private void PublishSnapshot()
    {
        NotificationSnapshot snapshot;
        lock (gate)
        {
            snapshot = new NotificationSnapshot([.. history], UnreadCount);
        }

        _ = dispatcher.InvokeAsync(() =>
        {
            items.Clear();
            foreach (AppNotification item in snapshot.Items)
            {
                items.Add(item);
            }
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}

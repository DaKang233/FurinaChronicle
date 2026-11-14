// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.Tests.Presentation;

public sealed class NotificationCenterTests
{
    [Fact]
    public void PublishAddsUnreadNotification()
    {
        var center = Create(out _);

        AppNotification notification = center.Publish(new(
            NotificationSeverity.Warning,
            NotificationCategory.Archive,
            "导出完成",
            "已写入 12 项"));

        Assert.Single(center.Items);
        Assert.Equal(notification.Id, center.Items[0].Id);
        Assert.Equal(1, center.UnreadCount);
        Assert.False(center.Items[0].IsRead);
        Assert.Equal(1, center.Items[0].RepeatCount);
        Assert.Equal(NotificationSeverity.Warning, center.Items[0].Severity);
        Assert.Equal(NotificationCategory.Archive, center.Items[0].Category);
    }

    [Fact]
    public void DuplicateWithinWindowMergesIntoOneItem()
    {
        var time = new TestTime();
        var center = Create(out _, time);
        var request = new NotificationRequest(
            NotificationSeverity.Error,
            NotificationCategory.Collection,
            "刷新失败",
            "网络不可用");

        center.Publish(request);
        time.Advance(TimeSpan.FromSeconds(1));
        AppNotification second = center.Publish(request);

        Assert.Single(center.Items);
        Assert.Equal(2, second.RepeatCount);
        Assert.Equal(second.Id, center.Items[0].Id);
        Assert.Equal(time.GetUtcNow(), center.Items[0].OccurredAt);
    }

    [Fact]
    public void DuplicateAfterWindowAppearsAsNewItem()
    {
        var time = new TestTime();
        var center = Create(out _, time);
        var request = new NotificationRequest(
            NotificationSeverity.Information,
            NotificationCategory.System,
            "提示",
            "同一内容");

        center.Publish(request);
        time.Advance(TimeSpan.FromSeconds(30));
        center.Publish(request);

        Assert.Equal(2, center.Items.Count);
        Assert.Equal(2, center.UnreadCount);
    }

    [Fact]
    public void CustomFingerprintMergesDifferentMessages()
    {
        var center = Create(out _);
        var first = new NotificationRequest(
            NotificationSeverity.Warning,
            NotificationCategory.Archive,
            "采集",
            "第 1 条")
        { Fingerprint = "collect-archive" };
        var second = first with { Message = "第 2 条" };

        center.Publish(first);
        center.Publish(second);

        Assert.Single(center.Items);
        Assert.Equal(2, center.Items[0].RepeatCount);
    }

    [Fact]
    public void MarkReadAndMarkAllReadUpdateUnreadCount()
    {
        var center = Create(out _);
        AppNotification first = center.Publish(Request("一"));
        center.Publish(Request("二"));

        Assert.True(center.MarkRead(first.Id));
        Assert.False(center.MarkRead(first.Id));
        Assert.Equal(1, center.UnreadCount);

        center.MarkAllRead();
        Assert.Equal(0, center.UnreadCount);
        Assert.All(center.Items, item => Assert.True(item.IsRead));
    }

    [Fact]
    public void RetentionKeepsNewestItemsOnly()
    {
        var center = Create(
            out _,
            options: new NotificationCenterOptions { MaxItems = 3 });

        for (int index = 0; index < 5; index++)
        {
            center.Publish(Request($"消息 {index}"));
        }

        Assert.Equal(3, center.Items.Count);
        Assert.Equal("消息 4", center.Items[0].Message);
        Assert.Equal("消息 2", center.Items[2].Message);
    }

    [Fact]
    public void RemoveAndClearUpdateItemsAndUnreadCount()
    {
        var center = Create(out _);
        AppNotification first = center.Publish(Request("一"));
        center.Publish(Request("二"));

        Assert.True(center.Remove(first.Id));
        Assert.False(center.Remove(first.Id));
        Assert.Single(center.Items);
        Assert.Equal(1, center.UnreadCount);

        center.Clear();
        Assert.Empty(center.Items);
        Assert.Equal(0, center.UnreadCount);
    }

    [Fact]
    public void ActionIsRetainedForNotificationCenterCommands()
    {
        var center = Create(out _);
        int invoked = 0;
        var request = new NotificationRequest(
            NotificationSeverity.Information,
            NotificationCategory.Settings,
            "可重试",
            "点击重试")
        {
            ActionLabel = "重试",
            Action = () => invoked++,
        };

        AppNotification notification = center.Publish(request);
        notification.Action!.Invoke();

        Assert.Equal("重试", notification.ActionLabel);
        Assert.Equal(1, invoked);
    }

    [Fact]
    public void EmptyNotificationAndInvalidOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            out _,
            options: new NotificationCenterOptions { MaxItems = 0 }));
        var center = Create(out _);
        Assert.Throws<ArgumentException>(() => center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.System,
            " ",
            " ")));
    }

    private static NotificationRequest Request(string message) => new(
        NotificationSeverity.Information,
        NotificationCategory.System,
        "消息",
        message);

    private static NotificationCenter Create(
        out ImmediateDispatcher dispatcher,
        TestTime? time = null,
        NotificationCenterOptions? options = null)
    {
        dispatcher = new ImmediateDispatcher();
        return new NotificationCenter(
            dispatcher,
            time ?? new TestTime(),
            options);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 14, 4, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan duration) => now += duration;
    }
}

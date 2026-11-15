// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;

namespace FurinaChronicle.Tests.Presentation;

public sealed class NotificationTaskTests
{
    [Fact]
    public void ProgressUpdatesReplaceTheSameRow()
    {
        var time = new TestTime();
        var center = Create(out _, time);

        AppNotification running = center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "导入中",
            "正在读取文件…")
        {
            TaskKey = "gacha-import",
            IsInProgress = true,
        });

        time.Advance(TimeSpan.FromSeconds(3));
        AppNotification completed = center.Publish(new(
            NotificationSeverity.Success,
            NotificationCategory.Gacha,
            "导入完成",
            "已导入 7 条")
        {
            TaskKey = "gacha-import",
        });

        Assert.Single(center.Items);
        Assert.Equal(running.Id, completed.Id);
        Assert.Equal(completed.Id, center.Items[0].Id);
        Assert.False(center.Items[0].IsInProgress);
        Assert.Equal("已导入 7 条", center.Items[0].Message);
        Assert.Equal(NotificationSeverity.Success, center.Items[0].Severity);
        Assert.Equal(time.GetUtcNow(), center.Items[0].OccurredAt);
    }

    [Fact]
    public void RunningProgressDoesNotRaiseTheUnreadBadge()
    {
        var center = Create(out _);

        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "刷新中",
            "正在请求…")
        {
            TaskKey = "gacha-refresh",
            IsInProgress = true,
        });

        Assert.Equal(0, center.UnreadCount);
        Assert.True(center.Items[0].IsRead);
    }

    [Fact]
    public void CompletionBecomesUnreadEvenAfterAProgressWasRead()
    {
        var center = Create(out _);
        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "刷新中",
            "正在请求…")
        {
            TaskKey = "gacha-refresh",
            IsInProgress = true,
        });

        center.Publish(new(
            NotificationSeverity.Error,
            NotificationCategory.Gacha,
            "刷新失败",
            "网络不可用")
        {
            TaskKey = "gacha-refresh",
            DiagnosticId = "FCD-1",
        });

        Assert.Equal(1, center.UnreadCount);
        Assert.False(center.Items[0].IsRead);
        Assert.Equal("FCD-1", center.Items[0].DiagnosticId);
        Assert.Single(center.Items);
    }

    [Fact]
    public void RemoveClearsTheTaskKeySoTheNextPublishCreatesANewRow()
    {
        var center = Create(out _);
        AppNotification first = center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "导出中",
            "正在写出文件…")
        {
            TaskKey = "gacha-export",
            IsInProgress = true,
        });

        center.Remove(first.Id);
        AppNotification second = center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "导出中",
            "再次开始")
        {
            TaskKey = "gacha-export",
            IsInProgress = true,
        });

        Assert.NotEqual(first.Id, second.Id);
        Assert.Single(center.Items);
    }

    [Fact]
    public void ClearForgetsTaskKeys()
    {
        var center = Create(out _);
        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "导出中",
            "正在写出文件…")
        {
            TaskKey = "gacha-export",
            IsInProgress = true,
        });

        center.Clear();
        Assert.Null(center.FindByTask("gacha-export"));
        AppNotification again = center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "导出中",
            "重新开始")
        {
            TaskKey = "gacha-export",
            IsInProgress = true,
        });
        Assert.Single(center.Items);
        Assert.Equal(again.Id, center.FindByTask("gacha-export")?.Id);
    }

    [Fact]
    public void EvictionDropsTheTaskMapping()
    {
        var dispatcher = new FakeDispatcher();
        var center = new NotificationCenter(
            dispatcher,
            timeProvider: null,
            new NotificationCenterOptions { MaxItems = 2 });

        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Gacha,
            "任务一",
            "运行") { TaskKey = "old-task", IsInProgress = true });
        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.System,
            "填充",
            "一"));
        center.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.System,
            "填充",
            "二"));

        Assert.Equal(2, center.Items.Count);
        Assert.Null(center.FindByTask("old-task"));
    }

    [Fact]
    public void FindByTaskReturnsTheCurrentRow()
    {
        var center = Create(out _);
        AppNotification published = center.Publish(new(
            NotificationSeverity.Warning,
            NotificationCategory.Settings,
            "保存中",
            "正在写入设置…")
        {
            TaskKey = "settings-save",
            IsInProgress = true,
        });

        Assert.Equal(published.Id, center.FindByTask("settings-save")?.Id);
        Assert.Null(center.FindByTask("missing"));
    }

    private static NotificationCenter Create(out FakeDispatcher dispatcher, TestTime? time = null)
    {
        dispatcher = new FakeDispatcher();
        return new NotificationCenter(dispatcher, time ?? new TestTime());
    }

    private sealed class FakeDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }
    }

    private sealed class TestTime : TimeProvider
    {
        private DateTimeOffset now = new(2026, 11, 15, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan delta) => now += delta;
    }
}

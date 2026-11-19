// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaRefreshDisplayTests
{
    [Fact]
    public void GroupsOnlyKnownItemIdsDeduplicatesRecordsAndKeepsCountsBeyondInt32()
    {
        var state = new GachaRefreshDisplayState("刷新");
        GachaRemoteRecord first = Record("1", "10000089", "合成角色", int.MaxValue);
        state.Apply(new("301", 1, 2, GachaRefreshProgressStage.PageReceived,
            [first, Record("2", "10000089", "不同显示名称", int.MaxValue)]));
        state.Apply(new("301", 1, 2, GachaRefreshProgressStage.PageReceived, [first]));
        Assert.Equal(2L * int.MaxValue, Assert.Single(state.Items).Count);
        Assert.Equal(2L * int.MaxValue, state.ItemCount);
        Assert.Equal("合成角色", state.Items[0].Name);
        Assert.Contains("角色活动祈愿", state.CurrentPool);
        state.Apply(new("302", 1, 4, GachaRefreshProgressStage.PageReceived,
            [Record("3", null, "同名未知物品"), Record("4", null, "同名未知物品")]));
        Assert.Equal(3, state.Items.Count);
        Assert.Equal(4, state.FetchedRecords);
    }

    [Fact]
    public void SavingAndFailureDoNotClaimProvisionalItemsWereInsertedOrExposeSourceStrings()
    {
        var state = new GachaRefreshDisplayState("刷新");
        state.Apply(new("https://example.invalid/?authkey=private", 1, 0, GachaRefreshProgressStage.RequestingPage, []));
        Assert.Equal("正在获取其他卡池", state.CurrentPool);
        state.Apply(new("301", 1, 1, GachaRefreshProgressStage.PageReceived, [Record("1", "10000089", "合成角色")]));
        state.Apply(new("200", 0, 1, GachaRefreshProgressStage.Saving, []));
        Assert.Equal("正在保存", state.CurrentPool);
        Assert.Single(state.Items);
        state.Complete(false, null);
        Assert.False(state.IsRunning);
        Assert.False(state.Succeeded);
        Assert.Contains("未确认", state.Detail);
        state.Apply(new("302", 2, 2, GachaRefreshProgressStage.PageReceived, [Record("2", "11501", "迟到记录")]));
        Assert.Single(state.Items);
        Assert.DoesNotContain("authkey", state.CurrentPool + state.Detail + state.Totals);
    }

    [Fact]
    public void SuccessfulResultDistinguishesFetchedInsertedCorrectedAndSuppressed()
    {
        var state = new GachaRefreshDisplayState("刷新");
        state.Apply(new("301", 1, 10, GachaRefreshProgressStage.PageReceived, []));
        state.Complete(true, new(10, 2, 3, 4, 5, 1, 1));
        Assert.True(state.Succeeded);
        Assert.Contains("新增 2", state.Detail);
        Assert.Contains("校正 3", state.Detail);
        Assert.Contains("永久删除抑制 1", state.Detail);
        state.Complete(false, null);
        Assert.True(state.Succeeded);
    }

    [Fact]
    public async Task ReporterPreservesOrderCopiesFramesAndDrainsBeforeCompletion()
    {
        var dispatcher = new QueuedDispatcher();
        var state = new GachaRefreshDisplayState("刷新");
        using var reporter = new GachaRefreshProgressReporter(dispatcher, state.Apply);
        var records = new List<GachaRemoteRecord> { Record("1", "10000089", "合成角色") };
        reporter.Report(new("301", 1, 1, GachaRefreshProgressStage.PageReceived, records));
        records.Clear();
        reporter.Report(new("200", 0, 1, GachaRefreshProgressStage.Saving, []));
        Task drain = reporter.DrainAsync();
        Assert.False(drain.IsCompleted);
        await dispatcher.NextAsync();
        await dispatcher.NextAsync();
        await drain;
        Assert.Equal("正在保存", state.CurrentPool);
        Assert.Single(state.Items);
        state.Complete(true, new(1, 1, 0, 0, 1, 0));
    }

    [Fact]
    public async Task DisposedOrBrokenDisplayCannotFailTheDomainOperation()
    {
        var dispatcher = new QueuedDispatcher();
        int delivered = 0;
        var reporter = new GachaRefreshProgressReporter(dispatcher, _ => delivered++);
        reporter.Report(new("301", 1, 0, GachaRefreshProgressStage.RequestingPage, []));
        reporter.Dispose();
        await dispatcher.NextAsync();
        await reporter.DrainAsync();
        Assert.Equal(0, delivered);
        using var broken = new GachaRefreshProgressReporter(new BrokenDispatcher(), _ => throw new InvalidOperationException());
        broken.Report(new("301", 1, 0, GachaRefreshProgressStage.RequestingPage, []));
        await broken.DrainAsync();
    }

    private static GachaRemoteRecord Record(string id, string? itemId, string name, int count = 1) =>
        new("100000001", id, name, itemId, "角色", 5, "301", DateTimeOffset.UnixEpoch, count);

    private sealed class BrokenDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) => throw new InvalidOperationException("Display closed.");
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly ConcurrentQueue<(Action Action, TaskCompletionSource Done)> queue = new();
        private readonly SemaphoreSlim signal = new(0);
        public Task InvokeAsync(Action action)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.Enqueue((action, done));
            signal.Release();
            return done.Task;
        }
        public async Task NextAsync()
        {
            Assert.True(await signal.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(queue.TryDequeue(out var work));
            work.Action();
            work.Done.SetResult();
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaReadOnlyStateTests
{
    [Fact]
    public async Task PagesKeepIndependentStableSelectionsAndRequeryOnReturn()
    {
        var source = new Source();
        var first = new GachaReadOnlyState(source, new ImmediateDispatcher());
        var second = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await first.EnterAsync();
        await second.EnterAsync();
        await first.SelectAccountAsync(source.B.Id);
        Assert.Equal(source.A.Id, second.Snapshot.AccountId);
        first.Leave();
        source.Accounts[1] = source.B with { Label = "B renamed" };
        await first.EnterAsync();
        Assert.Equal(source.B.Id, first.Snapshot.AccountId);
        Assert.Contains(first.Snapshot.Accounts, a => a.Label == "B renamed");
        source.Accounts.RemoveAt(1);
        first.Leave();
        await first.EnterAsync();
        Assert.Equal(source.A.Id, first.Snapshot.AccountId);
        Assert.Contains("回退", first.Snapshot.Status);
        Assert.Equal(25, source.LastPageSize);
    }

    [Fact]
    public async Task RemovedArchiveFallsBackWithoutKeepingItsRows()
    {
        var source = new Source();
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await state.EnterAsync();
        state.Leave();
        var replacement = new GachaScopeOption(Guid.NewGuid(), "replacement");
        source.Archives = [replacement];
        source.Accounts.Clear();
        await state.EnterAsync();
        Assert.Equal(replacement.Id, state.Snapshot.ArchiveId);
        Assert.Null(state.Snapshot.AccountId);
        Assert.Empty(state.Snapshot.Rows);
        Assert.Contains("回退", state.Snapshot.Status);
        Assert.Contains("没有游戏账号", state.Snapshot.Status);
    }

    [Theory]
    [InlineData(0, "尚未创建本地档案")]
    [InlineData(1, "此档案还没有游戏账号")]
    [InlineData(2, "尚未记录此账号的抽卡数据")]
    public async Task EmptyStatesRemainDistinct(int scenario, string message)
    {
        var source = new Source();
        if (scenario == 0) source.Archives = [];
        if (scenario == 1) source.Accounts.Clear();
        source.Page = (_, page, _) => Task.FromResult(new GachaReadOnlyPage([], 0, page, 1));
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await state.EnterAsync();
        Assert.Equal(message, state.Snapshot.Status);
        Assert.False(state.Snapshot.IsBusy);
        Assert.False(state.Snapshot.CanNext);
    }

    [Fact]
    public async Task LateAccountResultCannotOverwriteNewScopeEvenWhenSourceIgnoresCancellation()
    {
        var source = new Source();
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await state.EnterAsync();
        var late = new TaskCompletionSource<GachaReadOnlyPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Page = (id, page, _) => id == source.A.Id ? late.Task : Task.FromResult(Source.MakePage("B", page));
        Task a = state.SelectAccountAsync(source.A.Id);
        await state.SelectAccountAsync(source.B.Id);
        late.SetResult(Source.MakePage("A", 1));
        await a;
        Assert.Equal(source.B.Id, state.Snapshot.AccountId);
        Assert.Equal("B", Assert.Single(state.Snapshot.Rows).Name);
    }

    [Fact]
    public async Task LateArchiveEnumerationCannotChangeOptionsOrStartStalePageQuery()
    {
        var source = new Source();
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        var late = new TaskCompletionSource<IReadOnlyList<GachaScopeOption>>(TaskCreationOptions.RunContinuationsAsynchronously);
        source.ArchiveLoad = _ => late.Task;
        Task old = state.EnterAsync();
        source.ArchiveLoad = null;
        await state.SelectAccountAsync(source.B.Id);
        late.SetResult([new(Guid.NewGuid(), "old archive")]);
        await old;
        Assert.Equal(source.B.Id, state.Snapshot.AccountId);
        Assert.Equal(source.Archives[0].Id, Assert.Single(state.Snapshot.Archives).Id);
        Assert.Equal(1, source.PageCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelAndLeaveInvalidatePendingResultsAndPermitRetry(bool leave)
    {
        var source = new Source();
        var late = new TaskCompletionSource<GachaReadOnlyPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observed = default;
        source.Page = (_, _, token) => { observed = token; return late.Task; };
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        Task loading = state.EnterAsync();
        Assert.True(state.Snapshot.IsBusy);
        if (leave) state.Leave(); else state.Cancel();
        Assert.True(observed.IsCancellationRequested);
        late.SetResult(Source.MakePage("obsolete", 1));
        await loading;
        Assert.Empty(state.Snapshot.Rows);
        Assert.False(state.Snapshot.IsBusy);
        source.Page = (_, page, _) => Task.FromResult(Source.MakePage("retry", page));
        await state.EnterAsync();
        Assert.Equal("retry", Assert.Single(state.Snapshot.Rows).Name);
    }

    [Fact]
    public async Task QueuedUiCallbackChecksLifetimeWhenItActuallyRuns()
    {
        var source = new Source();
        var dispatcher = new QueuedDispatcher();
        var state = new GachaReadOnlyState(source, dispatcher);
        Task loading = state.EnterAsync();
        await dispatcher.Enqueued.Task;
        state.Leave();
        dispatcher.Run();
        await loading;
        Assert.Null(state.Snapshot.AccountId);
        Assert.Empty(state.Snapshot.Rows);
        Assert.Equal(0, source.PageCalls);
    }

    [Fact]
    public async Task FailureClearsBusyWithoutPublishingExceptionContentsAndCanRetry()
    {
        var source = new Source();
        source.Page = (_, _, _) => throw new IOException("sensitive source content");
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await state.EnterAsync();
        Assert.Contains("失败", state.Snapshot.Status);
        Assert.DoesNotContain("sensitive", state.Snapshot.Status);
        Assert.False(state.Snapshot.IsBusy);
        source.Page = (_, page, _) => Task.FromResult(Source.MakePage("recovered", page));
        await state.EnterAsync();
        Assert.Equal("recovered", Assert.Single(state.Snapshot.Rows).Name);
    }

    [Fact]
    public async Task PageQueryKeepsScopeAndUsesServiceNormalizedPage()
    {
        var source = new Source();
        var state = new GachaReadOnlyState(source, new ImmediateDispatcher());
        await state.EnterAsync();
        await state.SelectAccountAsync(source.B.Id);
        source.Page = (id, _, _) =>
        {
            Assert.Equal(source.B.Id, id);
            return Task.FromResult(Source.MakePage("last", 3));
        };
        await state.ChangePageAsync(99);
        Assert.Equal(3, state.Snapshot.PageNumber);
        Assert.Equal(3, state.Snapshot.TotalPages);
        Assert.False(state.Snapshot.CanNext);
        Assert.True(state.Snapshot.CanPrevious);
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private Action? callback;
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Enqueued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InvokeAsync(Action action) { callback = action; Enqueued.SetResult(); return completion.Task; }
        public void Run() { callback!(); completion.SetResult(); }
    }

    private sealed class Source : IGachaReadOnlySource
    {
        public GachaScopeOption A { get; } = new(Guid.NewGuid(), "A");
        public GachaScopeOption B { get; } = new(Guid.NewGuid(), "B");
        public IReadOnlyList<GachaScopeOption> Archives { get; set; } = [new(Guid.NewGuid(), "archive")];
        public List<GachaScopeOption> Accounts { get; }
        public Func<CancellationToken, Task<IReadOnlyList<GachaScopeOption>>>? ArchiveLoad { get; set; }
        public Func<Guid, int, CancellationToken, Task<GachaReadOnlyPage>> Page { get; set; } =
            (_, page, _) => Task.FromResult(MakePage("row", page));
        public int LastPageSize { get; private set; }
        public int PageCalls { get; private set; }
        public Source() { Accounts = [A, B]; }
        public Task<IReadOnlyList<GachaScopeOption>> GetArchivesAsync(CancellationToken cancellationToken) =>
            ArchiveLoad?.Invoke(cancellationToken) ?? Task.FromResult(Archives);
        public Task<IReadOnlyList<GachaScopeOption>> GetAccountsAsync(Guid archiveId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GachaScopeOption>>(Accounts.ToArray());
        public Task<GachaReadOnlyPage> GetPageAsync(Guid accountId, int pageNumber, int pageSize, CancellationToken cancellationToken)
        {
            LastPageSize = pageSize;
            PageCalls++;
            return Page(accountId, pageNumber, cancellationToken);
        }
        public static GachaReadOnlyPage MakePage(string name, int page) =>
            new([new("1", name, "3 星", "2026-10-01 +08:00", "角色活动祈愿", "来源未知", new(null, null))], 61, page, 3);
    }
}

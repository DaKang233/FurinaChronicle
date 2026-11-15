// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChroniclePageStateTests
{
    private static readonly Guid AccountId = ChronicleTestEntries.AccountId;

    [Fact]
    public void FirstPage_LoadsEntriesCursorAndGeneration()
    {
        var state = new ChroniclePageState();
        ChronicleEntry first = ChronicleTestEntries.Create(seed: "1");

        Assert.True(state.BeginInitialLoad(Query()));
        Assert.Equal(ChronicleLoadState.LoadingFirstPage, state.LoadState);

        state.ApplyPage(ChronicleTestEntries.Page(
            [first],
            nextCursor: "cursor-a"));

        Assert.Equal(ChronicleLoadState.Loaded, state.LoadState);
        Assert.Single(state.Entries);
        Assert.Equal("cursor-a", state.NextCursor);
        Assert.Equal("generation-1", state.GenerationId);
        Assert.True(state.HasMore);
        Assert.False(state.RequiresReload);
    }

    [Fact]
    public void LoadMore_AppendsAndDeduplicatesEntries()
    {
        var state = new ChroniclePageState();
        ChronicleEntry first = ChronicleTestEntries.Create(seed: "1");
        ChronicleEntry second = ChronicleTestEntries.Create(seed: "2");
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page([first], "cursor-a"));

        Assert.True(state.BeginLoadMore());
        Assert.Equal(ChronicleLoadState.LoadingMore, state.LoadState);
        state.ApplyPage(ChronicleTestEntries.Page([first, second]));

        Assert.Equal(2, state.LoadedCount);
        Assert.Null(state.NextCursor);
        Assert.False(state.HasMore);
        Assert.Equal(ChronicleLoadState.Loaded, state.LoadState);
    }

    [Fact]
    public void CursorInvalidated_ClearsSnapshotAndRequiresReload()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));
        state.BeginLoadMore();

        state.ApplyPage(ChronicleTestEntries.Page(
            [],
            error: ChronicleQueryError.CursorInvalidated,
            errorMessage: "The Chronicle generation was invalidated."));

        Assert.Empty(state.Entries);
        Assert.Equal(ChronicleStaleReason.CursorInvalidated, state.StaleReason);
        Assert.True(state.RequiresReload);
        Assert.False(state.HasMore);
        Assert.Equal(ChronicleLoadState.Error, state.LoadState);
        Assert.Contains("纠正、删除或撤销", state.ErrorMessage);
        Assert.False(state.BeginLoadMore());
    }

    [Fact]
    public void CursorExpired_KeepsLoadedEntriesButBlocksContinuation()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));
        state.BeginLoadMore();

        state.ApplyPage(ChronicleTestEntries.Page(
            [],
            error: ChronicleQueryError.CursorExpired));

        Assert.Single(state.Entries);
        Assert.Equal(ChronicleStaleReason.CursorExpired, state.StaleReason);
        Assert.True(state.RequiresReload);
        Assert.False(state.HasMore);
        Assert.Null(state.NextCursor);
        Assert.Contains("快照已过期", state.ErrorMessage);
    }

    [Fact]
    public void GenerationChanged_DoesNotMergeDifferentSnapshots()
    {
        var state = new ChroniclePageState();
        ChronicleEntry first = ChronicleTestEntries.Create(seed: "1");
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page([first], "cursor-a"));
        state.BeginLoadMore();

        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "2")],
            generationId: "generation-2"));

        Assert.Same(first, Assert.Single(state.Entries));
        Assert.Equal(ChronicleStaleReason.GenerationChanged, state.StaleReason);
        Assert.True(state.RequiresReload);
        Assert.Null(state.NextCursor);
        Assert.Contains("新数据快照", state.ErrorMessage);
    }

    [Fact]
    public void EmptyFirstPage_EntersEmptyState()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());

        state.ApplyPage(ChronicleTestEntries.Page([]));

        Assert.Equal(ChronicleLoadState.Empty, state.LoadState);
        Assert.True(state.IsEmpty);
        Assert.Empty(state.Entries);
        Assert.False(state.HasMore);
        Assert.Contains(
            "还没有足够的领域数据",
            ChronicleEmptyStateText.Describe(ChronicleEmptyReason.NoData));
        Assert.Contains(
            "放宽时间范围",
            ChronicleEmptyStateText.Describe(ChronicleEmptyReason.FilteredOut));
    }

    [Fact]
    public void UnsupportedFilter_IsErrorWithoutStaleSnapshot()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());

        state.ApplyQueryError(ChronicleQueryError.UnsupportedFilter, "technical");

        Assert.Equal(ChronicleLoadState.Error, state.LoadState);
        Assert.False(state.RequiresReload);
        Assert.Equal(ChronicleStaleReason.None, state.StaleReason);
        Assert.Contains("筛选条件", state.ErrorMessage);
        Assert.Equal("technical", state.DiagnosticMessage);
    }

    [Fact]
    public void TransportError_KeepsLoadedEntriesAndAllowsRetry()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));
        state.BeginLoadMore();

        state.ApplyTransportError("network down");

        Assert.Single(state.Entries);
        Assert.Equal(ChronicleLoadState.Loaded, state.LoadState);
        Assert.False(state.RequiresReload);
        Assert.True(state.HasMore);
        Assert.Equal("network down", state.DiagnosticMessage);
        Assert.Contains("读取游戏历程失败", state.ErrorMessage);
        Assert.True(state.BeginLoadMore());
    }

    [Fact]
    public void InvalidCursor_KeepsEntriesButDropsStaleCursor()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));
        state.BeginLoadMore();

        state.ApplyQueryError(ChronicleQueryError.InvalidCursor);

        Assert.Single(state.Entries);
        Assert.Null(state.NextCursor);
        Assert.Equal(ChronicleStaleReason.CursorInvalid, state.StaleReason);
        Assert.True(state.RequiresReload);
    }

    [Fact]
    public void BeginLoadMore_RequiresCursorAndNoStaleState()
    {
        var state = new ChroniclePageState();

        Assert.False(state.BeginLoadMore());

        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page([]));
        Assert.False(state.BeginLoadMore());
    }

    [Fact]
    public void Reset_ClearsEveryStateValue()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));

        state.Reset();

        Assert.Equal(ChronicleLoadState.Idle, state.LoadState);
        Assert.Empty(state.Entries);
        Assert.Null(state.Query);
        Assert.Null(state.NextCursor);
        Assert.Null(state.GenerationId);
        Assert.Equal(ChronicleStaleReason.None, state.StaleReason);
        Assert.False(state.RequiresReload);
    }

    [Fact]
    public void ApplyPageWithoutPendingRequest_IsIgnored()
    {
        var state = new ChroniclePageState();
        state.BeginInitialLoad(Query());
        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "1")],
            "cursor-a"));

        state.ApplyPage(ChronicleTestEntries.Page(
            [ChronicleTestEntries.Create(seed: "2")]));

        Assert.Single(state.Entries);
    }

    private static ChronicleQuery Query(string? cursor = null) => new(
        new ChronicleScope(ChronicleScopeKind.Account, AccountId),
        new ChronicleProfileRequest(ChronicleTimeline.Main),
        new ChronicleFilter(IncludeUndated: true),
        PageSize: 2,
        Cursor: cursor);
}

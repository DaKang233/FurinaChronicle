// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChronicleQueryControllerTests
{
    private static readonly Guid AccountId = ChronicleTestEntries.AccountId;

    [Fact]
    public async Task LoadFirstPage_AppliesPageAndReportsLoaded()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(seed: "1");
        var service = new FakeChronicleQueryService(
            (_, _) => Task.FromResult(
                ChronicleTestEntries.Page([entry], "cursor-a")));
        var controller = new ChronicleQueryController(service);

        ChronicleLoadOutcome outcome =
            await controller.LoadFirstPageAsync(Query());

        Assert.Equal(ChronicleLoadOutcome.Loaded, outcome);
        Assert.Equal(ChronicleLoadState.Loaded, controller.State.LoadState);
        Assert.Single(controller.State.Entries);
        Assert.Equal("cursor-a", controller.State.NextCursor);
    }

    [Fact]
    public async Task LoadMore_UsesStoredCursorAndAppends()
    {
        var service = new FakeChronicleQueryService(
            (query, _) => Task.FromResult(query.Cursor is null
                ? ChronicleTestEntries.Page(
                    [ChronicleTestEntries.Create(seed: "1")],
                    "cursor-a")
                : ChronicleTestEntries.Page(
                    [ChronicleTestEntries.Create(seed: "2")])));
        var controller = new ChronicleQueryController(service);
        await controller.LoadFirstPageAsync(Query());

        ChronicleLoadOutcome outcome = await controller.LoadMoreAsync();

        Assert.Equal(ChronicleLoadOutcome.Loaded, outcome);
        Assert.Equal(2, service.Queries.Count);
        Assert.Equal("cursor-a", service.Queries[1].Cursor);
        Assert.Equal(2, controller.State.LoadedCount);
        Assert.Null(controller.State.NextCursor);
    }

    [Fact]
    public async Task NewQuery_SupersedesInFlightRequest()
    {
        int calls = 0;
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeChronicleQueryService(async (_, cancellationToken) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.TrySetResult();
                await gate.Task.WaitAsync(cancellationToken);
                return ChronicleTestEntries.Page(
                    [ChronicleTestEntries.Create(seed: "first")],
                    "cursor-a");
            }

            return ChronicleTestEntries.Page(
                [ChronicleTestEntries.Create(seed: "second")],
                generationId: "generation-2");
        });
        var controller = new ChronicleQueryController(service);

        Task<ChronicleLoadOutcome> first =
            controller.LoadFirstPageAsync(Query(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ChronicleLoadOutcome second =
            await controller.LoadFirstPageAsync(Query(2));

        Assert.Equal(ChronicleLoadOutcome.Superseded, await first);
        Assert.Equal(ChronicleLoadOutcome.Loaded, second);
        Assert.Single(controller.State.Entries);
        Assert.Equal("second", controller.State.Entries[0].SourceEntityId);
        Assert.Equal("generation-2", controller.State.GenerationId);
    }

    [Fact]
    public async Task TransportException_BecomesFailedStateWithGenericMessage()
    {
        var service = new FakeChronicleQueryService(
            (_, _) => throw new InvalidOperationException("socket closed"));
        var controller = new ChronicleQueryController(service);

        ChronicleLoadOutcome outcome =
            await controller.LoadFirstPageAsync(Query());

        Assert.Equal(ChronicleLoadOutcome.Failed, outcome);
        Assert.Equal(ChronicleLoadState.Error, controller.State.LoadState);
        Assert.Contains("读取游戏历程失败", controller.State.ErrorMessage);
        Assert.Equal("socket closed", controller.State.DiagnosticMessage);
    }

    [Fact]
    public async Task CursorInvalidated_ReportsStaleOutcome()
    {
        var service = new FakeChronicleQueryService(
            (_, _) => Task.FromResult(ChronicleTestEntries.Page(
                [],
                error: ChronicleQueryError.CursorInvalidated)));
        var controller = new ChronicleQueryController(service);

        ChronicleLoadOutcome outcome =
            await controller.LoadFirstPageAsync(Query());

        Assert.Equal(ChronicleLoadOutcome.Stale, outcome);
        Assert.True(controller.State.RequiresReload);
        Assert.Equal(
            ChronicleStaleReason.CursorInvalidated,
            controller.State.StaleReason);
    }

    [Fact]
    public async Task EmptyPage_ReportsEmptyOutcome()
    {
        var service = new FakeChronicleQueryService(
            (_, _) => Task.FromResult(ChronicleTestEntries.Page([])));
        var controller = new ChronicleQueryController(service);

        ChronicleLoadOutcome outcome =
            await controller.LoadFirstPageAsync(Query());

        Assert.Equal(ChronicleLoadOutcome.Empty, outcome);
        Assert.Equal(ChronicleLoadState.Empty, controller.State.LoadState);
    }

    [Fact]
    public async Task LoadMore_WithoutCursor_IsSupersededWithoutQuerying()
    {
        var service = new FakeChronicleQueryService(
            (_, _) => Task.FromResult(ChronicleTestEntries.Page([])));
        var controller = new ChronicleQueryController(service);

        ChronicleLoadOutcome outcome = await controller.LoadMoreAsync();

        Assert.Equal(ChronicleLoadOutcome.Superseded, outcome);
        Assert.Empty(service.Queries);
    }

    [Fact]
    public async Task LoadMore_AfterStaleSnapshot_IsSupersededWithoutQuerying()
    {
        var service = new FakeChronicleQueryService(
            (_, _) => Task.FromResult(ChronicleTestEntries.Page(
                [ChronicleTestEntries.Create(seed: "1")],
                "cursor-a",
                error: ChronicleQueryError.CursorInvalidated)));
        var controller = new ChronicleQueryController(service);
        await controller.LoadFirstPageAsync(Query());

        ChronicleLoadOutcome outcome = await controller.LoadMoreAsync();

        Assert.Equal(ChronicleLoadOutcome.Superseded, outcome);
        Assert.Single(service.Queries);
    }

    [Fact]
    public async Task Reset_IgnoresInFlightResult()
    {
        var started = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new FakeChronicleQueryService(async (_, cancellationToken) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return ChronicleTestEntries.Page(
                [ChronicleTestEntries.Create(seed: "1")]);
        });
        var controller = new ChronicleQueryController(service);
        Task<ChronicleLoadOutcome> pending =
            controller.LoadFirstPageAsync(Query());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        controller.Reset();

        Assert.Equal(ChronicleLoadOutcome.Superseded, await pending);
        Assert.Equal(ChronicleLoadState.Idle, controller.State.LoadState);
        Assert.Empty(controller.State.Entries);
    }

    private static ChronicleQuery Query(int pageSize = 2) => new(
        new ChronicleScope(ChronicleScopeKind.Account, AccountId),
        new ChronicleProfileRequest(ChronicleTimeline.Main),
        new ChronicleFilter(IncludeUndated: true),
        pageSize);

    private sealed class FakeChronicleQueryService(
        Func<ChronicleQuery, CancellationToken, Task<ChroniclePage>> handler)
        : IChronicleQueryService
    {
        private readonly object gate = new();

        public List<ChronicleQuery> Queries { get; } = [];

        public Task<ChroniclePage> QueryAsync(
            ChronicleQuery query,
            CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                Queries.Add(query);
            }
            return handler(query, cancellationToken);
        }

        public Task RebuildAsync(
            ChronicleScope scope,
            IChronicleItemCatalogSnapshot itemCatalog,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearCacheAsync(
            ChronicleScope scope,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task ExportMarkdownAsync(
            ChronicleQuery query,
            TextWriter destination,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

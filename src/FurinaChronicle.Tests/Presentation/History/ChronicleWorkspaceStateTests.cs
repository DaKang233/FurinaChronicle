// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Chronicle;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class ChronicleWorkspaceStateTests
{
    private static readonly DateTimeOffset Time =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task Enter_LoadsLocalizedMainTimelineEntries()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await CommitGachaAsync(context, account.Id, "1", Time, rank: 5);
        ChronicleWorkspaceState state = CreateState(context);

        await state.EnterAsync();

        Assert.NotEmpty(state.Snapshot.Entries);
        Assert.Contains(
            state.Snapshot.Entries,
            entry => entry.Title.Contains("获得五星角色", StringComparison.Ordinal));
        Assert.NotNull(state.Snapshot.SelectedEntry);
        Assert.Contains("已载入", state.Snapshot.Status);
        string markdown = state.BuildMarkdownReport();
        Assert.Contains("# 游戏历程", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("chronicle.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoArchives_ReportsNoArchiveEmptyState()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        ChronicleWorkspaceState state = CreateState(context);

        await state.EnterAsync();

        Assert.Empty(state.Snapshot.Archives);
        Assert.Contains("还没有本地档案", state.Snapshot.EmptyMessage);
        Assert.False(state.Snapshot.IsBusy);
    }

    [Fact]
    public async Task ArchiveWithoutAccounts_ReportsNoAccountEmptyState()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Archives.AddAsync(
            SqliteRepositoryTestContext.CreateArchive("空档案"));
        ChronicleWorkspaceState state = CreateState(context);

        await state.EnterAsync();

        Assert.Empty(state.Snapshot.Accounts);
        Assert.Contains("还没有游戏账号", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task EmptyDomainData_ReportsNoDataEmptyState()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await Parents(context);
        ChronicleWorkspaceState state = CreateState(context);

        await state.EnterAsync();

        Assert.Empty(state.Snapshot.Entries);
        Assert.Contains("还没有足够的领域数据", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task LoadMore_AppendsWithoutDuplicates()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        for (int index = 1; index <= 35; index++)
        {
            await CommitGachaAsync(
                context,
                account.Id,
                index.ToString("D4"),
                Time.AddMinutes(index),
                rank: 5);
        }
        ChronicleWorkspaceState state = CreateState(context);
        await state.EnterAsync();

        Assert.Equal(30, state.Snapshot.Entries.Count);
        Assert.True(state.Snapshot.HasMore);

        await state.LoadMoreAsync();

        Assert.Equal(35, state.Snapshot.Entries.Count);
        Assert.False(state.Snapshot.HasMore);
        Assert.Equal(
            state.Snapshot.Entries.Count,
            state.Snapshot.Entries
                .Select(entry => entry.EntryId)
                .Distinct(StringComparer.Ordinal)
                .Count());
    }

    [Fact]
    public async Task Correction_InvalidatesCursorAndShowsStaleMessage()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        for (int index = 1; index <= 35; index++)
        {
            await CommitGachaAsync(
                context,
                account.Id,
                index.ToString("D4"),
                Time.AddMinutes(index),
                rank: 5);
        }
        ChronicleWorkspaceState state = CreateState(context);
        await state.EnterAsync();
        Assert.True(state.Snapshot.HasMore);

        await CommitGachaAsync(
            context,
            account.Id,
            "0001",
            Time.AddMinutes(2),
            rank: 4,
            kind: DataChangeOperationKind.Correction,
            expectedVersion: 1);

        await state.LoadMoreAsync();

        Assert.Contains("纠正、删除或撤销", state.Snapshot.StaleMessage);
        Assert.Empty(state.Snapshot.Entries);
        Assert.False(state.Snapshot.HasMore);
    }

    [Fact]
    public async Task ClearedCache_ExpiresContinuationAndKeepsLoadedEntries()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        for (int index = 1; index <= 35; index++)
        {
            await CommitGachaAsync(
                context,
                account.Id,
                index.ToString("D4"),
                Time.AddMinutes(index),
                rank: 5);
        }
        var service = Service(context);
        var state = new ChronicleWorkspaceState(
            service,
            new GetPlayerArchives(context.Archives),
            new GetGameAccounts(context.Archives, context.Accounts));
        await state.EnterAsync();
        await service.ClearCacheAsync(
            new ChronicleScope(ChronicleScopeKind.Account, account.Id));

        await state.LoadMoreAsync();

        Assert.Contains("快照已过期", state.Snapshot.StaleMessage);
        Assert.NotEmpty(state.Snapshot.Entries);
        Assert.False(state.Snapshot.HasMore);
    }

    [Fact]
    public async Task FilterToEmptyDomain_ReportsFilteredOutEmptyState()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await CommitGachaAsync(context, account.Id, "1", Time, rank: 5);
        ChronicleWorkspaceState state = CreateState(context);
        await state.EnterAsync();
        Assert.NotEmpty(state.Snapshot.Entries);

        await state.SetDomainAsync(ChronicleDomain.Achievement);

        Assert.Empty(state.Snapshot.Entries);
        Assert.Contains("筛选条件", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task Cancel_StopsInFlightQueryWithoutThrowing()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await CommitGachaAsync(context, account.Id, "1", Time, rank: 5);
        var slow = new FirstQuerySlowChronicleService(Service(context));
        var state = new ChronicleWorkspaceState(
            slow,
            new GetPlayerArchives(context.Archives),
            new GetGameAccounts(context.Archives, context.Accounts));

        Task entering = state.EnterAsync();
        await slow.FirstQueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.Cancel();
        await entering;

        Assert.False(state.Snapshot.IsBusy);
    }

    private static ChronicleWorkspaceState CreateState(
        SqliteRepositoryTestContext context) => new(
        Service(context),
        new GetPlayerArchives(context.Archives),
        new GetGameAccounts(context.Archives, context.Accounts));

    private static SqliteChronicleQueryService Service(
        SqliteRepositoryTestContext context) => new(
        context.Database,
        context.Gacha,
        context.Accounts,
        context.AtomicGacha);

    private static async Task<(PlayerArchive, GameAccount)> Parents(
        SqliteRepositoryTestContext context)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return (archive, account);
    }

    private static async Task CommitGachaAsync(
        SqliteRepositoryTestContext context,
        Guid accountId,
        string externalId,
        DateTimeOffset time,
        int rank = 5,
        DataChangeOperationKind kind = DataChangeOperationKind.Import,
        long? expectedVersion = null)
    {
        var record = new GachaRecord(
            accountId,
            externalId,
            "Synthetic",
            rank,
            time)
        {
            ItemId = "synthetic-five",
            ItemType = "Character",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(DataOrigin.StandardImport),
        };
        var mutation = new GachaFactMutation(
            new GachaFactReference(accountId, externalId),
            record,
            expectedVersion is null ? null : new FactVersion(expectedVersion.Value));
        var request = new GachaAtomicChangeRequest(
            OperationId.New(),
            kind,
            DataOrigin.StandardImport,
            "synthetic chronicle workspace test",
            time,
            time,
            [mutation],
            captureUndo: true);
        GachaAtomicChangeResult result = await context.AtomicGacha.CommitAsync(
            request);
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
    }

    private sealed class FirstQuerySlowChronicleService(
        IChronicleQueryService inner) : IChronicleQueryService
    {
        private int calls;

        public TaskCompletionSource FirstQueryStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ChroniclePage> QueryAsync(
            ChronicleQuery query,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                FirstQueryStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await inner.QueryAsync(query, cancellationToken);
        }

        public Task RebuildAsync(
            ChronicleScope scope,
            IChronicleItemCatalogSnapshot itemCatalog,
            CancellationToken cancellationToken = default) =>
            inner.RebuildAsync(scope, itemCatalog, cancellationToken);

        public Task ClearCacheAsync(
            ChronicleScope scope,
            CancellationToken cancellationToken = default) =>
            inner.ClearCacheAsync(scope, cancellationToken);

        public Task ExportMarkdownAsync(
            ChronicleQuery query,
            TextWriter destination,
            CancellationToken cancellationToken = default) =>
            inner.ExportMarkdownAsync(query, destination, cancellationToken);
    }
}

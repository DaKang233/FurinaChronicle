// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class CharacterWeaponHistoryStateTests
{
    private static readonly DateTimeOffset Time =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task NoArchives_ReportsNoArchiveEmptyState()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Empty(state.Snapshot.Archives);
        Assert.Empty(state.Snapshot.Observations);
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
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Single(state.Snapshot.Archives);
        Assert.Empty(state.Snapshot.Accounts);
        Assert.Contains("还没有游戏账号", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task Observations_LoadLatestFirstAndSupportHistorySelection()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive archive, GameAccount account) = await Parents(context);
        Guid older = await CommitItemAsync(
            context, account.Id, 1, Time.AddHours(-2), level: 80);
        Guid newer = await CommitItemAsync(
            context, account.Id, 2, Time, level: 90);
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Equal(2, state.Snapshot.Observations.Count);
        Assert.Equal(newer, state.Snapshot.SelectedObservationId);
        Assert.NotNull(state.Snapshot.SelectedObservation);
        Assert.Contains("观察于", state.Snapshot.SelectedObservation!.ObservedText);
        Assert.Contains("官方 API", state.Snapshot.SelectedObservation.SourceText);
        Assert.Contains("完整度", state.Snapshot.SelectedObservation.CompletenessText);

        state.SelectObservation(older);

        Assert.Equal(older, state.Snapshot.SelectedObservationId);
        Assert.Equal(older, state.Snapshot.SelectedObservation!.ObservationId);
        Assert.Contains("UID", state.Snapshot.SelectedObservation.AccountLabel);
    }

    [Fact]
    public async Task KindSwitch_ReloadsOnlyThatItemKind()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await CommitItemAsync(context, account.Id, 1, Time, level: 90);
        await CommitItemAsync(context, account.Id, 3, Time, level: 80);
        await CommitItemAsync(
            context,
            account.Id,
            2,
            Time,
            kind: InventoryItemKind.Weapon,
            level: 90);
        var state = CreateState(context);
        await state.EnterAsync();
        Assert.Equal(2, state.Snapshot.Observations.Count);

        await state.SetKindAsync(InventoryItemKind.Weapon);

        Assert.Equal(InventoryItemKind.Weapon, state.Snapshot.Kind);
        Assert.Single(state.Snapshot.Observations);
        Assert.Contains("武器", state.Snapshot.Observations[0].Title);
    }

    [Fact]
    public async Task Paging_UsesOffsetAndTotalCount()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        ItemStateObservation[] observations = Enumerable.Range(1, 25)
            .Select(index => ItemObservation(
                account.Id,
                index,
                Time.AddMinutes(-index),
                level: 80))
            .ToArray();
        await CommitItemsAsync(context, observations);
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Equal(20, state.Snapshot.Observations.Count);
        Assert.Equal(25, state.Snapshot.TotalCount);
        Assert.True(state.Snapshot.HasNext);
        Assert.False(state.Snapshot.HasPrevious);

        await state.NextPageAsync();

        Assert.Equal(5, state.Snapshot.Observations.Count);
        Assert.Equal(20, state.Snapshot.Offset);
        Assert.True(state.Snapshot.HasPrevious);
        Assert.False(state.Snapshot.HasNext);

        await state.PreviousPageAsync();

        Assert.Equal(20, state.Snapshot.Observations.Count);
        Assert.Equal(0, state.Snapshot.Offset);
    }

    [Fact]
    public async Task NewSelection_CancelsPreviousQueryAndKeepsLatestSelection()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (PlayerArchive archive, GameAccount first) = await Parents(context);
        GameAccount second = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, uid: "800000099", displayName: "第二账号");
        await context.Accounts.AddAsync(second);
        var slow = new FirstPageSlowHistoryStore(context.AtomicGacha);
        var state = new CharacterWeaponHistoryState(
            slow,
            new GetPlayerArchives(context.Archives),
            new GetGameAccounts(context.Archives, context.Accounts));

        Task entering = state.EnterAsync();
        await slow.FirstPageStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task selecting = state.SelectAccountAsync(second.Id);
        await Task.WhenAll(entering, selecting);

        Assert.Equal(second.Id, state.Snapshot.SelectedAccountId);
        Assert.False(state.Snapshot.IsBusy);
    }

    private static CharacterWeaponHistoryState CreateState(
        SqliteRepositoryTestContext context) => new(
        context.AtomicGacha,
        new GetPlayerArchives(context.Archives),
        new GetGameAccounts(context.Archives, context.Accounts));

    private static async Task<(PlayerArchive, GameAccount)> Parents(
        SqliteRepositoryTestContext context)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return (archive, account);
    }

    private static async Task<Guid> CommitItemAsync(
        SqliteRepositoryTestContext context,
        Guid accountId,
        int seed,
        DateTimeOffset observed,
        InventoryItemKind kind = InventoryItemKind.Character,
        int? level = 90)
    {
        ItemStateObservation observation = ItemObservation(
            accountId,
            seed,
            observed,
            kind,
            level);
        await CommitItemsAsync(context, [observation], observed);
        return observation.ObservationId.Value;
    }

    private static async Task CommitItemsAsync(
        SqliteRepositoryTestContext context,
        IReadOnlyList<ItemStateObservation> observations,
        DateTimeOffset? committedAt = null)
    {
        DateTimeOffset commit = committedAt ?? Time;
        ArchiveOperationResult result =
            await context.AtomicGacha.CommitItemObservationsAsync(
                new ItemObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    "synthetic item observation",
                    commit,
                    commit,
                    observations));
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
    }

    private static ItemStateObservation ItemObservation(
        Guid accountId,
        int seed,
        DateTimeOffset observed,
        InventoryItemKind kind = InventoryItemKind.Character,
        int? level = 90) => new(
        new ItemObservationId(
            Guid.Parse($"00000000-0000-4000-8000-{seed:D12}")),
        accountId,
        kind,
        DataCompleteness.Complete,
        [
            new InventoryStateEntry(
                kind,
                $"type-{seed}",
                kind == InventoryItemKind.Weapon
                    ? $"instance-{seed}"
                    : null,
                kind == InventoryItemKind.Weapon ? 1 : null,
                level,
                progression: 0,
                InventoryFieldCoverage.Level |
                    InventoryFieldCoverage.Progression,
                DataCompleteness.Complete),
        ],
        new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(
                ObservedAt: observed,
                FetchedAt: observed.AddMinutes(1))));

    private sealed class FirstPageSlowHistoryStore(
        IArchiveDomainHistoryStore inner) : IArchiveDomainHistoryStore
    {
        private int pageCalls;

        public TaskCompletionSource FirstPageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ArchiveDomainPage<ItemStateObservation>>
            GetItemObservationPageAsync(
                ItemObservationPageQuery query,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref pageCalls) == 1)
            {
                FirstPageStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await inner.GetItemObservationPageAsync(
                query,
                cancellationToken);
        }

        public Task<ArchiveOperationResult> CommitItemObservationsAsync(
            ItemObservationCommitRequest request,
            CancellationToken cancellationToken = default) =>
            inner.CommitItemObservationsAsync(request, cancellationToken);

        public Task<ArchiveOperationResult> CommitManualAcquisitionsAsync(
            ManualAcquisitionCommitRequest request,
            CancellationToken cancellationToken = default) =>
            inner.CommitManualAcquisitionsAsync(request, cancellationToken);

        public Task<ArchiveOperationResult> CommitChallengeObservationsAsync(
            ChallengeObservationCommitRequest request,
            CancellationToken cancellationToken = default) =>
            inner.CommitChallengeObservationsAsync(request, cancellationToken);

        public Task<IReadOnlyList<ItemStateObservation>> GetItemObservationsAsync(
            Guid gameAccountId,
            InventoryItemKind? kind = null,
            CancellationToken cancellationToken = default) =>
            inner.GetItemObservationsAsync(gameAccountId, kind, cancellationToken);

        public Task<IReadOnlyList<ManualAcquisitionState>>
            GetManualAcquisitionsAsync(
                Guid gameAccountId,
                bool includeDeleted = false,
                CancellationToken cancellationToken = default) =>
            inner.GetManualAcquisitionsAsync(
                gameAccountId,
                includeDeleted,
                cancellationToken);

        public Task<ArchiveDomainPage<ManualAcquisitionState>>
            GetManualAcquisitionPageAsync(
                ManualAcquisitionPageQuery query,
                CancellationToken cancellationToken = default) =>
            inner.GetManualAcquisitionPageAsync(query, cancellationToken);

        public Task<IReadOnlyList<ChallengeObservation>>
            GetChallengeObservationsAsync(
                Guid gameAccountId,
                ChallengeMode? mode = null,
                CancellationToken cancellationToken = default) =>
            inner.GetChallengeObservationsAsync(
                gameAccountId,
                mode,
                cancellationToken);

        public Task<ArchiveDomainPage<ChallengeObservation>>
            GetChallengeObservationPageAsync(
                ChallengeObservationPageQuery query,
                CancellationToken cancellationToken = default) =>
            inner.GetChallengeObservationPageAsync(query, cancellationToken);

        public Task<ChallengeObservation?> GetCurrentChallengeEvidenceAsync(
            Guid gameAccountId,
            ChallengeMode mode,
            string seasonId,
            CancellationToken cancellationToken = default) =>
            inner.GetCurrentChallengeEvidenceAsync(
                gameAccountId,
                mode,
                seasonId,
                cancellationToken);

        public Task<IReadOnlyList<DomainRevisionItem>> GetDomainRevisionsAsync(
            Guid changeSetId,
            CancellationToken cancellationToken = default) =>
            inner.GetDomainRevisionsAsync(changeSetId, cancellationToken);

        public Task<IReadOnlyList<ManualAcquisitionRevisionItem>>
            GetManualAcquisitionRevisionsAsync(
                Guid gameAccountId,
                CancellationToken cancellationToken = default) =>
            inner.GetManualAcquisitionRevisionsAsync(
                gameAccountId,
                cancellationToken);
    }
}

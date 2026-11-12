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

public sealed class ChallengeHistoryStateTests
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

        Assert.Empty(state.Snapshot.Accounts);
        Assert.Contains("还没有游戏账号", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task SeasonsAndObservations_FollowLatestFirstAndModeSwitch()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        await CommitChallengeAsync(
            context,
            Abyss(account.Id, "season-0", Time.AddDays(-10), 3));
        await CommitChallengeAsync(
            context,
            Abyss(account.Id, "season-1", Time, 3));
        await CommitChallengeAsync(
            context,
            Theater(account.Id, "31", Time.AddDays(-1)));
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Equal(ChallengeMode.SpiralAbyss, state.Snapshot.Mode);
        Assert.Equal(new[] { "season-1", "season-0" }, state.Snapshot.Seasons);
        Assert.Equal("season-1", state.Snapshot.SelectedSeasonId);
        Assert.Single(state.Snapshot.Observations);
        Assert.StartsWith("发生于", state.Snapshot.Observations[0].OccurredText);

        await state.SetSeasonAsync("season-0");

        Assert.Equal("season-0", state.Snapshot.SelectedSeasonId);
        Assert.Single(state.Snapshot.Observations);

        await state.SetModeAsync(ChallengeMode.ImaginariumTheater);

        Assert.Equal(ChallengeMode.ImaginariumTheater, state.Snapshot.Mode);
        Assert.Equal(new[] { "31" }, state.Snapshot.Seasons);
        Assert.Single(state.Snapshot.Observations);
        Assert.Contains("幻想真境剧诗", state.Snapshot.Observations[0].Title);

        await state.SetModeAsync(ChallengeMode.StygianOnslaught);

        Assert.Empty(state.Snapshot.Seasons);
        Assert.Empty(state.Snapshot.Observations);
        Assert.Contains("幽境危战", state.Snapshot.EmptyMessage);
    }

    [Fact]
    public async Task Paging_UsesOffsetAndTotalCount()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, GameAccount account) = await Parents(context);
        for (int index = 1; index <= 25; index++)
        {
            await CommitChallengeAsync(
                context,
                Abyss(account.Id, "season-1", Time.AddMinutes(-index), 3),
                commitAt: Time);
        }
        var state = CreateState(context);

        await state.EnterAsync();

        Assert.Equal(20, state.Snapshot.Observations.Count);
        Assert.Equal(25, state.Snapshot.TotalCount);
        Assert.True(state.Snapshot.HasNext);

        await state.NextPageAsync();

        Assert.Equal(5, state.Snapshot.Observations.Count);
        Assert.Equal(20, state.Snapshot.Offset);
        Assert.True(state.Snapshot.HasPrevious);

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
        await CommitChallengeAsync(
            context,
            Abyss(first.Id, "season-1", Time, 3));
        var slow = new FirstPageSlowChallengeStore(context.AtomicGacha);
        var state = new ChallengeHistoryState(
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

    private static ChallengeHistoryState CreateState(
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

    private static async Task CommitChallengeAsync(
        SqliteRepositoryTestContext context,
        ChallengeObservation observation,
        DateTimeOffset? commitAt = null)
    {
        DateTimeOffset commit = commitAt ??
            observation.Provenance.Timestamps.ObservedAt ??
            Time;
        ArchiveOperationResult result =
            await context.AtomicGacha.CommitChallengeObservationsAsync(
                new ChallengeObservationCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Refresh,
                    DataOrigin.OfficialApi,
                    "synthetic challenge observation",
                    commit,
                    commit,
                    [observation]));
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
    }

    private static ChallengeObservation Abyss(
        Guid accountId,
        string seasonId,
        DateTimeOffset occurredAt,
        int stars) => new(
        ChallengeObservationId.New(),
        accountId,
        ChallengeMode.SpiralAbyss,
        seasonId,
        null,
        null,
        DataCompleteness.Partial,
        [
            new SpiralAbyssFloorResult(
                12,
                DataCompleteness.Partial,
                [
                    new SpiralAbyssChamberResult(
                        3,
                        stars,
                        OccurrenceTime.At(occurredAt, TimePrecision.Second)),
                ]),
        ],
        new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(ObservedAt: occurredAt.AddMinutes(5))));

    private static ChallengeObservation Theater(
        Guid accountId,
        string seasonId,
        DateTimeOffset occurredAt) => new(
        ChallengeObservationId.New(),
        accountId,
        ChallengeMode.ImaginariumTheater,
        seasonId,
        null,
        null,
        DataCompleteness.Partial,
        [],
        new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(ObservedAt: occurredAt.AddMinutes(5))),
        [
            new ChallengeStageResult(
                "第一幕",
                score: 1,
                OccurrenceTime.At(occurredAt, TimePrecision.Second)),
        ],
        difficulty: 4,
        medalCount: 9);

    private sealed class FirstPageSlowChallengeStore(
        IArchiveDomainHistoryStore inner) : IArchiveDomainHistoryStore
    {
        private int pageCalls;

        public TaskCompletionSource FirstPageStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ArchiveDomainPage<ChallengeObservation>>
            GetChallengeObservationPageAsync(
                ChallengeObservationPageQuery query,
                CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref pageCalls) == 1)
            {
                FirstPageStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            return await inner.GetChallengeObservationPageAsync(
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

        public Task<ArchiveDomainPage<ItemStateObservation>>
            GetItemObservationPageAsync(
                ItemObservationPageQuery query,
                CancellationToken cancellationToken = default) =>
            inner.GetItemObservationPageAsync(query, cancellationToken);

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

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed partial class SpiralAbyssCollectionTests
{
    [Fact]
    public async Task BothPeriodsShareOneCommitAndUndoWithoutCreatingReplayObservations()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "recent-abyss");
        using var source = new TestSource();
        source.FetchOverride = (period, _) => Task.FromResult(RecentSnapshot(period, source.Time));
        var collector = CreateCollector(context, source, target);
        var passport = CreatePassport();
        var first = await collector.ExecuteRecentAsync(target.PlayerArchiveId, target.Id, passport,
            verificationPrompt: (_, _) => throw new InvalidOperationException("No challenge in this fixture"));
        Assert.Equal(ChangeExecutionStatus.Applied, first.Commit?.Status);
        Assert.Equal(new[] { SpiralAbyssPeriod.Current, SpiralAbyssPeriod.Previous }, source.RequestedPeriods);
        Assert.All(source.PromptAvailable, Assert.True);
        var stored = await context.AtomicGacha.GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, stored.Count);
        Assert.Single(stored.Select(item => item.Provenance.AcquisitionBatchId).Distinct());
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteRecentAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.Applied, (await ((IArchiveOperationHistoryStore)context.AtomicGacha).UndoLatestAsync(
            new ArchiveUndoRequest(OperationId.New(), target.PlayerArchiveId, source.Time.Now, source.Time.Now))).Status);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Theory]
    [InlineData(SpiralAbyssCollectionStatus.Empty)]
    [InlineData(SpiralAbyssCollectionStatus.NotUnlocked)]
    public async Task EmptyPreviousPeriodDoesNotPreventSavingCurrent(SpiralAbyssCollectionStatus empty)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "empty-previous");
        using var source = new TestSource();
        source.FetchOverride = (period, _) => Task.FromResult(period == SpiralAbyssPeriod.Current
            ? RecentSnapshot(period, source.Time) : new SpiralAbyssSourceResult(empty));
        var result = await CreateCollector(context, source, target).ExecuteRecentAsync(target.PlayerArchiveId, target.Id, CreatePassport());
        Assert.Equal(ChangeExecutionStatus.Applied, result.Commit?.Status);
        Assert.Single(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Theory]
    [InlineData(SpiralAbyssCollectionStatus.AuthenticationRequired)]
    [InlineData(SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(SpiralAbyssCollectionStatus.InvalidResponse)]
    [InlineData(SpiralAbyssCollectionStatus.VerificationCanceled)]
    public async Task FailedPreviousPeriodDoesNotLeaveCurrentPartiallySaved(SpiralAbyssCollectionStatus failure)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "failed-previous");
        using var source = new TestSource();
        source.FetchOverride = (period, _) => Task.FromResult(period == SpiralAbyssPeriod.Current
            ? RecentSnapshot(period, source.Time) : new SpiralAbyssSourceResult(failure, ProviderCode: -100));
        var result = await CreateCollector(context, source, target).ExecuteRecentAsync(target.PlayerArchiveId, target.Id, CreatePassport());
        Assert.Equal(failure, result.Status);
        Assert.Equal(-100, result.ProviderCode);
        Assert.Null(result.Commit);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Fact]
    public async Task DuplicatePeriodIdentityCannotBeSavedAsTwoDifferentObservations()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "duplicate-period");
        using var source = new TestSource();
        source.FetchOverride = (_, _) => Task.FromResult(RecentSnapshot(SpiralAbyssPeriod.Current, source.Time));
        var result = await CreateCollector(context, source, target).ExecuteRecentAsync(target.PlayerArchiveId, target.Id, CreatePassport());
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse, result.Status);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Fact]
    public async Task CancellationBetweenRequestsLeavesNoPartialHistory()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "cancel-between-periods");
        using var source = new TestSource();
        using var cancel = new CancellationTokenSource();
        source.FetchOverride = (period, token) =>
        {
            if (period == SpiralAbyssPeriod.Previous) { cancel.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromResult(RecentSnapshot(period, source.Time));
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateCollector(context, source, target)
            .ExecuteRecentAsync(target.PlayerArchiveId, target.Id, CreatePassport(), cancel.Token));
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    private static SpiralAbyssSourceResult RecentSnapshot(SpiralAbyssPeriod period, TestTime time) =>
        new(SpiralAbyssCollectionStatus.Received,
            new(period == SpiralAbyssPeriod.Current ? "901" : "900", null, null,
                [new SpiralAbyssFloorResult(12, DataCompleteness.Partial, [new SpiralAbyssChamberResult(3, 2, OccurrenceTime.Unknown)])], time.Now));
}

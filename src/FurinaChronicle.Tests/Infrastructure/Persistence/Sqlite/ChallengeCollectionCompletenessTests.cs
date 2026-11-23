// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed partial class SpiralAbyssCollectionTests
{
    [Theory]
    [InlineData(DataCompleteness.Partial)]
    [InlineData(DataCompleteness.Unknown)]
    public async Task OfficialRefreshAddsCompleteObservationWithoutRewritingOlderEvidence(DataCompleteness previous)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "legacy-abyss-completeness");
        using var source = new TestSource();
        var passport = CreatePassport();
        var snapshot = (await source.FetchAsync(passport, target.RoleIdentity!.NaturalIdentity,
            SpiralAbyssPeriod.Current, default)).Snapshot!;
        var legacy = new ChallengeObservation(ChallengeObservationId.New(), target.Id, ChallengeMode.SpiralAbyss,
            snapshot.SeasonId, snapshot.PeriodStart, snapshot.PeriodEnd, previous, snapshot.Floors,
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: snapshot.FetchedAt, FetchedAt: snapshot.FetchedAt)),
            spiralAbyssStatistics: snapshot.Statistics);
        Assert.Equal(ChangeExecutionStatus.Applied, (await context.AtomicGacha.CommitChallengeObservationsAsync(
            new(OperationId.New(), DataChangeOperationKind.Refresh, DataOrigin.OfficialApi, "Legacy fixture",
                source.Time.Now, source.Time.Now, [legacy], expectedTarget: target))).Status);
        source.Time.Now = source.Time.Now.AddMinutes(1);
        var collector = CreateCollector(context, source, target);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport)).Commit?.Status);
        await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
        var history = await new SqliteGachaAtomicChangeStore(reopened).GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(previous, Assert.Single(history, item => item.ObservationId == legacy.ObservationId).Completeness);
        Assert.Equal(DataCompleteness.Complete,
            Assert.Single(history, item => item.ObservationId != legacy.ObservationId).Completeness);
    }
}

public sealed partial class ChallengeModeCollectionTests
{
    [Theory]
    [InlineData(ChallengeMode.ImaginariumTheater, DataCompleteness.Partial)]
    [InlineData(ChallengeMode.ImaginariumTheater, DataCompleteness.Unknown)]
    [InlineData(ChallengeMode.StygianOnslaught, DataCompleteness.Partial)]
    [InlineData(ChallengeMode.StygianOnslaught, DataCompleteness.Unknown)]
    public async Task OfficialRefreshAddsCompleteObservationWithoutRewritingOlderEvidence(
        ChallengeMode mode, DataCompleteness previous)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "legacy-challenge-completeness");
        var source = new TestSource();
        var passport = CreatePassport();
        var snapshot = (await source.FetchAsync(mode, passport, target.RoleIdentity!.NaturalIdentity)).Snapshot!;
        var legacy = new ChallengeObservation(ChallengeObservationId.New(), target.Id, mode, snapshot.SeasonId,
            snapshot.PeriodStart, snapshot.PeriodEnd, previous, [],
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: snapshot.FetchedAt, FetchedAt: snapshot.FetchedAt)),
            snapshot.Stages, snapshot.Difficulty, snapshot.MedalCount);
        Assert.Equal(ChangeExecutionStatus.Applied, (await context.AtomicGacha.CommitChallengeObservationsAsync(
            new(OperationId.New(), DataChangeOperationKind.Refresh, DataOrigin.OfficialApi, "Legacy fixture",
                source.Time.Now, source.Time.Now, [legacy], expectedTarget: target))).Status);
        var collector = CreateCollector(context, source, target);
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode)).Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode)).Commit?.Status);
        await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
        var history = await new SqliteGachaAtomicChangeStore(reopened).GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, history.Count);
        Assert.Equal(previous, Assert.Single(history, item => item.ObservationId == legacy.ObservationId).Completeness);
        Assert.Equal(DataCompleteness.Complete,
            Assert.Single(history, item => item.ObservationId != legacy.ObservationId).Completeness);
    }
}

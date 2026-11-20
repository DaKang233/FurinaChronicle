// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed partial class ChallengeModeCollectionTests
{
    [Theory]
    [InlineData(ChallengeMode.ImaginariumTheater)]
    [InlineData(ChallengeMode.StygianOnslaught)]
    public async Task TwoSeasonsAreScopedIdempotentAndCanBeUndoneTogether(ChallengeMode mode)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "two-seasons");
        var other = await AddAccountAsync(context, "independent-copy");
        var source = new TestSource { IncludePrevious = true };
        var collect = CreateCollector(context, source, target);
        var passport = CreatePassport();
        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode);
        Assert.Equal(ChangeExecutionStatus.Applied, result.Commit?.Status);
        Assert.Equal(2, result.Commit!.AffectedRecordCount);
        var stored = await context.AtomicGacha.GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, stored.Count);
        Assert.Equal(2, stored.Select(item => item.SeasonId).Distinct().Count());
        Assert.Single(stored.Select(item => item.Provenance.AcquisitionBatchId).Distinct());
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(other.Id));
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode)).Commit?.Status);
        var undo = await ((IArchiveOperationHistoryStore)context.AtomicGacha).UndoLatestAsync(new ArchiveUndoRequest(
            OperationId.New(), target.PlayerArchiveId, source.Time.Now, source.Time.Now));
        Assert.Equal(ChangeExecutionStatus.Applied, undo.Status);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }

    [Theory]
    [InlineData(ChallengeMode.ImaginariumTheater)]
    [InlineData(ChallengeMode.StygianOnslaught)]
    public async Task ChangeInPreviousSeasonDoesNotAppendUnchangedCurrentSeason(ChallengeMode mode)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "previous-change");
        var source = new TestSource { IncludePrevious = true };
        var collect = CreateCollector(context, source, target);
        var passport = CreatePassport();
        await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode);
        source.PreviousDifficulty = 3;
        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, mode);
        Assert.Equal(ChangeExecutionStatus.Applied, result.Commit?.Status);
        Assert.Equal(1, result.Commit!.AffectedRecordCount);
        var stored = await context.AtomicGacha.GetChallengeObservationsAsync(target.Id);
        Assert.Equal(3, stored.Count);
        Assert.Single(stored, item => item.SeasonId != "previous-season");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateSeasonOrUnexpectedModeIsRejectedBeforeWriting(bool duplicate)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "invalid-second");
        var source = new TestSource { IncludePrevious = true, DuplicateSeason = duplicate,
            PreviousMode = duplicate ? null : ChallengeMode.StygianOnslaught };
        var collect = CreateCollector(context, source, target);
        var result = await collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport(), ChallengeMode.ImaginariumTheater);
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse, result.Status);
        Assert.Null(result.Commit);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
    }
    [Theory]
    [InlineData(ChallengeMode.ImaginariumTheater)]
    [InlineData(ChallengeMode.StygianOnslaught)]
    public async Task FailureDuringSecondInsertRollsBackFactsRevisionsAndChangeSet(ChallengeMode mode)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "second-insert-failure");
        using (var raw = context.OpenRawConnection())
            raw.Execute("""
                CREATE TRIGGER FailSecondChallenge BEFORE INSERT ON ArchiveDomainFacts
                WHEN (SELECT COUNT(*) FROM ArchiveDomainFacts) = 1
                BEGIN SELECT RAISE(ABORT, 'synthetic second challenge failure'); END;
                """);
        var source = new TestSource { IncludePrevious = true };
        var collect = CreateCollector(context, source, target);
        await Assert.ThrowsAsync<SQLite.SQLiteException>(() =>
            collect.ExecuteAsync(target.PlayerArchiveId, target.Id, CreatePassport(), mode));
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(target.Id));
        using var check = context.OpenRawConnection();
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM ArchiveDomainRevisions"));
        Assert.Equal(0, check.ExecuteScalar<int>("SELECT COUNT(*) FROM DataChangeSets"));
        Assert.Equal("ok", check.ExecuteScalar<string>("PRAGMA quick_check"));
    }

}

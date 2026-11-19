// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    [Theory]
    [InlineData(ChallengeMode.SpiralAbyss, false)]
    [InlineData(ChallengeMode.SpiralAbyss, true)]
    [InlineData(ChallengeMode.ImaginariumTheater, false)]
    [InlineData(ChallengeMode.ImaginariumTheater, true)]
    [InlineData(ChallengeMode.StygianOnslaught, false)]
    [InlineData(ChallengeMode.StygianOnslaught, true)]
    public async Task RestoreRetainsAllChallengeDetails(ChallengeMode mode, bool replace)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            var archive = SqliteRepositoryTestContext.CreateArchive("Main", TargetArchiveId);
            await context.Archives.AddAsync(archive);
            await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
                archive.Id, Role.NaturalIdentity.Uid, GameServerRegion.ChinaOfficial,
                "Traveler", TargetAccountId, roleIdentity: Role));
            var source = SourceData("Main", []);
            DateTimeOffset finish = DateTimeOffset.Parse("2026-10-03T15:16:17+08:00");
            var expected = new ChallengeObservation(
                ChallengeObservationId.New(), SourceAccountId, mode, "31", null, null,
                DataCompleteness.Partial, [],
                new RecordProvenance(DataOrigin.OfficialApi,
                    new RecordTimestamps(ObservedAt: finish.AddDays(1), FetchedAt: finish.AddDays(1))),
                mode == ChallengeMode.SpiralAbyss ? [] :
                [new ChallengeStageResult("1", mode == ChallengeMode.StygianOnslaught ? 123 : 1,
                    mode == ChallengeMode.StygianOnslaught ? OccurrenceTime.Unknown
                        : OccurrenceTime.At(finish, TimePrecision.Second),
                    [new ChallengeParticipant("10000001", "合成角色", null, 90, 5,
                        mode == ChallengeMode.ImaginariumTheater ? ChallengeParticipantOrigin.Trial : null)],
                    ["合成增益"], [new ChallengeEnemy("合成敌人", 100, null)],
                    mode == ChallengeMode.ImaginariumTheater ? new TheaterRoundDetails(true, 1) : null)],
                4, mode == ChallengeMode.ImaginariumTheater ? 8 : null,
                mode == ChallengeMode.SpiralAbyss ? new SpiralAbyssStatistics(17, 12, "12-3", 34,
                    [new SpiralAbyssRankEntry("10000089", 12, null, 5)],
                    [new SpiralAbyssRankEntry("10000089", 27, null, 5)],
                    [new SpiralAbyssRankEntry("10000089", 3_000_000_000, null, 5)],
                    [new SpiralAbyssRankEntry("10000089", 999, null, 5)],
                    [new SpiralAbyssRankEntry("10000089", 24, null, 5)],
                    [new SpiralAbyssRankEntry("10000089", 8, null, 5)]) : null,
                mode == ChallengeMode.ImaginariumTheater ? FurinaChronicle.Tests.TestDoubles.TheaterDetailFixture.Statistics() : null);
            source.Challenges[SourceAccountId] = [expected];

            using var archiveFile = new MemoryStream();
            await new ArchivePackageWriter().WriteAsync(archiveFile, source,
                new ArchiveGeneratorInfo("FurinaChronicle", "0.10.0", "10.0", "fixture", "test"));
            archiveFile.Position = 0;
            await using var staged = await new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(), new ArchiveRestoreStorageOptions(root)).StageAsync(archiveFile);
            var descriptor = Assert.Single(staged.Summary.Manifest.Datasets, dataset => dataset.Kind == "challenge-history");
            Assert.Equal(mode == ChallengeMode.ImaginariumTheater ? "1.2" : mode == ChallengeMode.SpiralAbyss ? "1.1" : "1.0", descriptor.FormatVersion);
            Assert.Equal(mode != ChallengeMode.StygianOnslaught, descriptor.Required);
            var planner = new PlanArchiveRestore(CreateTargetFactory(context, root), TimeProvider.System);
            var planRequest = new ArchiveRestorePlanRequest(staged,
                Mode: replace ? ArchiveRestoreMode.Replace : ArchiveRestoreMode.Merge,
                ReplaceSelections: replace
                    ? [new ArchiveReplaceSelection(SourceArchiveId, ArchiveReplaceScopeKind.Domain,
                        SourceAccountId, ArchiveReplaceDomain.ChallengeObservation)]
                    : null);
            var warning = await planner.PlanAsync(planRequest);
            var plan = await planner.PlanAsync(planRequest with
            {
                Confirmations = warning.Confirmations.Where(item => !item.Satisfied)
                    .Select(item => new ArchiveRestoreConfirmation(item.Kind, item.Reference,
                        item.MarkerKey, item.MarkerVersion)).ToArray(),
            });
            Assert.True(plan.CanApply);
            var applied = await context.AtomicGacha.ApplyAsync(
                new ArchiveRestoreApplyRequest(staged, plan, OperationId.New(), finish, finish));
            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            var actual = Assert.Single(
                await context.AtomicGacha.GetChallengeObservationsAsync(TargetAccountId));
            Assert.Equal(TargetAccountId, actual.GameAccountId);
            Assert.Equal(expected.Difficulty, actual.Difficulty);
            Assert.Equal(expected.MedalCount, actual.MedalCount);
            if (mode == ChallengeMode.SpiralAbyss)
            {
                var statistics = Assert.IsType<SpiralAbyssStatistics>(actual.SpiralAbyssStatistics);
                Assert.Equal(34, statistics.TotalStars);
                Assert.Equal(3_000_000_000, Assert.Single(statistics.DamageRank).Value);
            }
            else
            {
                var actualStage = Assert.Single(actual.Stages);
                Assert.Equal(expected.Stages[0].Score, actualStage.Score);
                Assert.Equal(expected.Stages[0].CompletedAt, actualStage.CompletedAt);
                Assert.Equal("合成角色", Assert.Single(actualStage.Participants).Name);
                Assert.Equal("合成增益", Assert.Single(actualStage.Buffs));
                Assert.Equal("合成敌人", Assert.Single(actualStage.Enemies).Name);
            }
            if (mode == ChallengeMode.ImaginariumTheater)
            {
                Assert.Equal(90761, actual.TheaterStatistics!.TotalUseTime);
                Assert.Equal("3000000000", actual.TheaterStatistics.MaxDamage!.Value);
                Assert.Equal(ChallengeParticipantOrigin.Trial, actual.Stages[0].Participants[0].Origin);
                Assert.True(actual.Stages[0].TheaterRound!.IsTarot);
            }
            Assert.Equal(ArchiveRestoreSemanticDigest.ForChallenge(expected),
                ArchiveRestoreSemanticDigest.ForChallenge(actual));

            var repeated = await PlanForTargetAsync(context, root, source, confirmNonUndoable: true);
            Assert.True(repeated.CanApply);
            Assert.All(repeated.Facts, fact => Assert.Equal(ArchiveRestoreFactAction.Skip, fact.Action));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }
}

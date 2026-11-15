// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class ChallengeModeCollectionTests
{
    [Fact]
    public async Task TheaterCollectionStoresStagesAndRepeatsAreNoOp()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "theater");
        var source = new TestSource();
        CollectChallengeMode collect = CreateCollector(context, source, target);
        PassportAccount passport = CreatePassport();

        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, passport,
                ChallengeMode.ImaginariumTheater)).Commit?.Status);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, passport,
                ChallengeMode.ImaginariumTheater)).Commit?.Status);

        await using var reopened = new FurinaDatabase(
            new SqliteDatabaseOptions(context.DatabasePath));
        var observations = await new SqliteGachaAtomicChangeStore(reopened)
            .GetChallengeObservationsAsync(
                target.Id, ChallengeMode.ImaginariumTheater);
        ChallengeObservation observation = Assert.Single(observations);
        Assert.Equal("37", observation.SeasonId);
        Assert.Empty(observation.SpiralAbyssFloors);
        Assert.Equal(2, observation.Stages.Count);
        Assert.Equal(1, observation.Stages[0].Score);
        Assert.Equal(4, observation.Difficulty);
        Assert.Equal(9, observation.MedalCount);
        ChallengeStageResult detailed = observation.Stages[0];
        Assert.Equal("10000093", Assert.Single(detailed.Participants).Id);
        Assert.Equal("闲云", Assert.Single(detailed.Participants).Name);
        Assert.Equal(["元素战技强化"], detailed.Buffs);
        Assert.Equal("深渊法师", Assert.Single(detailed.Enemies).Name);
        Assert.Equal(DataCompleteness.Partial, observation.Completeness);
        Assert.Equal(DataOrigin.OfficialApi, observation.Provenance.Origin);
        Assert.Equal("miyoushe", observation.Provenance.Source?.Provider);
    }

    [Fact]
    public async Task StygianCollectionStoresMonsterSeconds()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "stygian");
        var source = new TestSource();
        CollectChallengeMode collect = CreateCollector(context, source, target);

        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, CreatePassport(),
                ChallengeMode.StygianOnslaught)).Commit?.Status);

        var observation = Assert.Single(
            await context.AtomicGacha.GetChallengeObservationsAsync(
                target.Id, ChallengeMode.StygianOnslaught));
        Assert.Equal("5", observation.SeasonId);
        Assert.Equal(6, observation.Difficulty);
        Assert.Null(observation.MedalCount);
        Assert.Equal(
            ["怪物A", "怪物B"],
            observation.Stages.Select(stage => stage.StageId));
        Assert.Equal([60, 90], observation.Stages.Select(stage => stage.Score));
        Assert.All(
            observation.Stages,
            stage => Assert.Equal(OccurrenceTime.Unknown, stage.CompletedAt));
    }

    [Fact]
    public async Task RoleMismatchStopsBeforeRequest()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "mismatch");
        var source = new TestSource();
        CollectChallengeMode collect = CreateCollector(
            context, source, target, region: "cn_qd01");

        Assert.Equal(
            SpiralAbyssCollectionStatus.RoleNotBound,
            (await collect.ExecuteAsync(
                target.PlayerArchiveId, target.Id, CreatePassport(),
                ChallengeMode.ImaginariumTheater)).Status);
        Assert.Equal(0, source.RequestCount);
    }

    [Fact]
    public async Task IsolatedValidationWritesOnlyTheTemporaryStore()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount target = await AddAccountAsync(context, "validation");
        var source = new TestSource();
        var validation = new ValidateChallengeModeCollection(
            new TestProfile(new PassportGameRole(
                "hk4e_cn", "cn_gf01", target.Uid, "fixture", 60, "fixture")),
            source,
            source.Time);

        ChallengeModeValidationResult result = await validation.ExecuteAsync(
            target, CreatePassport(), ChallengeMode.ImaginariumTheater);

        Assert.Equal(ChangeExecutionStatus.Applied, result.First.Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.NoOp, result.Repeated?.Commit?.Status);
        Assert.Equal("37", Assert.Single(result.ReopenedObservations).SeasonId);
        Assert.Empty(await context.AtomicGacha.GetChallengeObservationsAsync(
            target.Id));
    }

    private static CollectChallengeMode CreateCollector(
        SqliteRepositoryTestContext context,
        TestSource source,
        GameAccount target,
        string region = "cn_gf01") =>
        new(
            context.Accounts,
            new TestProfile(new PassportGameRole(
                "hk4e_cn", region, target.Uid, "fixture", 60, "fixture")),
            source,
            context.AtomicGacha,
            source.Time);

    private static async Task<GameAccount> AddAccountAsync(
        SqliteRepositoryTestContext context,
        string name)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive(name);
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id, "100000001", GameServerRegion.ChinaOfficial);
        await context.Accounts.AddAsync(account);
        return account;
    }

    private static PassportAccount CreatePassport() =>
        new(
            Guid.NewGuid(), "fixture", null, null,
            PassportLoginMethod.ManualCookie,
            new PassportCredentials(
                null, "fixture", "fixture", null, null, null, null),
            PassportDeviceIdentity.Create(),
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

    private sealed class TestProfile(PassportGameRole role)
        : IMiHoYoAccountProfileClient
    {
        public Task<PassportAccountProfile> GetAsync(
            PassportAccount account,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PassportAccountProfile("fixture", null, [role]));
    }

    private sealed class TestSource : IChallengeModeSource
    {
        public TestTime Time { get; } = new();
        public int RequestCount { get; private set; }

        public Task<ChallengeModeSourceResult> FetchAsync(
            ChallengeMode mode,
            PassportAccount passport,
            GameRoleNaturalIdentity role,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            Time.Now = Time.Now.AddMinutes(1);
            ChallengeModeSnapshot snapshot = mode switch
            {
                ChallengeMode.ImaginariumTheater => new(
                    mode, "37",
                    DateTimeOffset.FromUnixTimeSeconds(1794000000),
                    DateTimeOffset.FromUnixTimeSeconds(1795000000),
                    DataCompleteness.Partial,
                    [
                        new ChallengeStageResult(
                            "1", 1,
                            OccurrenceTime.At(
                                DateTimeOffset.FromUnixTimeSeconds(1794100000),
                                TimePrecision.Second),
                            [new ChallengeParticipant(
                                "10000093", "闲云",
                                "https://example.invalid/avatar.png", 90, 5)],
                            ["元素战技强化"],
                            [new ChallengeEnemy(
                                "深渊法师", 90,
                                "https://example.invalid/enemy.png")]),
                        new ChallengeStageResult("2", 0, OccurrenceTime.Unknown),
                    ],
                    4, 9, Time.Now),
                ChallengeMode.StygianOnslaught => new(
                    mode, "5",
                    DateTimeOffset.FromUnixTimeSeconds(1794000000),
                    DateTimeOffset.FromUnixTimeSeconds(1795000000),
                    DataCompleteness.Partial,
                    [
                        new ChallengeStageResult("怪物A", 60, OccurrenceTime.Unknown),
                        new ChallengeStageResult("怪物B", 90, OccurrenceTime.Unknown),
                    ],
                    6, null, Time.Now),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            return Task.FromResult(new ChallengeModeSourceResult(
                SpiralAbyssCollectionStatus.Received, snapshot, HttpStatus: 200));
        }
    }

    private sealed class TestTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } =
            DateTimeOffset.FromUnixTimeSeconds(1794100000);

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

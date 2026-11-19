// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed partial class ChallengeModeCollectionTests
{
    [Fact]
    public async Task StructuredBuffChangesAppendObservationsButNewFetchTimeAloneDoesNot()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "theater-buffs");
        var source = new TestSource { OverrideSnapshot = (await TheaterBuffFixture.ReadAsync()).Snapshot! };
        var collector = CreateCollector(context, source, target);
        var passport = CreatePassport();
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.ImaginariumTheater)).Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.ImaginariumTheater)).Commit?.Status);
        var json = JsonNode.Parse(TheaterBuffFixture.Response)!;
        json["data"]!["data"]![0]!["detail"]!["rounds_data"]![0]!["choice_cards"]![0]!["is_enhanced"] = false;
        source.OverrideSnapshot = (await TheaterBuffFixture.ReadAsync(json.ToJsonString())).Snapshot!;
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.ImaginariumTheater)).Commit?.Status);
        await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
        var observations = await new SqliteGachaAtomicChangeStore(reopened).GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, observations.Count);
        Assert.Contains(observations, observation => observation.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffDetails![0].IsEnhanced == true);
        Assert.Contains(observations, observation => observation.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffDetails![0].IsEnhanced == false);
        Assert.All(observations, observation =>
        {
            Assert.Equal(3, observation.Stages.Single(stage => stage.StageId == "10").TheaterRound!.BuffDetails!.Count);
            Assert.Equal(TimePrecision.Second, observation.Stages.Single(stage => stage.StageId == "10").CompletedAt.Precision);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794100010), observation.Stages.Single(stage => stage.StageId == "10").CompletedAt.Start);
        });
    }
}

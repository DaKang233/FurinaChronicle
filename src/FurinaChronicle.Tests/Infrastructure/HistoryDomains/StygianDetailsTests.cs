// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class StygianDetailsTests
{
    [Fact]
    public async Task ReadsBothRunsConstellationsMonsterDetailsAndLargeMetricsWithoutInventingTime()
    {
        var result = await StygianDetailFixture.ReadAsync();
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        var snapshot = result.Snapshot!;
        Assert.Equal(180, snapshot.StygianStatistics!.SinglePlayer!.Seconds);
        Assert.Equal(240, snapshot.StygianStatistics.Multiplayer!.Seconds);
        Assert.Equal(6, snapshot.Difficulty);
        var single = Assert.Single(snapshot.Stages);
        var multiplayer = Assert.Single(snapshot.MultiplayerStages!);
        Assert.Equal(single.StageId, multiplayer.StageId); // Separate run scopes; no accidental merge.
        Assert.Equal(75, single.Score);
        Assert.Equal(200, multiplayer.Score);
        Assert.Equal(new int?[] { 6, 0 }, single.Participants.Select(avatar => avatar.Constellation));
        Assert.Equal(2, multiplayer.Participants[0].Constellation);
        Assert.Equal(OccurrenceTime.Unknown, single.CompletedAt);
        Assert.Equal(OccurrenceTime.Unknown, multiplayer.CompletedAt);
        Assert.Equal("123456", single.StygianDetails!.MonsterId);
        Assert.Equal(2, single.StygianDetails.MonsterTags.Count);
        Assert.Equal(6_000_000_000, single.StygianDetails.BestAvatars[1].Value);
        Assert.Equal(9_000_000_000, multiplayer.StygianDetails!.BestAvatars[0].Value);
        Assert.Equal(new bool?[] { true, false, null }, snapshot.StygianStatistics.BlingAvatars.Select(avatar => avatar.IsPlus));
        Assert.Equal(DataCompleteness.Partial, snapshot.Completeness);
    }

    [Fact]
    public async Task MultiplayerOnlyAndEmptyOptionalBranchRemainUsableWithoutInventingSingleResults()
    {
        var root = JsonNode.Parse(StygianDetailFixture.Response)!;
        var entry = root["data"]!["data"]![0]!;
        entry["single"] = new JsonObject { ["has_data"] = false, ["best"] = new JsonObject { ["difficulty"] = 0, ["second"] = 0 } };
        var snapshot = (await StygianDetailFixture.ReadAsync(root.ToJsonString())).Snapshot!;
        Assert.Empty(snapshot.Stages);
        Assert.Null(snapshot.Difficulty);
        Assert.False(snapshot.StygianStatistics!.SinglePlayer!.HasData);
        Assert.Null(snapshot.StygianStatistics.SinglePlayer.Seconds);
        Assert.Single(snapshot.MultiplayerStages!);
        root = JsonNode.Parse(StygianDetailFixture.Response)!;
        root["data"]!["data"]![0]!["mp"] = new JsonObject();
        var emptyBranch = (await StygianDetailFixture.ReadAsync(root.ToJsonString())).Snapshot!;
        Assert.Null(emptyBranch.StygianStatistics!.Multiplayer);
        Assert.Empty(emptyBranch.MultiplayerStages!);
    }

    [Fact]
    public async Task MissingCountersAndPerformanceValuesRemainUnknownWhileUnrecognizedCodesAreRetained()
    {
        var root = JsonNode.Parse(StygianDetailFixture.Response)!;
        var entry = root["data"]!["data"]![0]!;
        entry["single"]!["best"] = new JsonObject();
        var metric = entry["single"]!["challenge"]![0]!["best_avatar"]![0]!.AsObject();
        metric.Remove("dps");
        metric["type"] = 99;
        var snapshot = (await StygianDetailFixture.ReadAsync(root.ToJsonString())).Snapshot!;
        Assert.Null(snapshot.Difficulty);
        Assert.Null(snapshot.StygianStatistics!.SinglePlayer!.Seconds);
        Assert.Null(snapshot.Stages[0].StygianDetails!.BestAvatars[0].Value);
        Assert.Equal(99, snapshot.Stages[0].StygianDetails!.BestAvatars[0].Type);
    }

    [Theory]
    [InlineData("seconds")]
    [InlineData("constellation")]
    [InlineData("metric")]
    [InlineData("monster_id")]
    [InlineData("tag")]
    [InlineData("bling")]
    [InlineData("missing_flag")]
    public async Task InvalidDetailsAreRejectedBeforeCollection(string invalid)
    {
        var root = JsonNode.Parse(StygianDetailFixture.Response)!;
        var entry = root["data"]!["data"]![0]!;
        var stage = entry["single"]!["challenge"]![0]!;
        switch (invalid)
        {
            case "seconds": entry["mp"]!["best"]!["second"] = -1; break;
            case "constellation": stage["teams"]![0]!["rank"] = 7; break;
            case "metric": stage["best_avatar"]![0]!["dps"] = -1; break;
            case "monster_id": stage["monster"]!["monster_id"] = -1; break;
            case "tag": stage["monster"]!["tags"]![0] = null; break;
            case "bling": entry["blings"]![0] = null; break;
            case "missing_flag": entry["mp"]!.AsObject().Remove("has_data"); break;
        }
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse,
            (await StygianDetailFixture.ReadAsync(root.ToJsonString())).Status);
    }
}

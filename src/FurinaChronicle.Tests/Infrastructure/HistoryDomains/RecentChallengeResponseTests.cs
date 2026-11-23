// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class RecentChallengeResponseTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BothSeasonsRemainSeparateEvenIfResponseOrderIsReversed(bool stygian)
    {
        var root = Body(stygian);
        var entries = root["data"]!["data"]!.AsArray();
        var previous = entries[0]!.DeepClone();
        previous["schedule"]!["schedule_id"] = 900001;
        entries.Insert(0, previous);
        var result = await Read(root, stygian);
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(0, result.ProviderCode);
        Assert.Equal(200, result.HttpStatus);
        Assert.Equal("900001", result.Snapshot!.SeasonId);
        var second = Assert.Single(result.AdditionalSnapshots);
        Assert.NotEqual(second.SeasonId, result.Snapshot.SeasonId);
        Assert.Equal(second.FetchedAt, result.Snapshot.FetchedAt);
        Assert.Equal(DataCompleteness.Complete, result.Snapshot.Completeness);
        Assert.Equal(DataCompleteness.Complete, second.Completeness);
        if (stygian)
        {
            Assert.Equal(result.Snapshot.Stages[0].StygianDetails!.MonsterDescriptions,
                second.Stages[0].StygianDetails!.MonsterDescriptions);
            Assert.Single(second.MultiplayerStages!);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyCurrentSeasonDoesNotHidePreviousData(bool stygian)
    {
        var root = Body(stygian);
        var entries = root["data"]!["data"]!.AsArray();
        var current = entries[0]!.DeepClone();
        current["schedule"]!["schedule_id"] = 900001;
        if (stygian)
        {
            current["single"] = new JsonObject { ["has_data"] = false };
            current["mp"] = new JsonObject { ["has_data"] = false };
            current["blings"] = new JsonArray();
        }
        else current["has_data"] = false;
        entries.Insert(0, current);
        var result = await Read(root, stygian);
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.NotEqual("900001", result.Snapshot!.SeasonId);
        Assert.Empty(result.AdditionalSnapshots);
    }

    [Theory]
    [InlineData(false, "duplicate")]
    [InlineData(true, "duplicate")]
    [InlineData(false, "invalid")]
    [InlineData(true, "invalid")]
    [InlineData(false, "too_many")]
    [InlineData(true, "too_many")]
    public async Task InvalidSecondSeasonDoesNotExposePartialSuccess(bool stygian, string defect)
    {
        var root = Body(stygian);
        var entries = root["data"]!["data"]!.AsArray();
        var previous = entries[0]!.DeepClone();
        if (defect != "duplicate") previous["schedule"]!["schedule_id"] = defect == "invalid" ? 0 : 900001;
        entries.Add(previous);
        if (defect == "too_many")
        {
            var third = previous.DeepClone(); third["schedule"]!["schedule_id"] = 900002; entries.Add(third);
        }
        var result = await Read(root, stygian);
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Empty(result.AdditionalSnapshots);
        Assert.Equal(0, result.ProviderCode);
    }

    private static JsonNode Body(bool stygian) => JsonNode.Parse(stygian ? StygianDetailFixture.Response : TheaterDetailFixture.Response)!;
    private static async Task<ChallengeModeSourceResult> Read(JsonNode root, bool stygian)
    {
        using var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(root.ToJsonString()) };
        return stygian ? await new MiYouSheHardChallengeResponseReader(TimeProvider.System).ReadAsync(message)
            : await new MiYouSheRoleCombatResponseReader(TimeProvider.System).ReadAsync(message);
    }
}

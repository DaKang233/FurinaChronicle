// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed class TheaterBuffSemanticDigestTests
{
    [Theory]
    [InlineData("id")]
    [InlineData("icon")]
    [InlineData("is_enhanced")]
    [InlineData("desc")]
    [InlineData("level")]
    [InlineData("effects")]
    [InlineData("summary")]
    [InlineData("kind")]
    public async Task EachSourceDetailChangesContentDigestWhileIdentityAndTimesStayFixed(string field)
    {
        var snapshot = (await TheaterBuffFixture.ReadAsync()).Snapshot!;
        var json = JsonNode.Parse(TheaterBuffFixture.Response)!;
        var round = json["data"]!["data"]![0]!["detail"]!["rounds_data"]![0]!;
        switch (field)
        {
            case "id": round["choice_cards"]![0]!["id"] = 103; break;
            case "icon": round["choice_cards"]![0]!["icon"] = "https://act-webstatic.mihoyo.com/changed.png"; break;
            case "is_enhanced": round["choice_cards"]![0]!["is_enhanced"] = false; break;
            case "desc": round["choice_cards"]![0]!["desc"] = "改变的说明"; break;
            case "level": round["splendour_buff"]!["buffs"]![0]!["level"] = 4; break;
            case "effects": round["splendour_buff"]!["buffs"]![0]!["level_effect"]![1]!["desc"] = "改变的效果"; break;
            case "summary": round["splendour_buff"]!["summary"]!["desc"] = "改变的总览"; break;
            case "kind":
                var card = round["choice_cards"]![0]!.DeepClone();
                round["choice_cards"] = new JsonArray();
                round["buffs"]!.AsArray().Insert(0, card);
                break;
        }
        var changed = (await TheaterBuffFixture.ReadAsync(json.ToJsonString())).Snapshot! with { FetchedAt = snapshot.FetchedAt };
        Guid account = Guid.NewGuid();
        var id = ChallengeObservationId.New();
        var before = TheaterBuffFixture.Observation(account, snapshot, id);
        var after = TheaterBuffFixture.Observation(account, changed, id);
        Assert.Equal(before.ObservationId, after.ObservationId);
        Assert.Equal(before.Provenance, after.Provenance);
        Assert.Equal(before.Stages.Single(stage => stage.StageId == "10").CompletedAt,
            after.Stages.Single(stage => stage.StageId == "10").CompletedAt);
        Assert.NotEqual(ArchiveRestoreSemanticDigest.ForChallenge(before), ArchiveRestoreSemanticDigest.ForChallenge(after));
    }
}

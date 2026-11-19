// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class TheaterBuffDetailsTests
{
    [Fact]
    public async Task RetainsDuplicateNamesKindsIdsIconsEnhancementAndOrderedEffects()
    {
        var snapshot = (await TheaterBuffFixture.ReadAsync()).Snapshot!;
        var round = snapshot.Stages[0].TheaterRound!;
        Assert.Equal("合成祝福总览", round.BuffSummary);
        Assert.Equal(3, round.BuffDetails!.Count);
        Assert.Equal(new[] { TheaterBuffKind.ChoiceCard, TheaterBuffKind.Buff, TheaterBuffKind.Splendour },
            round.BuffDetails.Select(value => value.Kind));
        Assert.Equal(new long?[] { 101, 102, null }, round.BuffDetails.Select(value => value.SourceId));
        Assert.True(round.BuffDetails[0].IsEnhanced);
        Assert.False(round.BuffDetails[1].IsEnhanced);
        Assert.Null(round.BuffDetails[2].IsEnhanced);
        Assert.Equal("选择效果\n第二行", round.BuffDetails[0].Description);
        Assert.Equal(new[] { "效果甲\n后续", "效果乙" }, round.BuffDetails[2].Effects);
        Assert.Equal(3, round.BuffDetails[2].Level);
        Assert.All(round.BuffDetails, value => Assert.NotNull(value.IconUrl));
        Assert.Equal(TimePrecision.Second, snapshot.Stages[0].CompletedAt.Precision);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794100010), snapshot.Stages[0].CompletedAt.Start);
        Assert.Equal(4, snapshot.Stages[0].Buffs.Count);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("icon")]
    [InlineData("is_enhanced")]
    public async Task MissingSourceFieldsRemainUnknown(string property)
    {
        var json = JsonNode.Parse(TheaterBuffFixture.Response)!;
        var card = json["data"]!["data"]![0]!["detail"]!["rounds_data"]![0]!["choice_cards"]![0]!.AsObject();
        card.Remove(property);
        var value = (await TheaterBuffFixture.ReadAsync(json.ToJsonString())).Snapshot!.Stages[0].TheaterRound!.BuffDetails![0];
        if (property == "id") Assert.Null(value.SourceId);
        if (property == "icon") Assert.Null(value.IconUrl);
        if (property == "is_enhanced") Assert.Null(value.IsEnhanced);
    }

    [Fact]
    public void MissingExtensionPreservesOldRoundJsonAndEmptyInputIsNotNewEvidence()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var old = """{"isTarot":false,"tarotSerialNumber":null}""";
        Assert.Equal(old, JsonSerializer.Serialize(new TheaterRoundDetails(false, null), options));
        Assert.Equal(old, JsonSerializer.Serialize(new TheaterRoundDetails(false, null, []), options));
        Assert.Null(JsonSerializer.Deserialize<TheaterRoundDetails>(old, options)!.BuffDetails);
    }

    [Theory]
    [InlineData("id", -1)]
    [InlineData("level", -1)]
    public async Task InvalidNumbersFailBeforeAnyWrite(string property, int value)
    {
        var json = JsonNode.Parse(TheaterBuffFixture.Response)!;
        var round = json["data"]!["data"]![0]!["detail"]!["rounds_data"]![0]!;
        if (property == "id") round["choice_cards"]![0]![property] = value;
        else round["splendour_buff"]!["buffs"]![0]![property] = value;
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse,
            (await TheaterBuffFixture.ReadAsync(json.ToJsonString())).Status);
    }
}

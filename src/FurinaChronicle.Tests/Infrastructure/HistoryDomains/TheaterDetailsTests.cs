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

public sealed class TheaterDetailsTests
{
    [Fact]
    public async Task ReadsSourceCountersBackupOriginsAndExactRoundTimeWithoutTruncatingValues()
    {
        var snapshot = (await Read(TheaterDetailFixture.Response)).Snapshot!;
        Assert.Equal(4, snapshot.Stages.Count);
        Assert.Equal(ChallengeParticipantOrigin.Support, snapshot.Stages[0].Participants[0].Origin);
        Assert.Equal(TimePrecision.Second, snapshot.Stages[0].CompletedAt.Precision);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794100010), snapshot.Stages[0].CompletedAt.Start);
        Assert.True(snapshot.Stages[3].TheaterRound!.IsTarot);
        Assert.Equal(1, snapshot.Stages[3].TheaterRound!.TarotSerialNumber);
        var stats = Assert.IsType<TheaterStatistics>(snapshot.TheaterStatistics);
        Assert.Equal(10, stats.MaxRoundId);
        Assert.Equal(1250, stats.CoinCount);
        Assert.Equal(2, stats.AvatarBonusCount);
        Assert.Equal(3, stats.RentCount);
        Assert.Equal(90761, stats.TotalUseTime);
        Assert.Equal(new[] { 1, 2, 10 }, stats.MedalRounds);
        Assert.Equal(new[] { ChallengeParticipantOrigin.Owned, ChallengeParticipantOrigin.Trial, ChallengeParticipantOrigin.Support },
            stats.BackupAvatars.Select(avatar => avatar.Origin!.Value));
        Assert.Equal("3000000000", stats.MaxDamage!.Value);
        Assert.Equal("0", stats.MaxTakeDamage!.Value);
        Assert.Equal("1250", stats.TotalCoinsConsumed);
        Assert.Equal("27", Assert.Single(stats.ShortestAvatars).Value);
    }

    [Theory]
    [InlineData("\"avatar_type\":3", "\"avatar_type\":99")]
    [InlineData("\"coin_num\":1250", "\"coin_num\":-1")]
    [InlineData("\"tarot_serial_no\":1", "\"tarot_serial_no\":-1")]
    [InlineData("\"backup_avatars\":[", "\"backup_avatars\":[null,")]
    [InlineData("\"value\":3000000000", "\"value\":[]")]
    public async Task RejectsInvalidExtendedFieldsBeforeWriting(string before, string after)
    {
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse,
            (await Read(TheaterDetailFixture.Response.Replace(before, after, StringComparison.Ordinal))).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(null)]
    public async Task NonTarotMinusOneSerialIsNotApplicableAndDoesNotDiscardTheObservation(bool? isTarot)
    {
        string ordinary = isTarot is null ? "\"tarot_serial_no\":-1" : "\"is_tarot\":false,\"tarot_serial_no\":-1";
        string response = TheaterDetailFixture.Response.Replace("\"is_tarot\":false", ordinary, StringComparison.Ordinal);
        var result = await Read(response);
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(200, result.HttpStatus); Assert.Equal(0, result.ProviderCode);
        Assert.Equal(4, result.Snapshot!.Stages.Count);
        Assert.All(result.Snapshot.Stages.Where(stage => stage.TheaterRound?.IsTarot != true), stage => Assert.Null(stage.TheaterRound!.TarotSerialNumber));
        Assert.Equal(1, result.Snapshot.Stages.Single(stage => stage.TheaterRound?.IsTarot == true).TheaterRound!.TarotSerialNumber);
    }

    [Fact]
    public async Task RealNegativeOrdinalStillReportsInvalidResponseWithSafeResponseCodes()
    {
        var result = await Read(TheaterDetailFixture.Response.Replace("\"tarot_serial_no\":1", "\"tarot_serial_no\":-2", StringComparison.Ordinal));
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse, result.Status);
        Assert.Equal(200, result.HttpStatus); Assert.Equal(0, result.ProviderCode);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task MissingValuesAndOriginRemainUnknownAndNumericRepresentationNormalizes()
    {
        string response = TheaterDetailFixture.Response
            .Replace("\"value\":3000000000", "\"value\":\"3000000000\"", StringComparison.Ordinal)
            .Replace("\"avatar_type\":3,", string.Empty, StringComparison.Ordinal)
            .Replace("\"coin_num\":1250,", string.Empty, StringComparison.Ordinal)
            .Replace("\"value\":\"\"", "\"value\":null", StringComparison.Ordinal);
        var snapshot = (await Read(response)).Snapshot!;
        Assert.Null(snapshot.Stages[0].Participants[0].Origin);
        Assert.Null(snapshot.TheaterStatistics!.CoinCount);
        Assert.Null(snapshot.TheaterStatistics.MaxTakeDamage!.Value);
        Assert.Equal("3000000000", snapshot.TheaterStatistics.MaxDamage!.Value);
    }

    [Fact]
    public async Task SharedRoundNumbersKeepOrdinaryAndDistinctTarotChallengesWithStableKeys()
    {
        var root = JsonNode.Parse(TheaterDetailFixture.Response)!;
        var rounds = root["data"]!["data"]![0]!["detail"]!["rounds_data"]!.AsArray();
        rounds[3]!["round_id"] = 10;
        var secondTarot = rounds[3]!.DeepClone();
        secondTarot["tarot_serial_no"] = 2;
        rounds.Insert(0, secondTarot);
        var result = await Read(root.ToJsonString());
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(new[] { "1", "10", "2", "tarot:10:1", "tarot:10:2" },
            result.Snapshot!.Stages.Select(stage => stage.StageId).Order(StringComparer.Ordinal));
        var tarot = result.Snapshot.Stages.Single(stage => stage.StageId == "tarot:10:1");
        Assert.True(tarot.TheaterRound!.IsTarot);
        Assert.Equal(1, tarot.TheaterRound.TarotSerialNumber);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1794100011), tarot.CompletedAt.Start);
        rounds.RemoveAt(1); // Removing the ordinary round must not change either Tarot identity.
        var withoutOrdinary = await Read(root.ToJsonString());
        Assert.Equal(new[] { "tarot:10:1", "tarot:10:2" }, withoutOrdinary.Snapshot!.Stages
            .Where(stage => stage.TheaterRound?.IsTarot == true).Select(stage => stage.StageId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ExactDuplicateTarotKeyIsRejectedRatherThanDroppingAStage()
    {
        var root = JsonNode.Parse(TheaterDetailFixture.Response)!;
        var rounds = root["data"]!["data"]![0]!["detail"]!["rounds_data"]!.AsArray();
        rounds.Add(rounds[3]!.DeepClone());
        var result = await Read(root.ToJsonString());
        Assert.Equal(SpiralAbyssCollectionStatus.InvalidResponse, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(0, result.ProviderCode);
    }

    private static async Task<ChallengeModeSourceResult> Read(string json)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        return await new MiYouSheRoleCombatResponseReader(TimeProvider.System).ReadAsync(response);
    }
}

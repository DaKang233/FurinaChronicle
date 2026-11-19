// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
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

    private static async Task<ChallengeModeSourceResult> Read(string json)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
        return await new MiYouSheRoleCombatResponseReader(TimeProvider.System).ReadAsync(response);
    }
}

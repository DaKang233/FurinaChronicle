// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed partial class ChallengeModeCollectionTests
{
    [Fact]
    public async Task StygianBothRunsSurviveActualCollectorReopenAndMultiplayerOnlyChanges()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var target = await AddAccountAsync(context, "stygian-details");
        var snapshot = (await StygianDetailFixture.ReadAsync()).Snapshot!;
        var source = new TestSource { OverrideSnapshot = snapshot };
        var collector = CreateCollector(context, source, target);
        var passport = CreatePassport();
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.StygianOnslaught)).Commit?.Status);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.StygianOnslaught)).Commit?.Status);
        source.OverrideSnapshot = (await StygianDetailFixture.ReadAsync(
            StygianDetailFixture.Response.Replace("\"second\":240", "\"second\":241", StringComparison.Ordinal))).Snapshot!;
        Assert.Equal(ChangeExecutionStatus.Applied,
            (await collector.ExecuteAsync(target.PlayerArchiveId, target.Id, passport, ChallengeMode.StygianOnslaught)).Commit?.Status);
        await using var reopened = new FurinaDatabase(new SqliteDatabaseOptions(context.DatabasePath));
        var history = await new SqliteGachaAtomicChangeStore(reopened).GetChallengeObservationsAsync(target.Id);
        Assert.Equal(2, history.Count);
        Assert.Contains(history, item => item.StygianStatistics?.Multiplayer?.Seconds == 240);
        Assert.Contains(history, item => item.StygianStatistics?.Multiplayer?.Seconds == 241);
        Assert.All(history, item =>
        {
            Assert.Equal(DataOrigin.OfficialApi, item.Provenance.Origin);
            Assert.Equal(6, item.Stages[0].Participants[0].Constellation);
            Assert.Equal(9_000_000_000, item.MultiplayerStages[0].StygianDetails!.BestAvatars[0].Value);
            Assert.Equal("合成多人标签", item.MultiplayerStages[0].StygianDetails!.MonsterTags[0].Description);
            Assert.All(item.Stages.Concat(item.MultiplayerStages), stage => Assert.Equal(OccurrenceTime.Unknown, stage.CompletedAt));
        });
    }

    [Fact]
    public async Task UnknownConstellationDoesNotAddFieldsToLegacyEmbeddedTheaterParticipants()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var account = await AddAccountAsync(context, "legacy-theater");
        var source = new TestSource { Statistics = TheaterDetailFixture.Statistics() };
        var collector = CreateCollector(context, source, account);
        await collector.ExecuteAsync(account.PlayerArchiveId, account.Id, CreatePassport(), ChallengeMode.ImaginariumTheater);
        string raw = await context.Database.Connection.ExecuteScalarAsync<string>("SELECT SnapshotJson FROM ArchiveDomainFacts LIMIT 1");
        using var json = JsonDocument.Parse(raw);
        var participant = json.RootElement.GetProperty("theaterStatistics").GetProperty("maxDamage").GetProperty("participant");
        Assert.Equal(new[] { "id", "name", "iconUrl", "level", "rarity", "origin" }, participant.EnumerateObject().Select(property => property.Name));
        Assert.Equal(JsonValueKind.Null, participant.GetProperty("origin").ValueKind);
        Assert.DoesNotContain("constellation", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stygianStatistics", raw);
        Assert.DoesNotContain("multiplayerStages", raw);
        Assert.Equal(ChangeExecutionStatus.NoOp,
            (await collector.ExecuteAsync(account.PlayerArchiveId, account.Id, CreatePassport(), ChallengeMode.ImaginariumTheater)).Commit?.Status);
    }
}

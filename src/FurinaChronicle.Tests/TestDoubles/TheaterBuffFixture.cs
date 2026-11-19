// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.TestDoubles;

internal static class TheaterBuffFixture
{
    public static string Response
    {
        get
        {
            var root = JsonNode.Parse(TheaterDetailFixture.Response)!;
            var round = root["data"]!["data"]![0]!["detail"]!["rounds_data"]![0]!;
            round["choice_cards"] = JsonNode.Parse("""
                [{"id":101,"name":"同名合成卡牌","desc":"选择效果\\n第二行","icon":"https://act-webstatic.mihoyo.com/fixture-choice.png","is_enhanced":true}]
                """);
            round["buffs"] = JsonNode.Parse("""
                [{"id":102,"name":"同名合成卡牌","desc":"普通增益","icon":"https://act-webstatic.mihoyo.com/fixture-buff.png","is_enhanced":false}]
                """);
            round["splendour_buff"] = JsonNode.Parse("""
                {"summary":{"desc":"合成祝福总览"},"buffs":[{"name":"合成辉彩祝福","icon":"https://act-webstatic.mihoyo.com/fixture-splendour.png","level":3,"level_effect":[{"desc":"效果甲\\n后续"},{"desc":"效果乙"}]}]}
                """);
            return root.ToJsonString();
        }
    }

    public static async Task<ChallengeModeSourceResult> ReadAsync(string? response = null)
    {
        using var http = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response ?? Response) };
        return await new MiYouSheRoleCombatResponseReader(TimeProvider.System).ReadAsync(http);
    }

    public static ChallengeObservation Observation(Guid accountId, ChallengeModeSnapshot snapshot,
        ChallengeObservationId? observationId = null) => new(
            observationId ?? ChallengeObservationId.New(), accountId, snapshot.Mode, snapshot.SeasonId,
            snapshot.PeriodStart, snapshot.PeriodEnd, snapshot.Completeness, [],
            new RecordProvenance(DataOrigin.OfficialApi, new RecordTimestamps(ObservedAt: snapshot.FetchedAt, FetchedAt: snapshot.FetchedAt)),
            snapshot.Stages, snapshot.Difficulty, snapshot.MedalCount, theaterStatistics: snapshot.TheaterStatistics);
}

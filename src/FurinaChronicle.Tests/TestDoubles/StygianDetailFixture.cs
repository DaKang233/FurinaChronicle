// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Tests.TestDoubles;

internal static class StygianDetailFixture
{
    public const string Response = """
        {"retcode":0,"data":{"data":[{
          "schedule":{"schedule_id":5269007,"start_time":1794000000,"end_time":1795000000},
          "single":{"has_data":true,"best":{"difficulty":6,"second":180},
            "challenge":[{"name":"合成关卡","second":75,
              "teams":[{"avatar_id":10000093,"name":"合成六命角色","level":90,"rarity":5,"rank":6},
                       {"avatar_id":10000089,"name":"合成零命角色","level":95,"rarity":5,"rank":0}],
              "monster":{"monster_id":123456,"name":"合成敌人甲","level":110,
                "desc":["<color=#FFCC00>合成敌人说明</color>\\n合成机制第二行"],
                "tags":[{"type":1,"desc":"合成元素抗性"},{"type":8,"desc":"合成机制标签"}]},
              "best_avatar":[{"avatar_id":10000093,"dps":3000000000,"type":1},
                             {"avatar_id":10000089,"dps":"6000000000","type":2}]}]},
          "mp":{"has_data":true,"best":{"difficulty":5,"second":240},
            "challenge":[{"name":"合成关卡","second":200,
              "teams":[{"avatar_id":10000089,"name":"合成多人角色","level":90,"rarity":5,"rank":2}],
              "monster":{"monster_id":123456,"name":"合成敌人甲","level":100,
                "desc":["合成多人机制"],"tags":[{"type":2,"desc":"合成多人标签"}]},
              "best_avatar":[{"avatar_id":10000089,"dps":9000000000,"type":2}]}]},
          "blings":[{"avatar_id":10000089,"name":"合成增辉角色","element":"水","rarity":5,"is_plus":true},
                    {"avatar_id":10000093,"name":"合成未上榜角色","element":"风","rarity":5,"is_plus":false},
                    {"avatar_id":10000087,"name":"合成未知角色","rarity":5}]
        }],"is_unlock":true}}
        """;

    public static async Task<ChallengeModeSourceResult> ReadAsync(string? response = null)
    {
        using var message = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response ?? Response) };
        return await new MiYouSheHardChallengeResponseReader(TimeProvider.System).ReadAsync(message);
    }

    public static ChallengeObservation Observation(Guid account, ChallengeModeSnapshot snapshot, ChallengeObservationId? id = null) =>
        new(id ?? ChallengeObservationId.New(), account, snapshot.Mode, snapshot.SeasonId,
            snapshot.PeriodStart, snapshot.PeriodEnd, snapshot.Completeness, [],
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: snapshot.FetchedAt, FetchedAt: snapshot.FetchedAt)),
            snapshot.Stages, snapshot.Difficulty, snapshot.MedalCount,
            stygianStatistics: snapshot.StygianStatistics, multiplayerStages: snapshot.MultiplayerStages);
}

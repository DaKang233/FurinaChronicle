// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;

namespace FurinaChronicle.Tests.TestDoubles;

internal static class TheaterDetailFixture
{
    public const string Response = """
        {"retcode":0,"data":{"data":[{
          "schedule":{"schedule_id":37,"start_time":1794000000,"end_time":1795000000},
          "has_data":true,
          "stat":{"difficulty_id":4,"medal_num":3,"max_round_id":10,"heraldry":4,
            "coin_num":1250,"avatar_bonus_num":2,"rent_cnt":3,"tarot_finished_cnt":1,
            "get_medal_round_list":[10,2,1]},
          "detail":{
            "backup_avatars":[
              {"avatar_id":10000093,"name":"合成自有角色","level":90,"rarity":5,"avatar_type":1},
              {"avatar_id":10000089,"name":"合成试用角色","level":80,"rarity":5,"avatar_type":2},
              {"avatar_id":10000087,"name":"合成助演角色","level":90,"rarity":5,"avatar_type":3}],
            "fight_statisic":{
              "total_use_time":90761,"is_show_battle_stats":true,
              "max_defeat_avatar":{"avatar_id":10000093,"value":"22","rarity":5},
              "max_damage_avatar":{"avatar_id":10000089,"value":3000000000,"rarity":5},
              "max_take_damage_avatar":{"avatar_id":10000087,"value":"","rarity":5},
              "total_coin_consumed":{"avatar_id":0,"value":"1250"},
              "shortest_avatar_list":[{"avatar_id":10000093,"value":"27","rarity":5}]},
            "rounds_data":[
              {"round_id":10,"is_get_medal":true,"finish_time":1794100010,"is_tarot":false,
               "avatars":[{"avatar_id":10000087,"avatar_type":3,"name":"合成助演角色","level":90,"rarity":5}]},
              {"round_id":2,"is_get_medal":true,"finish_time":1794100002,"is_tarot":false,
               "avatars":[{"avatar_id":10000089,"avatar_type":2,"name":"合成试用角色","level":80,"rarity":5}]},
              {"round_id":1,"is_get_medal":true,"finish_time":1794100001,"is_tarot":false,
               "avatars":[{"avatar_id":10000093,"avatar_type":1,"name":"合成自有角色","level":90,"rarity":5}]},
              {"round_id":11,"is_get_medal":false,"finish_time":1794100011,"is_tarot":true,"tarot_serial_no":1}]
          }}]}}
        """;

    public static TheaterStatistics Statistics(int coinCount = 1250) => new(
        maxRoundId: 10, heraldry: 4, coinCount: coinCount, avatarBonusCount: 2, rentCount: 3,
        tarotFinishedCount: 1, totalUseTime: 90761, showBattleStats: true, medalRounds: [1, 2, 10],
        backupAvatars: [Avatar("10000093", "合成自有角色", ChallengeParticipantOrigin.Owned),
            Avatar("10000089", "合成试用角色", ChallengeParticipantOrigin.Trial),
            Avatar("10000087", "合成助演角色", ChallengeParticipantOrigin.Support)],
        maxDefeat: new(Avatar("10000093", null), "22"),
        maxDamage: new(Avatar("10000089", null), "3000000000"),
        maxTakeDamage: new(Avatar("10000087", null), "0"),
        totalCoinsConsumed: "1250", shortestAvatars: [new(Avatar("10000093", null), "27")]);

    private static ChallengeParticipant Avatar(string id, string? name,
        ChallengeParticipantOrigin? origin = null) => new(id, name, null, 90, 5, origin);
}

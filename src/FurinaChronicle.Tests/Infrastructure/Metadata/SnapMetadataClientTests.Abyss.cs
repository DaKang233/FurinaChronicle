// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Infrastructure.Metadata;

namespace FurinaChronicle.Tests.Infrastructure.Metadata;

public sealed partial class SnapMetadataClientTests
{
    private static Dictionary<string, string> AbyssFiles() => new()
    {
        ["Meta.json"] = """{"Meta":"abyss-fixture"}""",
        ["TowerSchedule.json"] = """[{"Id":124,"FloorIds":[1200],"BuffName":"合成渊月祝福","Descriptions":["<color=#fff>祝福效果</color>"]}]""",
        ["TowerFloor.json"] = """
            [
              {"Id":1200,"Index":12,"LevelGroupId":990,"Descriptions":["楼层效果"],"FirstDescriptions":["上半效果"],"SecondDescriptions":["下半效果"]},
              {"Id":9999,"Index":12,"LevelGroupId":991,"Descriptions":["其他期次"]}
            ]
            """,
        ["TowerLevel.json"] = """
            [
              {"Id":1,"GroupId":990,"Index":1,"MonsterLevel":99,
                "FirstWaves":[{"Type":11,"Description":"追加刷新说明","Monsters":[
                  {"Id":5020,"Count":3,"AttackMonolith":true,"Affixes":[{"Name":"合成词缀","Description":"<color=#fff>效果</color>"}]},
                  {"Id":9999999,"Count":3000000000}]}],
                "SecondWaves":[{"Type":42,"Monsters":[{"Id":106,"Count":-1,"AttackMonolith":false}]}],
                "FirstGadget":{"Name":"合成机关","Description":"机关说明"}},
              {"Id":2,"GroupId":990,"Index":2,"MonsterLevel":100,"FirstMonsters":[105],"SecondMonsters":[106]},
              {"Id":3,"GroupId":990,"Index":3,"FirstWaves":[],"FirstMonsters":[105]},
              {"Id":4,"GroupId":991,"Index":1,"MonsterLevel":1,"FirstMonsters":[106]}
            ]
            """,
        ["Monster.json"] = """
            [
              {"Id":20050201,"DescribeId":502,"Name":"合成幻形敌人","Icon":"UI_MonsterIcon_Slime_Grass_02"},
              {"Id":20010501,"DescribeId":105,"Name":"合成旧版敌人","Icon":"UI_MonsterIcon_Slime_Grass_02"},
              {"Id":20010601,"DescribeId":106,"Name":"合成下半敌人","Icon":"../invalid"}
            ]
            """,
    };

    private HttpClient AbyssHttp(Dictionary<string, string> files, bool offline = false) =>
        new(new RecordingHandler(request => offline
            ? throw new HttpRequestException("Synthetic offline")
            : files.TryGetValue(request.RequestUri!.Segments.Last(), out string? json)
                ? Json(json) : new(HttpStatusCode.NotFound)));

    [Fact]
    public async Task AbyssLayoutUsesExactFloorGroupAndPreservesWavesAliasesAndUnknowns()
    {
        using var http = AbyssHttp(AbyssFiles());
        using var snap = new SnapMetadataClient(new(cacheDirectory, BaseAddress), http);
        var schedule = (await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!;
        var floor = Assert.Single(schedule.Abyss!.Floors);
        Assert.Equal(12, floor.Floor);
        Assert.Equal("上半效果", Assert.Single(floor.UpperEffects));
        Assert.Equal("下半效果", Assert.Single(floor.LowerEffects));
        Assert.DoesNotContain("其他期次", schedule.FloorEffects![12]);
        var chamber = floor.Chambers[0];
        Assert.Equal(99, chamber.MonsterLevel);
        Assert.Equal("合成机关", chamber.Upper.Gadget!.Name);
        var wave = Assert.Single(chamber.Upper.Waves);
        Assert.Equal(11, wave.Type);
        Assert.Equal("追加刷新说明", wave.Description);
        Assert.Equal("5020", wave.Monsters[0].DescribeId); // Display alias must not replace the source identity.
        Assert.Equal("合成幻形敌人", wave.Monsters[0].Name);
        Assert.Equal(3, wave.Monsters[0].Count);
        Assert.True(wave.Monsters[0].AttackMonolith);
        Assert.Equal("效果", Assert.Single(wave.Monsters[0].Affixes).Description);
        Assert.Equal("static.snaphutaorp.org", new Uri(wave.Monsters[0].IconUrl!).Host);
        Assert.Null(wave.Monsters[1].Name);
        Assert.Equal(3_000_000_000, wave.Monsters[1].Count);
        var lower = Assert.Single(chamber.Lower.Waves).Monsters[0];
        Assert.Null(lower.Count);
        Assert.False(lower.AttackMonolith);
        Assert.Null(lower.IconUrl);
        var legacy = Assert.Single(floor.Chambers[1].Upper.Waves);
        Assert.True(legacy.IsLegacy);
        Assert.Null(legacy.Type);
        Assert.Null(Assert.Single(legacy.Monsters).Count);
        Assert.True(floor.Chambers[2].Upper.HasEnemyData); // Explicit [] wins over the old list.
        Assert.Empty(floor.Chambers[2].Upper.Waves);
        Assert.False(floor.Chambers[2].Lower.HasEnemyData);
        Assert.Null(await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "125"));
    }

    [Theory]
    [InlineData("TowerLevel.json", false)]
    [InlineData("Monster.json", true)]
    public async Task BrokenOptionalTableDoesNotHideBlessingOrUnrelatedFields(string table, bool keepsChambers)
    {
        var files = AbyssFiles();
        files[table] = "{invalid";
        using var http = AbyssHttp(files);
        using var snap = new SnapMetadataClient(new(cacheDirectory, BaseAddress), http);
        var schedule = (await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!;
        Assert.Equal("合成渊月祝福", schedule.Blessing!.Name);
        Assert.Equal("祝福效果", Assert.Single(schedule.Blessing.Descriptions));
        var floor = Assert.Single(schedule.Abyss!.Floors);
        Assert.Equal(keepsChambers, floor.Chambers.Count > 0);
        if (keepsChambers) Assert.Null(floor.Chambers[0].Upper.Waves[0].Monsters[0].Name);
    }

    [Fact]
    public async Task CachedAbyssConfigurationWorksOfflineAndCancellationStillPropagates()
    {
        using (var http = AbyssHttp(AbyssFiles()))
        using (var snap = new SnapMetadataClient(new(cacheDirectory, BaseAddress), http))
            Assert.NotNull((await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!.Abyss);

        using var offline = AbyssHttp(AbyssFiles(), offline: true);
        using var cold = new SnapMetadataClient(new(cacheDirectory, BaseAddress), offline);
        var floor = Assert.Single((await cold.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!.Abyss!.Floors);
        Assert.Equal("合成幻形敌人", floor.Chambers[0].Upper.Waves[0].Monsters[0].Name);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cold.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124", cancellation.Token));
    }

    [Fact]
    public async Task MissingOptionalTablesPreserveSeasonAndFloorEffects()
    {
        var files = AbyssFiles();
        files.Remove("TowerLevel.json");
        files.Remove("Monster.json");
        using var http = AbyssHttp(files);
        using var snap = new SnapMetadataClient(new(cacheDirectory, BaseAddress), http);
        var schedule = (await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!;
        Assert.NotNull(schedule.Blessing);
        Assert.Equal("楼层效果", Assert.Single(schedule.FloorEffects![12]));
        Assert.Empty(Assert.Single(schedule.Abyss!.Floors).Chambers);
    }

    [Fact]
    public async Task AmbiguousMonsterDescriptionsNeverChooseAnArbitraryName()
    {
        var files = AbyssFiles();
        files["Monster.json"] = """[{"Id":1,"DescribeId":502,"Name":"甲"},{"Id":2,"DescribeId":502,"Name":"乙"}]""";
        using var http = AbyssHttp(files);
        using var snap = new SnapMetadataClient(new(cacheDirectory, BaseAddress), http);
        var monster = (await snap.ResolveScheduleAsync(ChallengeMode.SpiralAbyss, "124"))!
            .Abyss!.Floors[0].Chambers[0].Upper.Waves[0].Monsters[0];
        Assert.Equal("5020", monster.DescribeId);
        Assert.Null(monster.Name);
    }

    [Theory]
    [InlineData("http://example.invalid/images/")]
    [InlineData("https://example.invalid/images/?authkey=synthetic")]
    [InlineData("https://user:synthetic@example.invalid/images/")]
    [InlineData("https://example.invalid/images")]
    public void MonsterImageBaseRejectsInappropriateUris(string uri) =>
        Assert.Throws<ArgumentException>(() => new SnapMetadataOptions(cacheDirectory, monsterIconBaseAddress: new Uri(uri)));
}

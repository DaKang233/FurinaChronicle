// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class AbyssConfigurationViewTests
{
    private static readonly DateTimeOffset Time = new(2026, 10, 1, 12, 34, 56, TimeSpan.FromHours(8));

    [Fact]
    public async Task ShowsConfigurationSeparateFromRecordedBattleAndKeepsOfficialSeconds()
    {
        var observation = Observation();
        string before = JsonSerializer.Serialize(observation);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(Schedule()), "合成账号");
        var floor = Assert.Single(view.DetailSections);
        Assert.Contains(floor.Lines, line => line.Contains("不表示实际出战或击破"));
        Assert.Contains("上半异常：合成上半异常", floor.Lines);
        Assert.Contains("下半异常：合成下半异常", floor.Lines);
        var upper = Assert.Single(floor.Battles, battle => battle.Title.StartsWith("第 1 间 · 上半 · 完成"));
        Assert.Contains(Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), upper.Title);
        Assert.Contains("机关（期次配置）：合成机关", upper.Lines);
        Assert.Equal("10000089(Lv.90)", upper.Groups[0].Portraits[0].Label);
        var wave = Assert.Single(floor.Battles, battle => battle.Title.Contains("A 组第 1 波"));
        Assert.Contains("（期次配置）", wave.Title);
        Assert.Contains("追加说明", wave.Lines);
        Assert.Contains("合成敌人：会攻击地脉镇石", wave.Lines);
        Assert.Contains("合成敌人 · 词缀：合成词缀 · 词缀效果", wave.Lines);
        Assert.Equal("合成敌人(Lv.99) ×3000000000", wave.Groups[0].Portraits[0].Label);
        Assert.NotNull(wave.Groups[0].Portraits[0].Icon);
        Assert.False(wave.Groups[0].Portraits[0].IsCharacter);
        var missingBattle = Assert.Single(floor.Battles, battle => battle.Title == "第 2 间 · 上半（期次配置）");
        Assert.Contains("未保存该半场战斗记录。", missingBattle.Lines);
        Assert.Empty(missingBattle.Groups); // Metadata cannot become a player's party.
        var legacy = Assert.Single(floor.Battles, battle => battle.Title.Contains("旧版，波次未知"));
        Assert.Equal("敌人 #999(Lv.100) · 数量未知", legacy.Groups[0].Portraits[0].Label);
        Assert.Null(legacy.Groups[0].Portraits[0].Icon);
        Assert.Equal(before, JsonSerializer.Serialize(observation));
        Assert.Single(observation.SpiralAbyssFloors[0].Chambers);
        Assert.Equal(3, observation.SpiralAbyssFloors[0].Stars);
    }

    [Fact]
    public async Task StyledDescriptionsAreReadableAcrossAllMetadataSectionsWithoutChangingSources()
    {
        var schedule = Schedule();
        var floor = schedule.Abyss!.Floors[0];
        var chamber = floor.Chambers[0];
        var upper = chamber.Upper;
        var wave = upper.Waves[0];
        var monster = wave.Monsters[0];
        schedule = schedule with
        {
            Blessing = new("<b>渊月祝福</b>", ["<color=#fff>伤害</color> &amp; 增益\\n下一行"]),
            FloorEffects = new Dictionary<int, IReadOnlyList<string>> { [12] = ["基础<br/>规则"] },
            Abyss = new([floor with
            {
                UpperEffects = ["<b>合成上半异常</b>"],
                LowerEffects = ["<size=20>合成下半异常</size>"],
                Chambers = [chamber with
                {
                    Upper = upper with
                    {
                        Gadget = new("<b>合成机关</b>", "<i>机关效果</i>"),
                        Waves = [wave with
                        {
                            Description = "<b>维持5名盗宝团在场，击败一名补充一名</b>",
                            Monsters = [monster with
                            {
                                Name = "<b>合成敌人</b>",
                                Affixes = [new("<b>合成词缀</b>", "<color=#fff>词缀效果</color> <未知标记>")],
                            }],
                        }],
                    },
                }],
            }]),
        };
        var observation = Observation();
        string metadataBefore = JsonSerializer.Serialize(schedule);
        string factsBefore = JsonSerializer.Serialize(observation);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(schedule), "合成");
        string text = Flatten(view);
        Assert.Contains("渊月祝福", text);
        Assert.Contains("伤害 & 增益\n下一行", text);
        Assert.Contains("基础\n规则", text);
        Assert.Contains("上半异常：合成上半异常", text);
        Assert.Contains("下半异常：合成下半异常", text);
        Assert.Contains("机关（期次配置）：合成机关", text);
        Assert.Contains("维持5名盗宝团在场，击败一名补充一名", text);
        Assert.Contains("合成敌人 · 词缀：合成词缀 · 词缀效果 <未知标记>", text);
        Assert.DoesNotContain("<b>", text);
        Assert.DoesNotContain("<color", text);
        Assert.DoesNotContain("<size", text);
        Assert.DoesNotContain("<i>", text);
        Assert.DoesNotContain("<br", text);
        Assert.Equal(metadataBefore, JsonSerializer.Serialize(schedule));
        Assert.Equal(factsBefore, JsonSerializer.Serialize(observation));
    }

    [Theory]
    [InlineData("125", ChallengeMode.SpiralAbyss)]
    [InlineData("124", ChallengeMode.ImaginariumTheater)]
    public async Task OtherSeasonOrModeCannotSupplyConfiguration(string season, ChallengeMode mode)
    {
        var schedule = Schedule() with { ScheduleId = season, Mode = mode };
        var view = await HistoryObservationViewBuilder.ChallengeAsync(Observation(), new Metadata(schedule), "合成");
        string shown = Flatten(view);
        Assert.DoesNotContain("合成敌人", shown);
        Assert.DoesNotContain("合成上半异常", shown);
        Assert.Contains("不从其他期次推断", shown);
    }

    [Fact]
    public async Task MissingFloorOrOptionalMetadataDoesNotHideTheRecordedParty()
    {
        var schedule = Schedule() with { Abyss = new([Schedule().Abyss!.Floors[0] with { Floor = 11 }]) };
        var view = await HistoryObservationViewBuilder.ChallengeAsync(Observation(), new Metadata(schedule), "合成");
        Assert.Contains("10000089(Lv.90)", Flatten(view));
        Assert.Contains("敌方配置缺失", Flatten(view));
        Assert.DoesNotContain("合成敌人", Flatten(view));
    }

    [Theory]
    [InlineData(123456, "其他刷新规则（类型 123456）")]
    [InlineData(99, "暂缺分波信息")]
    [InlineData(999, "增援怪物")]
    [InlineData(1999, "第 1 波增援")]
    [InlineData(99999, "维持 4 名盗宝团在场，击败后补充（总计 12 名）")]
    public async Task WaveRulesAreVisibleAndDoNotInventTimeOrStarResults(int type, string label)
    {
        var observation = Observation() with { };
        var schedule = Schedule();
        var old = schedule.Abyss!.Floors[0];
        var chamber = old.Chambers[0];
        schedule = schedule with { Abyss = new([old with
        {
            Chambers = [chamber with { Upper = chamber.Upper with
            {
                Waves = [chamber.Upper.Waves[0] with { Type = type }],
            }}],
        }]) };
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(schedule), "合成");
        Assert.Contains(label, Flatten(view));
        Assert.Equal(3, observation.SpiralAbyssFloors[0].Stars);
    }

    [Theory]
    [InlineData("https://api.snaphutaorp.org/private/image.png")]
    [InlineData("https://api.hutaorp.org/static/raw/AvatarIcon/image.png")]
    [InlineData("https://api.snaphutaorp.org.evil.invalid/static/raw/MonsterIcon/image.png")]
    [InlineData("https://api.snaphutaorp.org/static/raw/MonsterIcon/image.png?authkey=synthetic")]
    [InlineData("https://user:synthetic@api.snaphutaorp.org/static/raw/MonsterIcon/image.png")]
    public async Task MonsterArtUsesOnlyTheNarrowPublicResourceRoute(string icon)
    {
        var schedule = Schedule();
        var floor = schedule.Abyss!.Floors[0];
        var chamber = floor.Chambers[0];
        var wave = chamber.Upper.Waves[0];
        schedule = schedule with { Abyss = new([floor with
        {
            Chambers = [chamber with { Upper = chamber.Upper with
            {
                Waves = [wave with { Monsters = [wave.Monsters[0] with { IconUrl = icon }] }],
            }}],
        }]) };
        var view = await HistoryObservationViewBuilder.ChallengeAsync(Observation(), new Metadata(schedule), "合成");
        var target = Assert.Single(view.DetailSections[0].Battles,
            battle => battle.Title.Contains("A 组第 1 波"));
        Assert.Null(Assert.Single(target.Groups[0].Portraits).Icon);
        Assert.Contains("合成敌人", Flatten(view));
    }

    private static string Flatten(ChallengeObservationView view) => string.Join("\n",
        view.DetailSections.SelectMany(section => section.Lines.Concat([section.Title])
            .Concat(section.Battles.SelectMany(battle => battle.Lines.Concat([battle.Title])
                .Concat(battle.Groups.SelectMany(group => group.Portraits.Select(portrait => portrait.Label)))))));

    private static ChallengeScheduleMetadata Schedule()
    {
        var upper = new AbyssHalfMetadata(
            [new(11, "追加说明", [new("5020", "合成敌人",
                "https://api.snaphutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png",
                3_000_000_000, true, [new("合成词缀", "词缀效果")])])],
            new("合成机关", "机关效果"), true);
        var lower = new AbyssHalfMetadata([], null, true);
        var legacy = new AbyssHalfMetadata([new(null, null, [new("999", null, null, null, null, [])], true)],
            null, true);
        return new(ChallengeMode.SpiralAbyss, "124", null, null, null, "Synthetic",
            Abyss: new([new(12, ["合成上半异常"], ["合成下半异常"],
                [new(1, 99, upper, lower), new(2, 100, legacy, new([], null, false))])]));
    }

    private static ChallengeObservation Observation() => new(ChallengeObservationId.New(), Guid.NewGuid(),
        ChallengeMode.SpiralAbyss, "124", null, null, DataCompleteness.Partial,
        [new(12, DataCompleteness.Partial,
            [new(1, 3, OccurrenceTime.At(Time.AddMinutes(1), TimePrecision.Second),
                [new(1, OccurrenceTime.At(Time, TimePrecision.Second),
                    [new("10000089", null, null, 90, 5)]),
                 new(2, OccurrenceTime.At(Time.AddMinutes(1), TimePrecision.Second))])])],
        new(DataOrigin.OfficialApi, new RecordTimestamps(FetchedAt: Time.AddMinutes(2))));

    private sealed class Metadata(ChallengeScheduleMetadata schedule) : IDomainMetadataService
    {
        public ValueTask<DomainItemMetadata?> ResolveItemAsync(
            InventoryItemKind kind, string typeId, CancellationToken cancellationToken = default) => new((DomainItemMetadata?)null);
        public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(
            ChallengeMode mode, string seasonId, CancellationToken cancellationToken = default) => new(schedule);
    }
}

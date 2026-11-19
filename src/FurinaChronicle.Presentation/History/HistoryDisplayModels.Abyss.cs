// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

public sealed partial record ChallengeObservationCard
{
    private static async Task<ChallengeDetailSection> BuildAbyssFloorAsync(
        SpiralAbyssFloorResult floor, AbyssFloorMetadata? layout, IReadOnlyList<string> effects,
        IDomainMetadataService metadata, Dictionary<string, DomainItemMetadata?> names,
        CancellationToken cancellationToken)
    {
        var lines = effects.Select(HoYoDescriptionText.Plain).ToList();
        if (layout is null) lines.Add("该期该层的敌方配置缺失；不从其他期次推断。");
        else
        {
            lines.Add("敌方、异常与机关为期次配置，不表示实际出战或击破。");
            lines.AddRange(layout.UpperEffects.Select(effect => "上半异常：" + HoYoDescriptionText.Plain(effect)));
            lines.AddRange(layout.LowerEffects.Select(effect => "下半异常：" + HoYoDescriptionText.Plain(effect)));
        }
        var rounds = new List<ChallengeCombatDetail>();
        var chamberDetails = new List<AbyssChamberDetail>();
        int[] chambers = floor.Chambers.Select(chamber => chamber.Chamber)
            .Concat(layout?.Chambers.Select(chamber => chamber.Chamber) ?? [])
            .Distinct().Order().ToArray();
        foreach (int index in chambers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SpiralAbyssChamberResult? chamber = floor.Chambers.FirstOrDefault(item => item.Chamber == index);
            AbyssChamberMetadata? chamberLayout = layout?.Chambers.FirstOrDefault(item => item.Chamber == index);
            string title = chamber is null ? $"第 {index} 间 · 未保存该间战斗记录"
                : WithCompletion($"第 {index} 间 · {chamber.Stars} 星", chamber.CompletedAt);
            if (chamber?.Battles.Count is not > 0 && chamberLayout is null)
            {
                rounds.Add(new(title, [], []));
                chamberDetails.Add(new(title, [], []));
                continue;
            }
            var halfDetails = new List<ChallengeCombatDetail>[] { [], [] };
            foreach (int half in new[] { 1, 2 })
            {
                SpiralAbyssBattleResult? battle = chamber?.Battles.FirstOrDefault(item => item.Half == half);
                AbyssHalfMetadata? halfLayout = half == 1 ? chamberLayout?.Upper : chamberLayout?.Lower;
                if (battle is null && halfLayout is null) continue;
                string halfName = half == 1 ? "上半" : "下半";
                var battleLines = new List<string>();
                var richBattleLines = new List<string>();
                if (half == 1) { battleLines.Add(title); richBattleLines.Add(title); }
                if (battle is null) { battleLines.Add("未保存该半场战斗记录。"); richBattleLines.Add("未保存该半场战斗记录。"); }
                if (halfLayout?.Gadget is { } gadget)
                {
                    richBattleLines.Add("机关（期次配置）：" + (gadget.Name ?? "名称未知"));
                    if (gadget.Description is { } rawDescription) richBattleLines.Add(rawDescription);
                    battleLines.Add("机关（期次配置）：" + HoYoDescriptionText.Plain(gadget.Name ?? "名称未知"));
                    if (gadget.Description is { } description) battleLines.Add(HoYoDescriptionText.Plain(description));
                }
                if (halfLayout is { HasEnemyData: false }) { battleLines.Add("该半场敌方配置缺失。"); richBattleLines.Add("该半场敌方配置缺失。"); }
                if (halfLayout is { HasEnemyData: true, Waves.Count: 0 })
                { battleLines.Add("该半场期次配置未列出敌人。"); richBattleLines.Add("该半场期次配置未列出敌人。"); }
                IReadOnlyList<ChallengePortraitGroup> groups = battle is null ? []
                    : await BuildPortraitGroupsAsync(battle.Participants, battle.Enemies, metadata, names, cancellationToken);
                var halfBattle = new ChallengeCombatDetail(
                    battle is null ? $"第 {index} 间 · {halfName}（期次配置）"
                        : WithCompletion($"第 {index} 间 · {halfName}", battle.CompletedAt),
                    battleLines, groups) { RichLines = richBattleLines };
                rounds.Add(halfBattle);
                halfDetails[half - 1].Add(halfBattle);
                foreach (AbyssWaveMetadata wave in halfLayout?.Waves ?? [])
                {
                    var details = new List<string>();
                    var richDetails = new List<string>();
                    if (wave.Description is { } description) { details.Add(HoYoDescriptionText.Plain(description)); richDetails.Add(description); }
                    var portraits = new List<ChallengePortrait>();
                    foreach (AbyssMonsterMetadata monster in wave.Monsters)
                    {
                        string name = HoYoDescriptionText.Plain(monster.Name ?? $"敌人 #{monster.DescribeId}");
                        string label = DescribeCombatant(name, chamberLayout?.MonsterLevel) +
                            (monster.Count is long count ? $" ×{count}" : " · 数量未知");
                        if (monster.AttackMonolith == true) { details.Add(name + "：会攻击地脉镇石"); richDetails.Add(name + "：会攻击地脉镇石"); }
                        foreach (ChallengeDescriptionMetadata affix in monster.Affixes)
                        {
                            richDetails.Add(name + " · 词缀：" + string.Join(" · ", new[] { affix.Name, affix.Description }.OfType<string>()));
                            details.Add(name + " · 词缀：" + string.Join(" · ",
                                new[] { affix.Name, affix.Description }.OfType<string>().Select(HoYoDescriptionText.Plain)));
                        }
                        portraits.Add(new(label, ReadArtworkUri(monster.IconUrl)));
                    }
                    var waveBattle = new ChallengeCombatDetail($"第 {index} 间 · {halfName} · {WaveName(wave)}（期次配置）",
                        details, [new("敌人（期次配置）", portraits)]) { RichLines = richDetails };
                    rounds.Add(waveBattle);
                    halfDetails[half - 1].Add(waveBattle);
                }
            }
            chamberDetails.Add(new(title, halfDetails[0], halfDetails[1]));
        }
        return new($"第 {floor.Floor} 层 · {floor.Stars} 星", lines, rounds)
        {
            Kind = ChallengeSectionKind.AbyssFloor, Number = floor.Floor, Chambers = chamberDetails,
            UpperEffects = layout?.UpperEffects ?? [], LowerEffects = layout?.LowerEffects ?? [],
            RichLines = effects.Concat([layout is null ? "该期该层的敌方配置缺失；不从其他期次推断。" : "敌方、异常与机关为期次配置，不表示实际出战或击破。"]).ToArray(),
        };
    }

    private static string WaveName(AbyssWaveMetadata wave)
    {
        if (wave.IsLegacy) return "敌人列表（旧版，波次未知）";
        int? type = wave.Type;
        if (type is 0 or 114514) return "独立刷新";
        if (type is >= 1 and <= 4) return $"第 {type} 波";
        if (type == 99) return "暂缺分波信息";
        if (type == 999) return "增援怪物";
        if (type == 1999) return "第 1 波增援";
        if (type == 99999) return "维持 4 名盗宝团在场，击败后补充（总计 12 名）";
        if (type is >= 10 and <= 43 && type % 10 is >= 0 and <= 3)
        {
            char group = (char)('A' + type.Value / 10 - 1);
            return type % 10 == 0 ? $"{group} 组" : $"{group} 组第 {type % 10} 波";
        }
        return type.HasValue ? $"其他刷新规则（类型 {type}）" : "波次未知";
    }
}

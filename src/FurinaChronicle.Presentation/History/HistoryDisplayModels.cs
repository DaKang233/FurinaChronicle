// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Presentation.History;

/// <summary>
/// Display-only projection for challenge history. It is shared by current MAUI
/// pages and the ongoing WinUI3 redesign so neither host duplicates wording.
/// </summary>
public sealed partial record ChallengeObservationCard(
    string Title,
    string Evidence,
    string Detail)
{
    public static ChallengeObservationCard FromObservation(
        ChallengeObservation observation,
        string? scheduleName = null)
    {
        string coverage = observation.Completeness switch
        {
            DataCompleteness.Complete => "完整观察",
            DataCompleteness.Partial => "部分观察",
            _ => "覆盖未知",
        };
        string observed = observation.Provenance.Timestamps.ObservedAt is { } time
            ? time.ToLocalTime().ToString("g")
            : "未知";
        string evidence = $"{coverage} · 观察于 {observed}";
        if (observation.Mode == ChallengeMode.SpiralAbyss)
        {
            int stars = observation.SpiralAbyssStatistics?.TotalStars ?? observation.SpiralAbyssFloors.Sum(floor => floor.Stars);
            string floors = string.Join("\n", observation.SpiralAbyssFloors.Select(floor =>
                $"第 {floor.Floor} 层 · {floor.Stars} 星  " +
                string.Join("  ", floor.Chambers.Select(chamber =>
                {
                    string? time = DescribeExactCompletion(chamber.CompletedAt);
                    return $"{chamber.Chamber} 间 {chamber.Stars} 星" +
                        (time is null ? string.Empty : $" · {time}");
                }))));
            return new($"深境螺旋 · 第 {observation.SeasonId} 期 · {stars} 星", evidence, floors);
        }

        string mode = ModeName(observation.Mode);
        string schedule = string.IsNullOrWhiteSpace(scheduleName)
            ? $"第 {observation.SeasonId} 期"
            : $"{scheduleName} · 第 {observation.SeasonId} 期";
        string summary = observation.Mode == ChallengeMode.ImaginariumTheater
            ? $"难度 {TheaterDifficultyName(observation.Difficulty)} · " +
              $"奖章 {observation.MedalCount?.ToString() ?? "未知"}"
            : observation.StygianStatistics is { } stygian ? StygianDifficultySummary(stygian) :
                $"最高难度 {observation.Difficulty?.ToString() ?? "未知"}";
        string stages = observation.Mode == ChallengeMode.StygianOnslaught
            ? string.Join("\n", observation.Stages.Select(stage => "单人 · " + DescribeStage(stage, observation.Mode))
                .Concat(observation.MultiplayerStages.Select(stage => "联机 · " + DescribeStage(stage, observation.Mode))))
            : string.Join("\n", StagesForDisplay(observation).Select(stage => DescribeStage(stage, observation.Mode)));
        return new($"{mode} · {schedule}", $"{evidence} · {summary}", stages);
    }

    public static string DescribeStage(
        ChallengeStageResult stage,
        ChallengeMode mode)
        => string.Join(" · ", new[] { mode == ChallengeMode.ImaginariumTheater ? TheaterStageTitle(stage) : stage.StageId }
            .Concat(DescribeStageFields(stage, mode)));

    private static IReadOnlyList<string> DescribeStageFields(
        ChallengeStageResult stage, ChallengeMode mode, bool includeCombatants = true)
    {
        var parts = new List<string>();
        if (stage.Score is int score)
        {
            parts.Add(mode == ChallengeMode.ImaginariumTheater
                ? score > 0 ? "已获奖章" : "未获奖章"
                : $"{score} 秒");
        }
        if (mode != ChallengeMode.StygianOnslaught &&
            DescribeExactCompletion(stage.CompletedAt) is { } completion)
        {
            parts.Add(completion);
        }
        if (includeCombatants && stage.Participants.Count > 0)
        {
            parts.Add("阵容 " + string.Join(
                "、",
                stage.Participants.Select(participant => DescribeParticipant(participant))));
        }
        if (stage.Buffs.Count > 0 && (includeCombatants || stage.TheaterRound?.BuffDetails is null))
        {
            parts.Add("增益与祝福");
            parts.AddRange(stage.Buffs.Select(HoYoDescriptionText.Plain));
        }
        if (stage.TheaterRound is { BuffSummary: { } summary } &&
            (stage.Buffs.Count == 0 || (!includeCombatants && stage.TheaterRound.BuffDetails is not null)))
            parts.Add("祝福总览：" + HoYoDescriptionText.Plain(summary));
        if (includeCombatants && stage.Enemies.Count > 0)
        {
            parts.Add("敌人 " + string.Join(
                "、",
                stage.Enemies.Select(enemy =>
                    enemy.Name is { } enemyName
                        ? enemy.Level is int enemyLevel
                            ? $"{enemyName}(Lv.{enemyLevel})"
                            : enemyName
                        : "未知")));
        }
        return parts;
    }

    internal static async Task<IReadOnlyList<ChallengeDetailSection>> BuildDetailSectionsAsync(
        ChallengeObservation observation,
        IDomainMetadataService metadata,
        ChallengeScheduleMetadata? schedule,
        CancellationToken cancellationToken)
    {
        var sections = new List<ChallengeDetailSection>();
        if (schedule is not null && (schedule.Mode != observation.Mode || schedule.ScheduleId != observation.SeasonId))
            schedule = null;
        var names = new Dictionary<string, DomainItemMetadata?>(StringComparer.Ordinal);
        if (observation.Mode == ChallengeMode.SpiralAbyss)
        {
            if (observation.SpiralAbyssStatistics is { } statistics)
            {
                sections.Add(new ChallengeDetailSection("挑战总览", DescribeAbyssStatistics(statistics), []));
                (string Title, IReadOnlyList<SpiralAbyssRankEntry> Entries)[] rankings =
                [
                    ("出战次数", statistics.RevealRank), ("击破次数", statistics.DefeatRank),
                    ("最强一击", statistics.DamageRank), ("承受伤害", statistics.TakeDamageRank),
                    ("元素战技释放", statistics.NormalSkillRank), ("元素爆发释放", statistics.EnergySkillRank),
                ];
                foreach (var ranking in rankings.Where(ranking => ranking.Entries.Count > 0))
                    sections.Add(await BuildAbyssRankSectionAsync(
                        ranking.Title, ranking.Entries, metadata, names, cancellationToken));
            }
            if (schedule?.Blessing is { } blessing)
            {
                sections.Add(new ChallengeDetailSection(
                    HoYoDescriptionText.Plain(blessing.Name ?? "渊月祝福"),
                    blessing.Descriptions.Select(HoYoDescriptionText.Plain).ToArray(), []));
            }
            foreach (SpiralAbyssFloorResult floor in observation.SpiralAbyssFloors)
            {
                AbyssFloorMetadata? layout = schedule?.Mode == observation.Mode &&
                    schedule.ScheduleId == observation.SeasonId
                    ? schedule.Abyss?.Floors.FirstOrDefault(item => item.Floor == floor.Floor) : null;
                IReadOnlyList<string> effects = schedule?.FloorEffects?.GetValueOrDefault(floor.Floor) ?? [];
                sections.Add(await BuildAbyssFloorAsync(floor, layout, effects, metadata, names, cancellationToken));
            }
            return sections;
        }

        if (observation.Mode == ChallengeMode.StygianOnslaught)
        {
            await AddStygianDetailsAsync(sections, observation, metadata, names, cancellationToken);
            return sections;
        }
        if (observation.Mode == ChallengeMode.ImaginariumTheater)
            await AddTheaterDetailsAsync(sections, observation, schedule, metadata, names, cancellationToken);
        foreach (ChallengeStageResult stage in StagesForDisplay(observation))
        {
            string heading = observation.Mode == ChallengeMode.ImaginariumTheater
                ? TheaterStageTitle(stage)
                : stage.StageId;
            IReadOnlyList<ChallengePortraitGroup> groups = await BuildPortraitGroupsAsync(
                stage.Participants, stage.Enemies, metadata, names, cancellationToken);
            sections.Add(new ChallengeDetailSection(
                heading,
                DescribeStageFields(stage, observation.Mode, includeCombatants: false),
                groups.Count > 0 ? [new ChallengeCombatDetail("战斗详情", [], groups)] : [])
            {
                Buffs = BuildTheaterBuffCards(stage),
            });
        }
        return sections;
    }

    private static IReadOnlyList<string> DescribeAbyssStatistics(SpiralAbyssStatistics statistics)
    {
        var lines = new List<string>();
        if (statistics.TotalBattleTimes is int battles) lines.Add($"战斗次数：{battles}");
        if (statistics.TotalWinTimes is int wins) lines.Add($"胜利次数：{wins}");
        if (statistics.MaxFloor is { } floor) lines.Add($"最深进度：{floor}");
        if (statistics.TotalStars is int stars) lines.Add($"官方总星数：{stars}");
        return lines;
    }

    private static async Task<ChallengeDetailSection> BuildAbyssRankSectionAsync(
        string title, IReadOnlyList<SpiralAbyssRankEntry> ranking,
        IDomainMetadataService metadata, Dictionary<string, DomainItemMetadata?> names,
        CancellationToken cancellationToken)
    {
        var portraits = new List<ChallengePortrait>(ranking.Count);
        foreach (SpiralAbyssRankEntry entry in ranking)
        {
            if (!names.TryGetValue(entry.AvatarId, out DomainItemMetadata? item))
            {
                item = await metadata.ResolveItemAsync(
                    InventoryItemKind.Character, entry.AvatarId, cancellationToken);
                names.Add(entry.AvatarId, item);
            }
            portraits.Add(new ChallengePortrait(
                $"{item?.Name ?? entry.AvatarId} · {entry.Value.ToString(CultureInfo.InvariantCulture)}",
                ReadArtworkUri(entry.IconUrl) ?? ReadArtworkUri(item?.IconUrl), entry.Rarity ?? item?.Rarity, true));
        }
        return new ChallengeDetailSection(title, [],
            [new ChallengeCombatDetail(string.Empty, [], [new ChallengePortraitGroup(string.Empty, portraits)])]);
    }

    private static async Task<IReadOnlyList<ChallengePortraitGroup>> BuildPortraitGroupsAsync(
        IReadOnlyList<ChallengeParticipant> participants,
        IReadOnlyList<ChallengeEnemy> enemies,
        IDomainMetadataService metadata,
        Dictionary<string, DomainItemMetadata?> names,
        CancellationToken cancellationToken)
    {
        var groups = new List<ChallengePortraitGroup>(2);
        if (participants.Count > 0)
        {
            var portraits = new List<ChallengePortrait>(participants.Count);
            foreach (ChallengeParticipant participant in participants)
            {
                DomainItemMetadata? item = null;
                if (participant.Name is null || participant.IconUrl is null)
                {
                    if (!names.TryGetValue(participant.Id, out item))
                    {
                        item = await metadata.ResolveItemAsync(
                            InventoryItemKind.Character, participant.Id, cancellationToken);
                        names.Add(participant.Id, item);
                    }
                }
                string name = participant.Name ?? item?.Name ?? participant.Id;
                portraits.Add(new ChallengePortrait(
                    DescribeParticipant(participant, name),
                    ReadArtworkUri(participant.IconUrl) ?? ReadArtworkUri(item?.IconUrl), participant.Rarity ?? item?.Rarity, true));
            }
            groups.Add(new ChallengePortraitGroup("阵容", portraits));
        }
        if (enemies.Count > 0)
        {
            groups.Add(new ChallengePortraitGroup("敌人", enemies.Select(enemy =>
                new ChallengePortrait(
                    DescribeCombatant(enemy.Name ?? "未知敌人", enemy.Level),
                    ReadArtworkUri(enemy.IconUrl))).ToArray()));
        }
        return groups;
    }

    internal static Uri? ReadArtworkUri(string? value) => ChallengeArtworkUri.Parse(value);

    private static string WithCompletion(string label, OccurrenceTime time) =>
        DescribeExactCompletion(time) is { } completion ? $"{label} · {completion}" : label;

    private static string DescribeCombatant(string name, int? level) =>
        level is int knownLevel ? $"{name}(Lv.{knownLevel})" : name;

    private static string? DescribeExactCompletion(OccurrenceTime time) =>
        time.Kind == OccurrenceTimeKind.Instant && time.Precision == TimePrecision.Second &&
        time.UtcStart is { } start
            ? "完成于 " + start.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : null;

    public static string ModeName(ChallengeMode mode) => mode switch
    {
        ChallengeMode.ImaginariumTheater => "幻想真境剧诗",
        ChallengeMode.StygianOnslaught => "幽境危战",
        _ => "深境螺旋",
    };
}

/// <summary>
/// Display-only projection for character/weapon observations. Names come from
/// the metadata service; a missing source keeps the raw type id.
/// </summary>
public sealed record CharacterObservationCard(
    string Title,
    string Evidence,
    string Detail)
{
    private const int NamedEntryLimit = 8;

    public static async Task<CharacterObservationCard> FromObservationAsync(
        ItemStateObservation observation,
        IDomainMetadataService metadata,
        CancellationToken cancellationToken = default)
    {
        string kind = observation.Kind == InventoryItemKind.Character
            ? "角色"
            : "武器";
        string coverage = observation.CollectionCompleteness switch
        {
            DataCompleteness.Complete => "完整观察",
            DataCompleteness.Partial => "部分观察",
            _ => "覆盖未知",
        };
        string observed = observation.Provenance.Timestamps.ObservedAt is { } time
            ? time.ToLocalTime().ToString("g")
            : "未知";
        string provider = observation.Provenance.Source?.Provider ?? "未知来源";
        var names = new Dictionary<string, DomainItemMetadata?>(StringComparer.Ordinal);
        foreach (InventoryStateEntry entry in observation.Entries.Take(NamedEntryLimit))
        {
            names[entry.StableItemKey] = await metadata.ResolveItemAsync(
                observation.Kind, entry.TypeId, cancellationToken);
        }
        string detail = observation.Kind == InventoryItemKind.Character
            ? DescribeCharacters(observation.Entries, names)
            : DescribeWeapons(observation.Entries, names);
        return new(
            $"{kind} · {observation.Entries.Count} 条",
            $"{coverage} · 观察于 {observed} · {provider}",
            detail);
    }

    private static string DescribeCharacters(
        IReadOnlyList<InventoryStateEntry> entries,
        IReadOnlyDictionary<string, DomainItemMetadata?> names)
    {
        string listed = string.Join(
            "、",
            entries.Take(NamedEntryLimit).Select(entry =>
            {
                string name = names.TryGetValue(entry.StableItemKey, out var item) &&
                    item is not null
                    ? item.Name
                    : entry.TypeId;
                string level = entry.Level is int value ? $" Lv.{value}" : string.Empty;
                string constellation = entry.Progression is > 0
                    ? $" {entry.Progression} 命"
                    : string.Empty;
                return $"{name}{level}{constellation}";
            }));
        int knownLevel = entries.Count(entry => entry.Level is not null);
        int maximum = entries
            .Where(entry => entry.Level is not null)
            .Select(entry => entry.Level!.Value)
            .DefaultIfEmpty(0)
            .Max();
        string suffix = entries.Count > NamedEntryLimit
            ? $" …共 {entries.Count} 条"
            : string.Empty;
        return $"已知等级 {knownLevel} 条 · 最高 Lv.{maximum}\n{listed}{suffix}";
    }

    private static string DescribeWeapons(
        IReadOnlyList<InventoryStateEntry> entries,
        IReadOnlyDictionary<string, DomainItemMetadata?> names)
    {
        string listed = string.Join(
            "、",
            entries.Take(NamedEntryLimit).Select(entry =>
            {
                string name = names.TryGetValue(entry.StableItemKey, out var item) &&
                    item is not null
                    ? item.Name
                    : entry.TypeId;
                string quantity = entry.Quantity is int count && count > 1
                    ? $" ×{count}"
                    : string.Empty;
                return $"{name}{quantity}";
            }));
        int total = entries.Sum(entry => entry.Quantity ?? 0);
        string suffix = entries.Count > NamedEntryLimit
            ? $" …共 {entries.Count} 条"
            : string.Empty;
        return $"装备武器 {total} 件\n{listed}{suffix}";
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

/// <summary>
/// Display-only projection for challenge history. It is shared by current MAUI
/// pages and the ongoing WinUI3 redesign so neither host duplicates wording.
/// </summary>
public sealed record ChallengeObservationCard(
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
            int stars = observation.SpiralAbyssFloors.Sum(floor => floor.Stars);
            string floors = string.Join("\n", observation.SpiralAbyssFloors.Select(floor =>
                $"第 {floor.Floor} 层 · {floor.Stars} 星  " +
                string.Join("  ", floor.Chambers.Select(chamber => $"{chamber.Chamber} 间 {chamber.Stars} 星"))));
            return new($"深境螺旋 · 第 {observation.SeasonId} 期 · {stars} 星", evidence, floors);
        }

        string mode = ModeName(observation.Mode);
        string schedule = string.IsNullOrWhiteSpace(scheduleName)
            ? $"第 {observation.SeasonId} 期"
            : $"{scheduleName} · 第 {observation.SeasonId} 期";
        string summary = observation.Mode == ChallengeMode.ImaginariumTheater
            ? $"难度 {observation.Difficulty?.ToString() ?? "未知"} · " +
              $"奖章 {observation.MedalCount?.ToString() ?? "未知"}"
            : $"最高难度 {observation.Difficulty?.ToString() ?? "未知"}";
        string stages = string.Join("\n", observation.Stages.Select(
            stage => DescribeStage(stage, observation.Mode)));
        return new($"{mode} · {schedule}", $"{evidence} · {summary}", stages);
    }

    public static string DescribeStage(
        ChallengeStageResult stage,
        ChallengeMode mode)
    {
        var parts = new List<string> { stage.StageId };
        if (stage.Score is int score)
        {
            parts.Add(mode == ChallengeMode.ImaginariumTheater
                ? score > 0 ? "已获奖章" : "未获奖章"
                : $"{score} 秒");
        }
        if (stage.CompletedAt.UtcStart is { } completed)
        {
            parts.Add($"完成于 {completed.ToLocalTime():g}");
        }
        if (stage.Participants.Count > 0)
        {
            parts.Add("阵容 " + string.Join(
                "、",
                stage.Participants.Select(participant =>
                    participant.Name is { } name
                        ? participant.Level is int level
                            ? $"{name}(Lv.{level})"
                            : name
                        : participant.Id)));
        }
        if (stage.Buffs.Count > 0)
        {
            parts.Add("增益 " + string.Join("、", stage.Buffs));
        }
        if (stage.Enemies.Count > 0)
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
        return string.Join(" · ", parts);
    }

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

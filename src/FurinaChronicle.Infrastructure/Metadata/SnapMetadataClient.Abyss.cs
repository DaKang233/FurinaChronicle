// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Metadata;

namespace FurinaChronicle.Infrastructure.Metadata;

public sealed partial class SnapMetadataClient
{
    private Dictionary<int, TowerLevelEntry[]>? abyssLevels;
    private Dictionary<long, MonsterEntry>? abyssMonsters;

    private async Task<ChallengeScheduleMetadata?> ResolveAbyssScheduleAsync(
        int seasonId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        abyssSchedules ??= (await ReadJsonAsync<TowerScheduleEntry[]>(
            "TowerSchedule.json", cancellationToken) ?? [])
            .Where(item => item.Id > 0).GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        if (!abyssSchedules.TryGetValue(seasonId, out TowerScheduleEntry? schedule)) return null;

        abyssFloors ??= (await ReadJsonAsync<TowerFloorEntry[]>("TowerFloor.json", cancellationToken) ?? [])
            .Where(item => item.Id > 0).GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        TowerFloorEntry[] selectedFloors = (schedule.FloorIds ?? [])
            .Distinct().Select(id => abyssFloors.GetValueOrDefault(id)).OfType<TowerFloorEntry>()
            .Where(floor => floor.Index > 0)
            .GroupBy(floor => floor.Index).Where(group => group.Count() == 1)
            .Select(group => group.Single()).OrderBy(floor => floor.Index).ToArray();
        var effects = selectedFloors.ToDictionary(floor => floor.Index,
            floor => (IReadOnlyList<string>)DescribeEffects(floor.Descriptions));
        var layouts = new List<AbyssFloorMetadata>();
        foreach (TowerFloorEntry floor in selectedFloors)
        {
            var chambers = new List<AbyssChamberMetadata>();
            if (floor.LevelGroupId is > 0)
            {
                abyssLevels ??= (await ReadJsonAsync<TowerLevelEntry[]>("TowerLevel.json", cancellationToken) ?? [])
                    .Where(level => level.GroupId > 0 && level.Index is >= 1 and <= 3)
                    .GroupBy(level => level.GroupId)
                    .ToDictionary(group => group.Key, group => group
                        .GroupBy(level => level.Index).Where(items => items.Count() == 1)
                        .Select(items => items.Single()).OrderBy(level => level.Index).ToArray());
                TowerLevelEntry[] levels = abyssLevels.GetValueOrDefault(floor.LevelGroupId.Value) ?? [];
                if (levels.Length > 0)
                {
                    abyssMonsters ??= (await ReadJsonAsync<MonsterEntry[]>("Monster.json", cancellationToken) ?? [])
                        .Where(monster => monster.DescribeId > 0)
                        .GroupBy(monster => monster.DescribeId)
                        .Where(group => group.Select(item => (item.Name, item.Icon)).Distinct().Count() == 1)
                        .ToDictionary(group => group.Key, group => group.First());
                    foreach (TowerLevelEntry level in levels)
                        chambers.Add(new(level.Index, level.MonsterLevel is >= 0 ? level.MonsterLevel : null,
                            MapHalf(level.FirstWaves, level.FirstMonsters, level.FirstGadget),
                            MapHalf(level.SecondWaves, level.SecondMonsters, level.SecondGadget)));
                }
            }
            layouts.Add(new(floor.Index, DescribeEffects(floor.FirstDescriptions),
                DescribeEffects(floor.SecondDescriptions), chambers));
        }
        string[] descriptions = DescribeEffects(schedule.Descriptions);
        var blessing = string.IsNullOrWhiteSpace(schedule.BuffName) && descriptions.Length == 0
            ? null : new ChallengeBlessingMetadata(CleanText(schedule.BuffName), descriptions);
        return new(ChallengeMode.SpiralAbyss, seasonId.ToString(CultureInfo.InvariantCulture),
            null, schedule.Open, schedule.Close, "Snap.Metadata", blessing, effects,
            Abyss: new(layouts));
    }

    private AbyssHalfMetadata MapHalf(TowerWaveEntry?[]? waves, long[]? legacy, DescriptionEntry? gadget)
    {
        ChallengeDescriptionMetadata? mechanism = gadget is null ? null :
            new(CleanText(gadget.Name), CleanText(gadget.Description));
        if (waves is not null)
            return new(waves.OfType<TowerWaveEntry>().Select(wave => new AbyssWaveMetadata(
                wave.Type, CleanText(wave.Description), (wave.Monsters ?? [])
                    .OfType<TowerMonsterEntry>().Where(monster => monster.Id > 0)
                    .Select(MapMonster).ToArray())).ToArray(), mechanism, true);
        if (legacy is not null)
            return new([new(null, null, legacy.Where(id => id > 0).Select(id =>
                MapMonster(new(id, null, null, null))).ToArray(), IsLegacy: true)], mechanism, true);
        return new([], mechanism, false);
    }

    private AbyssMonsterMetadata MapMonster(TowerMonsterEntry entry)
    {
        // Metadata display aliases from the upstream source. Original IDs remain unchanged.
        long normalized = entry.Id switch
        {
            5020 or 5021 => 502, 5040 or 5041 => 504, 5070 or 5071 => 507,
            5102 => 510, 5112 => 511, 30121 => 30111, 30123 => 30113, 30126 => 30116,
            30605 => 30603, 30606 => 30604, 40632 => 40613,
            60402 or 60403 or 60404 => 60401, 28021302 => 80213, _ => entry.Id,
        };
        MonsterEntry? monster = abyssMonsters?.GetValueOrDefault(entry.Id)
            ?? abyssMonsters?.GetValueOrDefault(normalized);
        string? icon = CleanIconName(monster?.Icon);
        return new(entry.Id.ToString(CultureInfo.InvariantCulture), CleanText(monster?.Name),
            icon is null ? null : new Uri(options.MonsterIconBaseAddress, icon + ".png").AbsoluteUri,
            entry.Count is >= 0 ? entry.Count : null, entry.AttackMonolith,
            (entry.Affixes ?? []).OfType<DescriptionEntry>()
                .Select(affix => new ChallengeDescriptionMetadata(CleanText(affix.Name), CleanText(affix.Description)))
                .Where(affix => affix.Name is not null || affix.Description is not null).ToArray());
    }

    private static string? CleanText(string? value) =>
        DescribeEffects([value]).FirstOrDefault();

    private static string? CleanIconName(string? icon) =>
        !string.IsNullOrWhiteSpace(icon) && icon.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? icon : null;

    private sealed record TowerFloorEntry(
        int Id, int Index, string?[]? Descriptions, int? LevelGroupId,
        string?[]? FirstDescriptions, string?[]? SecondDescriptions);
    private sealed record TowerLevelEntry(
        int Id, int GroupId, int Index, int? MonsterLevel,
        long[]? FirstMonsters, long[]? SecondMonsters,
        TowerWaveEntry?[]? FirstWaves, TowerWaveEntry?[]? SecondWaves,
        DescriptionEntry? FirstGadget, DescriptionEntry? SecondGadget);
    private sealed record TowerWaveEntry(int? Type, string? Description, TowerMonsterEntry?[]? Monsters);
    private sealed record TowerMonsterEntry(long Id, long? Count, bool? AttackMonolith, DescriptionEntry?[]? Affixes);
    private sealed record DescriptionEntry(string? Name, string? Description);
    private sealed record MonsterEntry(long Id, long DescribeId, string? Name, string? Icon);
}

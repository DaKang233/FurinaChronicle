// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

public sealed record CharacterStatView(int Id, string Name, string? Base, string? Bonus, string Total, string? Icon)
{
    public string? SignedBonus => string.IsNullOrWhiteSpace(Bonus) ? null :
        Bonus.StartsWith('+') || Bonus.StartsWith('-') ? Bonus : "+" + Bonus;
}

public static class CharacterDetailPresentation
{
    private static readonly JsonSerializerOptions ClipboardJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static IReadOnlyList<InventoryStateEntry> Characters(ItemStateObservation observation) =>
        observation.Entries.OrderByDescending(entry => entry.Level)
            .ThenBy(Name, StringComparer.CurrentCulture).ToArray();

    public static string Name(InventoryStateEntry entry) =>
        First(entry.CharacterDetail?.Name, entry.CharacterDetail?.Roster?.Name) ?? entry.TypeId;

    public static string? First(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public static string Level(int? level) => level is int number ? $"Lv. {number}" : "等级未知";

    public static string Ascension(int? stage) => stage is >= 0 and <= 6
        ? new string('✦', stage.Value) + new string('✧', 6 - stage.Value)
        : stage is int number ? $"突破 {number} 阶" : "突破未知";

    public static string Rarity(int? rarity) => rarity is > 0 and <= 5 ? new string('★', rarity.Value) : string.Empty;

    public static string StatName(int propertyId, CharacterResponseMetadata? metadata) =>
        First(Lookup(propertyId, metadata)?.Name) ?? propertyId switch
        {
            1 => "基础生命值", 2 => "生命值", 3 => "生命值",
            4 => "基础攻击力", 5 => "攻击力", 6 => "攻击力",
            7 => "基础防御力", 8 => "防御力", 9 => "防御力",
            20 => "暴击率", 22 => "暴击伤害", 23 => "元素充能效率",
            26 => "治疗加成", 27 => "受治疗加成", 28 => "元素精通",
            29 => "物理抗性", 30 => "物理伤害加成",
            40 => "火元素伤害加成", 41 => "雷元素伤害加成", 42 => "水元素伤害加成",
            43 => "草元素伤害加成", 44 => "风元素伤害加成", 45 => "岩元素伤害加成",
            46 => "冰元素伤害加成",
            50 => "火元素抗性", 51 => "雷元素抗性", 52 => "水元素抗性",
            53 => "草元素抗性", 54 => "风元素抗性", 55 => "岩元素抗性", 56 => "冰元素抗性",
            80 => "冷却缩减", 81 => "护盾强效",
            2000 => "生命值上限", 2001 => "攻击力", 2002 => "防御力",
            _ => $"属性 {propertyId}",
        };

    public static string? ElementIcon(string? element) => element?.ToLowerInvariant() switch
    {
        "pyro" or "fire" or "火" => "fire",
        "electro" or "electric" or "雷" => "electric",
        "hydro" or "water" or "水" => "water",
        "dendro" or "grass" or "草" => "grass",
        "anemo" or "wind" or "风" => "wind",
        "geo" or "rock" or "岩" => "rock",
        "cryo" or "ice" or "冰" => "ice",
        _ => null,
    };

    public static IReadOnlyList<CharacterStatView> Overview(CharacterDetail detail, CharacterResponseMetadata? metadata)
    {
        int elementProperty = ElementIcon(detail.Element) switch
        {
            "fire" => 40, "electric" => 41, "water" => 42, "grass" => 43,
            "wind" => 44, "rock" => 45, "ice" => 46, _ => 30,
        };
        int[] ids = [2000, 2001, 2002, 28, elementProperty, 20, 22, 26, 27, 23];
        var stats = AllGroups(detail).SelectMany(section => section.Stats)
            .GroupBy(stat => stat.PropertyId).ToDictionary(group => group.Key, group => group.First());
        return ids.Select(id => Project(stats.GetValueOrDefault(id) ?? new(id, null, null, null), metadata)).ToArray();
    }

    public static IEnumerable<(string Name, IReadOnlyList<CharacterStat> Stats)> AllGroups(CharacterDetail detail)
    {
        if (detail.BaseStats is { Count: > 0 } basic) yield return ("基础属性", basic);
        if (detail.AdvancedStats is { Count: > 0 } advanced) yield return ("进阶属性", advanced);
        if (detail.ElementalStats is { Count: > 0 } elemental) yield return ("元素属性", elemental);
        if (detail.SelectedStats is { Count: > 0 } selected)
        {
            var known = (detail.BaseStats ?? []).Concat(detail.AdvancedStats ?? [])
                .Concat(detail.ElementalStats ?? []).Select(stat => stat.PropertyId).ToHashSet();
            var remaining = selected.Where(stat => !known.Contains(stat.PropertyId)).ToArray();
            if (remaining.Length > 0) yield return ("其他属性", remaining);
        }
    }

    public static CharacterStatView Project(CharacterStat stat, CharacterResponseMetadata? metadata) =>
        new(stat.PropertyId, StatName(stat.PropertyId, metadata),
            stat.PropertyId is >= 2000 and <= 2002 ? First(stat.Base) : null,
            stat.PropertyId is >= 2000 and <= 2002 ? First(stat.Bonus) : null,
            First(stat.Total, stat.Base) ?? "—", Lookup(stat.PropertyId, metadata)?.Icon);

    public static string Export(ItemStateObservation observation, InventoryStateEntry? character = null) =>
        JsonSerializer.Serialize(new
        {
            observed_at = observation.Provenance.Timestamps.ObservedAt,
            source = observation.Provenance.Source?.Provider,
            properties = observation.CharacterMetadata?.Properties,
            characters = character is null ? Characters(observation) : [character],
        }, ClipboardJson);

    public static async Task<CharacterDetail?> WithMetadataAsync(InventoryStateEntry entry, IDomainMetadataService metadata,
        CancellationToken cancellationToken = default)
    {
        if (entry.CharacterDetail is not { } detail) return null;
        string? name = First(detail.Name, detail.Roster?.Name);
        string? icon = First(detail.Icon, detail.Roster?.Icon);
        int? rarity = detail.Rarity ?? detail.Roster?.Rarity;
        if (name is null || icon is null || rarity is null)
        {
            var avatar = await metadata.ResolveItemAsync(InventoryItemKind.Character, entry.TypeId, cancellationToken);
            name ??= avatar?.Name;
            icon ??= avatar?.IconUrl;
            rarity ??= avatar?.Rarity;
        }

        var weapon = detail.Weapon;
        if (weapon is { } equipped && (First(equipped.Name) is null || First(equipped.Icon) is null || equipped.Rarity is null))
        {
            var item = await metadata.ResolveItemAsync(InventoryItemKind.Weapon,
                equipped.Id.ToString(CultureInfo.InvariantCulture), cancellationToken);
            weapon = equipped with { Name = First(equipped.Name, item?.Name), Icon = First(equipped.Icon, item?.IconUrl),
                Rarity = equipped.Rarity ?? item?.Rarity };
        }
        var talents = detail.Talents ?? [];
        var constellations = detail.Constellations ?? [];
        var fallback = await metadata.ResolveCharacterMechanicsAsync(entry.TypeId, talents, cancellationToken);
        if (fallback is not null)
        {
            var metadataTalents = fallback.Talents.ToDictionary(item => item.Id);
            talents = talents.Select(item => metadataTalents.TryGetValue(item.Id, out var meta)
                ? item with { Name = First(item.Name, meta.Name), Icon = First(item.Icon, meta.Icon),
                    Description = First(item.Description, meta.Description), Attributes = item.Attributes is { Count: > 0 } ? item.Attributes : meta.Attributes }
                : item).Concat(fallback.Talents.Where(item => !talents.Any(observed => observed.Id == item.Id))).ToArray();
            var metadataConstellations = fallback.Constellations.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
            constellations = constellations.Select(item => metadataConstellations.TryGetValue(item.Id, out var meta)
                ? item with { Name = First(item.Name, meta.Name), Icon = First(item.Icon, meta.Icon),
                    Description = First(item.Description, meta.Description), Position = item.Position ?? meta.Position }
                : item).Concat(fallback.Constellations.Where(item => !constellations.Any(observed => observed.Id == item.Id ||
                    observed.Position is not null && observed.Position == item.Position))).ToArray();
        }
        return detail with { Name = name, Icon = icon, Rarity = rarity, Weapon = weapon, Talents = talents,
            Constellations = constellations.Select(item => item.Unlocked is null && item.Position is int slot && entry.Progression is int count
                ? item with { Unlocked = slot <= count } : item).ToArray() };
    }

    private static CharacterPropertyMetadata? Lookup(int id, CharacterResponseMetadata? metadata) =>
        metadata?.Properties?.GetValueOrDefault(id.ToString(CultureInfo.InvariantCulture));
}

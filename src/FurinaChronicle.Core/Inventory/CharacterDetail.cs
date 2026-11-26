// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Inventory;

/// <summary>Player state and the descriptive fields returned with one character.</summary>
public sealed record CharacterDetail(
    string? Name, string? Element, int? Rarity, int? Friendship, int? Ascension,
    string? Icon, string? SideIcon, string? Portrait, string? CardImage, bool? IsFeatured,
    int? WeaponType, CharacterWeaponDetail? Weapon,
    IReadOnlyList<CharacterConstellation>? Constellations,
    IReadOnlyList<CharacterTalent>? Talents,
    IReadOnlyList<CharacterStat>? SelectedStats,
    IReadOnlyList<CharacterStat>? BaseStats,
    IReadOnlyList<CharacterStat>? AdvancedStats,
    IReadOnlyList<CharacterStat>? ElementalStats,
    IReadOnlyList<CharacterArtifact>? Artifacts,
    IReadOnlyList<CharacterCostume>? Costumes,
    CharacterArtifactRecommendation? ArtifactRecommendation,
    bool? IsTpsUnlocked, CharacterWeaponSkin? WeaponSkin,
    CharacterWeaponDetail? BaseWeapon = null,
    CharacterRosterEntry? Roster = null);

public sealed record CharacterRosterEntry(
    int Level, int ConstellationCount, string? Name, string? Element, int? Rarity,
    int? Friendship, int? Ascension, int? WeaponType, string? Icon, string? SideIcon,
    string? Portrait, string? CardImage, bool? IsFeatured, CharacterWeaponDetail? Weapon);

public sealed record CharacterStat(int PropertyId, string? Base, string? Bonus, string? Total);
public sealed record CharacterPropertyMetadata(int PropertyId, string? Name, string? Icon, string? FilterName);

public sealed record CharacterWeaponDetail(
    int Id, string? Name, string? Icon, int? Type, string? TypeName,
    int? Rarity, int? Level, int? Refinement, int? Ascension,
    string? Description, CharacterStat? MainStat, CharacterStat? SecondaryStat);
public sealed record CharacterWeaponSkin(string? Name, string? Icon, int? Rarity);
public sealed record CharacterCostume(int Id, string? Name, string? Icon);
public sealed record CharacterConstellation(
    int Id, int? Position, string? Name, string? Icon, bool? Unlocked, string? Description);
public sealed record CharacterTalent(
    int Id, int? Type, int? Level, string? Name, string? Icon, bool? Unlocked,
    string? Description, IReadOnlyList<CharacterTalentValue>? Attributes);
public sealed record CharacterTalentValue(string? Name, string? Value);

public sealed record CharacterArtifact(
    int Id, int? Slot, string? SlotName, string? Name, string? Icon,
    int? Rarity, int? Level, CharacterArtifactSet? Set,
    CharacterArtifactStat? MainStat, IReadOnlyList<CharacterArtifactStat>? SubStats);
public sealed record CharacterArtifactStat(int PropertyId, string? Value, int? Hits);
public sealed record CharacterArtifactSet(int Id, string? Name, IReadOnlyList<CharacterSetBonus>? Bonuses);
public sealed record CharacterSetBonus(int? Pieces, string? Description);
public sealed record CharacterArtifactRecommendation(
    CharacterRecommendedStats? Recommended, CharacterRecommendedStats? Custom, bool? HasSetRecommendation);
public sealed record CharacterRecommendedStats(
    IReadOnlyList<int>? Sands, IReadOnlyList<int>? Goblet,
    IReadOnlyList<int>? Circlet, IReadOnlyList<int>? SubStats);

/// <summary>Official labels and reference data returned alongside the detailed entries.</summary>
public sealed record CharacterResponseMetadata(
    IReadOnlyDictionary<string, CharacterPropertyMetadata>? Properties,
    CharacterRecommendedStats? ArtifactPropertyOptions,
    IReadOnlyDictionary<string, string>? ArtifactWiki,
    IReadOnlyDictionary<string, string>? WeaponWiki,
    IReadOnlyDictionary<string, string>? AvatarWiki);

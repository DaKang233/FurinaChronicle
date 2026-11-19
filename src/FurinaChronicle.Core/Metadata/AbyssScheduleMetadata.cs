// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Metadata;

// Public season configuration, not a player's observed battle/kill/completion evidence.
// DescribeId preserves the source ID even when a display alias resolves a shared monster asset.
public sealed record AbyssScheduleMetadata(IReadOnlyList<AbyssFloorMetadata> Floors);
public sealed record AbyssFloorMetadata(
    int Floor, IReadOnlyList<string> UpperEffects, IReadOnlyList<string> LowerEffects,
    IReadOnlyList<AbyssChamberMetadata> Chambers);
public sealed record AbyssChamberMetadata(
    int Chamber, int? MonsterLevel, AbyssHalfMetadata Upper, AbyssHalfMetadata Lower);
public sealed record AbyssHalfMetadata(
    IReadOnlyList<AbyssWaveMetadata> Waves, ChallengeDescriptionMetadata? Gadget, bool HasEnemyData);
public sealed record AbyssWaveMetadata(
    int? Type, string? Description, IReadOnlyList<AbyssMonsterMetadata> Monsters, bool IsLegacy = false);
public sealed record AbyssMonsterMetadata(
    string DescribeId, string? Name, string? IconUrl, long? Count, bool? AttackMonolith,
    IReadOnlyList<ChallengeDescriptionMetadata> Affixes);
public sealed record ChallengeDescriptionMetadata(string? Name, string? Description);

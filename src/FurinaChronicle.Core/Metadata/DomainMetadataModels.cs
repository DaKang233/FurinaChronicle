// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;

namespace FurinaChronicle.Core.Metadata;

/// <summary>
/// Display metadata for an inventory type ID. It is intentionally separate
/// from observations: names, icons and rarity never become data identity.
/// </summary>
public sealed record DomainItemMetadata(
    InventoryItemKind Kind,
    string TypeId,
    string Name,
    int? Rarity,
    string? IconUrl,
    string Source);

/// <summary>
/// Optional display metadata matched to an official challenge season id.
/// </summary>
public sealed record ChallengeScheduleMetadata(
    ChallengeMode Mode,
    string ScheduleId,
    string? Name,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string Source,
    ChallengeBlessingMetadata? Blessing = null,
    IReadOnlyDictionary<int, IReadOnlyList<string>>? FloorEffects = null,
    TheaterScheduleMetadata? Theater = null,
    AbyssScheduleMetadata? Abyss = null);

public sealed record TheaterScheduleMetadata(
    IReadOnlyList<int> AllowedElements,
    IReadOnlyList<string> InitialAvatarIds,
    IReadOnlyList<string> SpecialAvatarIds);

public sealed record ChallengeBlessingMetadata(string? Name, IReadOnlyList<string> Descriptions);

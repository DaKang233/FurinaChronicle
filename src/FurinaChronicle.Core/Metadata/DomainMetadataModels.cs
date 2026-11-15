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
/// Schedule-level metadata for a challenge mode. Only modes with an official
/// schedule table can provide a name; the Abyss relies on its own season id.
/// </summary>
public sealed record ChallengeScheduleMetadata(
    ChallengeMode Mode,
    string ScheduleId,
    string? Name,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    string Source);

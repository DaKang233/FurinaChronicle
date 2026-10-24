// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Gacha.Metadata;

public sealed record GachaItemMetadata(
    GachaGame Game,
    string ItemId,
    string Name,
    string ItemType,
    int? RankType,
    string? IconUrl = null,
    IReadOnlyDictionary<string, string>? LocalizedNames = null);

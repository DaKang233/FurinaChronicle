// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;

namespace FurinaChronicle.Services.Abstractions;

/// <summary>
/// Resolves display metadata for domain type IDs and challenge schedules.
/// Implementations must never fail the surrounding history query: a missing
/// source degrades to null so the caller can fall back to raw IDs.
/// </summary>
public interface IDomainMetadataService
{
    ValueTask<DomainItemMetadata?> ResolveItemAsync(
        InventoryItemKind kind,
        string typeId,
        CancellationToken cancellationToken = default);

    ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(
        ChallengeMode mode,
        string seasonId,
        CancellationToken cancellationToken = default);
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;

namespace FurinaChronicle.Infrastructure.Metadata;

/// <summary>
/// Snap.Metadata is authoritative. When it is unavailable the cultivation
/// calculator metadata already shipped with the app supplies item names and
/// icons; a missing fallback degrades to the raw type id.
/// </summary>
public sealed class CompositeDomainMetadataService(
    SnapMetadataClient snap,
    IGachaItemMetadataProvider fallback) : IDomainMetadataService
{
    public async ValueTask<DomainItemMetadata?> ResolveItemAsync(
        InventoryItemKind kind,
        string typeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        DomainItemMetadata? primary = kind switch
        {
            InventoryItemKind.Character => await snap.ResolveAvatarAsync(
                typeId, cancellationToken),
            InventoryItemKind.Weapon => await snap.ResolveWeaponAsync(
                typeId, cancellationToken),
            _ => null,
        };
        if (primary is not null)
        {
            return primary;
        }

        GachaItemMetadata? item;
        try
        {
            item = await fallback.FindByIdAsync(GachaGame.GenshinImpact, typeId, cancellationToken);
        }
        catch (GachaMetadataUnavailableException)
        {
            return null;
        }
        return item is null
            ? null
            : new DomainItemMetadata(
                kind,
                typeId,
                item.Name,
                item.RankType,
                item.IconUrl,
                "GenshinCalculator");
    }

    public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(
        ChallengeMode mode,
        string seasonId,
        CancellationToken cancellationToken = default) =>
        new(snap.ResolveScheduleAsync(mode, seasonId, cancellationToken));
}

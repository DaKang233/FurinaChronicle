// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;

namespace FurinaChronicle.Presentation.Gacha;

/// <summary>Optional artwork lookup. It never changes a saved Gacha fact or its identity.</summary>
public static class GachaArtworkResolver
{
    public static async ValueTask<GachaImageReference> ResolveAsync(
        GachaImageReference image, string? itemName, IGachaItemMetadataProvider? metadata,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (metadata is null) return image;
        try
        {
            var item = !string.IsNullOrWhiteSpace(image.ResourceKey)
                ? await metadata.FindByIdAsync(GachaGame.GenshinImpact, image.ResourceKey, cancellationToken)
                : !string.IsNullOrWhiteSpace(itemName)
                    ? await metadata.FindByNameAsync(GachaGame.GenshinImpact, itemName, cancellationToken)
                    : null;
            if (item is null || item.Game != GachaGame.GenshinImpact ||
                (!string.IsNullOrWhiteSpace(image.ResourceKey) &&
                 !string.Equals(image.ResourceKey, item.ItemId, StringComparison.Ordinal))) return image;
            return image with { ResourceKey = item.ItemId, SourceUrl = image.SourceUrl ?? item.IconUrl };
        }
        catch (GachaMetadataUnavailableException)
        {
            return image;
        }
    }
}

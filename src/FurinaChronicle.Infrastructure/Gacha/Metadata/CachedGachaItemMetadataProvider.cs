// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

/// <summary>Reads available localization without refreshing or depending on network access.</summary>
public sealed class CachedGachaItemMetadataProvider(
    GachaMetadataDatabase database, GachaMetadataOptions options) : IGachaItemMetadataProvider
{
    public async ValueTask<GachaItemMetadata?> FindByIdAsync(
        GachaGame game, string itemId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        var (item, names) = await database.FindAsync(game, itemId.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return SqliteGachaItemMetadataProvider.CreateMetadata(game, item, names, options.PreferredLanguage);
    }

    public async ValueTask<GachaItemMetadata?> FindByNameAsync(
        GachaGame game, string itemName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemName);
        var (item, names) = await database.FindByNameAsync(game, itemName.Trim(), cancellationToken)
            .ConfigureAwait(false);
        return SqliteGachaItemMetadataProvider.CreateMetadata(game, item, names, options.PreferredLanguage);
    }
}

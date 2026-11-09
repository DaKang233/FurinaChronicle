// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaBannerImageCache
{
    Task<string?> GetCachedPathAsync(
        GachaGame game,
        string bannerId,
        CancellationToken cancellationToken = default);

    Task<string?> GetOrRefreshAsync(
        GachaGame game,
        string bannerId,
        string? sourceUrl,
        string? backupSourceUrl,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

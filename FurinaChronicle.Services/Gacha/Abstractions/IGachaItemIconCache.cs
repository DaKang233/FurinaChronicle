// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaItemIconCache
{
    Task<string?> GetOrRefreshAsync(
        GachaGame game,
        string itemId,
        string? sourceUrl,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

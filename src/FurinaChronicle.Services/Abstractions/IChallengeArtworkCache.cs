// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Abstractions;

/// <summary>Optional public artwork only. No player identity, credentials or facts enter this cache.</summary>
public interface IChallengeArtworkCache
{
    string? GetCachedPath(Uri? source);
    Task<string?> GetOrRefreshAsync(Uri? source, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
}

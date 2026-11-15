// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// Thin adapter over the production refresh use case. Only the ManualUrl source is reachable from
// the isolated WinUI host; SToken and web-cache paths stay behind their existing platform
// composition and are not wired here.
public sealed class ServiceGachaManualRefreshGateway(RefreshGachaRecords refresher)
    : IGachaManualRefreshGateway
{
    public Task<GachaRefreshIdentity> DiscoverIdentityAsync(
        string manualUrl,
        CancellationToken cancellationToken = default) =>
        refresher.DiscoverIdentityAsync(
            new GachaRefreshDiscoveryRequest(GachaRefreshSource.ManualUrl, ManualUrl: manualUrl),
            cancellationToken);

    public Task<GachaRefreshResult> RefreshAsync(
        GameAccount targetAccount,
        GachaImportMode mode,
        string manualUrl,
        CancellationToken cancellationToken = default) =>
        refresher.ExecuteAsync(
            new GachaRefreshRequest(
                targetAccount,
                GachaRefreshSource.ManualUrl,
                mode,
                ManualUrl: manualUrl),
            progress: null,
            cancellationToken);
}

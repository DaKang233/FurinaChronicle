// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// Adapter over the production refresh use case for the Windows web-cache source. The cache URL
// stays inside RefreshGachaRecords; the presentation layer only receives UID/result counts.
public sealed class ServiceGachaWindowsCacheRefreshGateway(
    RefreshGachaRecords refresher,
    IWindowsGachaCacheUrlProvider windowsCacheUrlProvider)
    : IGachaWindowsCacheRefreshGateway
{
    public bool IsSupported => windowsCacheUrlProvider.IsSupported;

    public Task<GachaRefreshIdentity> DiscoverIdentityAsync(
        string gameInstallationPath,
        GameServerRegion regionHint,
        CancellationToken cancellationToken = default) =>
        refresher.DiscoverIdentityAsync(
            new GachaRefreshDiscoveryRequest(
                GachaRefreshSource.WindowsWebCache,
                GameInstallationPath: gameInstallationPath,
                ServerRegionHint: regionHint),
            cancellationToken);

    public Task<GachaRefreshResult> RefreshAsync(
        GameAccount targetAccount,
        GachaImportMode mode,
        string gameInstallationPath,
        CancellationToken cancellationToken = default) =>
        refresher.ExecuteAsync(
            new GachaRefreshRequest(
                targetAccount,
                GachaRefreshSource.WindowsWebCache,
                mode,
                GameInstallationPath: gameInstallationPath),
            progress: null,
            cancellationToken);
}

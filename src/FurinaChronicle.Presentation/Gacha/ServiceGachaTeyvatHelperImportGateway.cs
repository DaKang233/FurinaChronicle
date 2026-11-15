// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

// Adapter over the production Teyvat Helper import source and client. The client is the only
// component that sends the UID/link to lelaer.com; nothing here persists the URL.
public sealed class ServiceGachaTeyvatHelperImportGateway(
    TeyvatHelperUigfImportSource importSource)
    : IGachaTeyvatHelperImportGateway
{
    public Task<TeyvatHelperImportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default) =>
        importSource.GetAvailabilityAsync(cancellationToken);

    public Task<TeyvatHelperUigfDownload> DownloadManuallyAsync(
        string uid,
        string gachaUrl,
        CancellationToken cancellationToken = default) =>
        importSource.DownloadManuallyAsync(uid, gachaUrl, cancellationToken);
}

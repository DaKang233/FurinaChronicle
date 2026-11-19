// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Services.Gacha.Refreshing;

public sealed record GachaUnselectedRefreshRequest(
    GachaRefreshSource Source, GachaImportMode Mode, Guid? ArchiveId,
    string? ManualUrl = null, string? GameInstallationPath = null);

public sealed record GachaUnselectedRefreshResult(
    PreparedGachaRefreshTarget Target, GachaRefreshResult Refresh)
{
    public bool ParentsCreated => Target.AccountToCreate is not null && Refresh.InsertedCount > 0;
}

// Only the absent-account path. Existing selected-account validation is kept by the host's
// coordinators. URL discovery is provisional; no creation occurs before the atomic refresh.
public sealed class RefreshUnselectedGachaRecords(
    PrepareGachaRefreshTarget targets, RefreshGachaRecords refresher)
{
    public async Task<GachaUnselectedRefreshResult> ExecuteAsync(
        GachaUnselectedRefreshRequest request, IProgress<GachaRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Source is not (GachaRefreshSource.ManualUrl or GachaRefreshSource.WindowsWebCache))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.ArchiveId == Guid.Empty) throw new ArgumentException("Invalid archive selection.");
        var identity = await refresher.DiscoverIdentityAsync(new(request.Source,
            request.ManualUrl, request.GameInstallationPath), cancellationToken);
        var target = await targets.ExecuteAsync(identity, request.ArchiveId, cancellationToken: cancellationToken);
        GachaRefreshResult result = await refresher.ExecuteAsync(new(target.Account, request.Source,
            request.Mode, ManualUrl: request.ManualUrl, GameInstallationPath: request.GameInstallationPath,
            ArchiveToCreate: target.ArchiveToCreate, AccountToCreate: target.AccountToCreate),
            progress, cancellationToken);
        return new(target, result);
    }
}

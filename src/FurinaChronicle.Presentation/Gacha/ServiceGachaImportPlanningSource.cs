// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Presentation.Gacha;

// Looks up archives and game-account copies through the production query services. Records are
// deliberately not queried here: preview only needs file-level counts and account identity.
public sealed class ServiceGachaImportPlanningSource(
    GetPlayerArchives archives,
    GetGameAccounts accounts) : IGachaImportPlanningSource
{
    public async Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlayerArchive> result = await archives.ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);
        return result.OrderBy(archive => archive.Name, StringComparer.Ordinal).ToArray();
    }

    public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default) =>
        accounts.ExecuteAsync(playerArchiveId, cancellationToken);
}

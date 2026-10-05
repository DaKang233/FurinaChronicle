// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class GetUnresolvedGameAccounts(
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository)
{
    public async Task<IReadOnlyList<UnresolvedGameAccountDiagnostic>>
        ExecuteAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlayerArchive> archives =
            await archiveRepository.GetAllAsync(cancellationToken);
        Dictionary<Guid, PlayerArchive> archivesById =
            archives.ToDictionary(archive => archive.Id);
        IReadOnlyList<GameAccount> accounts =
            await accountRepository.GetUnresolvedAsync(cancellationToken);

        var diagnostics = new List<UnresolvedGameAccountDiagnostic>(
            accounts.Count);
        foreach (GameAccount account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!archivesById.TryGetValue(
                    account.PlayerArchiveId,
                    out PlayerArchive? archive))
            {
                throw new InvalidDataException(
                    $"Unresolved game account {account.Id} references a missing archive.");
            }

            diagnostics.Add(new UnresolvedGameAccountDiagnostic(
                account.Id,
                account.PlayerArchiveId,
                archive.Name,
                account.Uid,
                account.ServerRegion,
                Classify(account)));
        }

        return diagnostics
            .OrderBy(item => item.PlayerArchiveName, StringComparer.Ordinal)
            .ThenBy(item => item.Uid, StringComparer.Ordinal)
            .ThenBy(item => item.GameAccountId)
            .ToArray();
    }

    private static UnresolvedGameAccountReason Classify(GameAccount account)
    {
        if (account.IsPlaceholder)
        {
            return UnresolvedGameAccountReason.PlaceholderAccount;
        }

        if (!GameUidValidation.IsStructurallyValidUid(account.Uid))
        {
            return UnresolvedGameAccountReason.InvalidUid;
        }

        GameServerRegion inferred =
            GameServerRegionResolver.Resolve(account.Uid);
        if (inferred != GameServerRegion.Unknown &&
            account.ServerRegion != inferred)
        {
            return UnresolvedGameAccountReason.UidRegionMismatch;
        }

        if (!Enum.IsDefined(account.ServerRegion) ||
            account.ServerRegion == GameServerRegion.Unknown)
        {
            return UnresolvedGameAccountReason.UnknownServerRegion;
        }

        return UnresolvedGameAccountReason.MissingRoleIdentity;
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Presentation.Management;

// Adapter from the archive management page model to the production archive/account use cases.
// It intentionally adds no normalization: names and UID text are passed through unchanged so
// the services remain the single authority for exact-name and UID rules.
public sealed class ServiceArchiveManagementSource(
    GetPlayerArchives getArchives,
    GetGameAccounts getAccounts,
    GetUnresolvedGameAccounts getUnresolved,
    CreatePlayerArchive createArchive,
    RenamePlayerArchive renameArchive,
    DeletePlayerArchive deleteArchive,
    AddGameAccount addAccount,
    UpdateGameAccount updateAccount,
    DeleteGameAccount deleteAccount) : IArchiveManagementSource
{
    public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
        CancellationToken cancellationToken = default) =>
        getArchives.ExecuteAsync(cancellationToken);

    public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default) =>
        getAccounts.ExecuteAsync(playerArchiveId, cancellationToken);

    public Task<IReadOnlyList<UnresolvedGameAccountDiagnostic>> GetUnresolvedAsync(
        CancellationToken cancellationToken = default) =>
        getUnresolved.ExecuteAsync(cancellationToken);

    public Task<PlayerArchive> CreateArchiveAsync(
        string name,
        CancellationToken cancellationToken = default) =>
        createArchive.ExecuteAsync(name, cancellationToken);

    public Task<PlayerArchive> RenameArchiveAsync(
        Guid archiveId,
        string name,
        CancellationToken cancellationToken = default) =>
        renameArchive.ExecuteAsync(archiveId, name, cancellationToken);

    public Task DeleteArchiveAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default) =>
        deleteArchive.ExecuteAsync(archiveId, cancellationToken);

    public Task<GameAccount> AddAccountAsync(
        Guid playerArchiveId,
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default) =>
        addAccount.ExecuteAsync(playerArchiveId, uid, serverRegion, displayName, cancellationToken);

    public Task<GameAccount> UpdateAccountAsync(
        Guid gameAccountId,
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default) =>
        updateAccount.ExecuteAsync(gameAccountId, uid, serverRegion, displayName, cancellationToken);

    public Task DeleteAccountAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default) =>
        deleteAccount.ExecuteAsync(gameAccountId, cancellationToken);
}

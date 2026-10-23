// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private static PreparedArchiveParents PrepareArchiveParents(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request)
    {
        Dictionary<Guid, ArchiveCatalogArchive> sourceArchives = request.Package
            .Catalog.Archives.ToDictionary(item => item.ArchiveReference);
        Dictionary<Guid, ArchiveCatalogAccount> sourceAccounts = request.Package
            .Catalog.Archives.SelectMany(item => item.Accounts)
            .ToDictionary(item => item.AccountReference);
        var archivesToInsert = new List<PlayerArchiveRow>();
        var identitiesToInsert = new Dictionary<Guid, GameRoleIdentityRow>();
        var accountsToInsert = new List<GameAccountRow>();
        var aliases = new List<(Guid Source, Guid Target)>();
        var newUndoSettings = new List<(Guid ArchiveId, int UndoLimit)>();
        var targetArchives = new Dictionary<Guid, Guid>();
        bool requireUniqueNames = connection.ExecuteScalar<int>(
            "SELECT RequireUniqueArchiveNames FROM PortableApplicationSettings WHERE Id = 1;") != 0;

        foreach (ArchiveRestoreArchiveMapping mapping in request.Plan.Archives)
        {
            Guid targetId = mapping.TargetArchiveId ??
                mapping.ProposedArchiveId ??
                throw new ArchiveRestoreApplyConflictException(
                    "The Archive mapping has no target ID.");
            targetArchives.Add(mapping.SourceArchiveReference, targetId);
            if (!sourceArchives.TryGetValue(
                    mapping.SourceArchiveReference,
                    out ArchiveCatalogArchive? source))
            {
                throw new ArchiveRestoreApplyConflictException(
                    "The RestorePlan references an Archive absent from staging.");
            }
            if (mapping.Kind == ArchiveRestoreArchiveMappingKind.CreateNew)
            {
                if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM PlayerArchives WHERE Id = ?;",
                        targetId.ToString("D")) != 0 ||
                    requireUniqueNames && connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM PlayerArchives WHERE Name = ?;",
                        mapping.ProposedName) != 0)
                {
                    throw new ArchiveRestoreApplyConflictException(
                        "A proposed Archive ID or unique name is no longer available.");
                }
                archivesToInsert.Add(new PlayerArchiveRow
                {
                    Id = targetId.ToString("D"),
                    Name = mapping.ProposedName,
                    CreatedAtUtcTicks = source.CreatedAt.UtcDateTime.Ticks,
                    UpdatedAtUtcTicks = source.UpdatedAt.UtcDateTime.Ticks,
                });
                int undoLimit = request.Package.Settings.Archives.Single(item =>
                    item.ArchiveReference == mapping.SourceArchiveReference).UndoLimit;
                newUndoSettings.Add((targetId, undoLimit));
            }
            else
            {
                PortableArchiveStateRow? current = connection
                    .Query<PortableArchiveStateRow>(
                        "SELECT Id, Name, UpdatedAtUtcTicks FROM PlayerArchives WHERE Id = ?;",
                        targetId.ToString("D"))
                    .SingleOrDefault();
                if (current is null || current.Name != mapping.ProposedName ||
                    mapping.TargetUpdatedAt is null ||
                    current.UpdatedAtUtcTicks !=
                        mapping.TargetUpdatedAt.Value.UtcDateTime.Ticks)
                {
                    throw new ArchiveRestoreApplyConflictException(
                        "A mapped target Archive changed after preview.");
                }
            }
        }

        var targetAccounts = new Dictionary<Guid, Guid>();
        var targetRoleIds = new Dictionary<Guid, Guid?>();
        foreach (ArchiveRestoreAccountMapping mapping in request.Plan.Accounts)
        {
            Guid targetArchiveId = mapping.TargetArchiveId ??
                throw new ArchiveRestoreApplyConflictException(
                    "The account mapping has no target Archive.");
            Guid targetAccountId = mapping.TargetAccountId ??
                mapping.ProposedAccountId ??
                throw new ArchiveRestoreApplyConflictException(
                    "The account mapping has no target account.");
            targetAccounts.Add(mapping.SourceAccountReference, targetAccountId);
            targetRoleIds.Add(
                mapping.SourceAccountReference,
                mapping.TargetRoleIdentityId);
            if (!sourceAccounts.TryGetValue(
                    mapping.SourceAccountReference,
                    out ArchiveCatalogAccount? source))
            {
                throw new ArchiveRestoreApplyConflictException(
                    "The account mapping references an account absent from staging.");
            }
            PrepareArchiveRoleIdentity(
                connection,
                mapping,
                identitiesToInsert);

            if (mapping.Kind == ArchiveRestoreAccountMappingKind.CreateNew)
            {
                if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM GameAccounts WHERE Id = ?;",
                        targetAccountId.ToString("D")) != 0)
                {
                    throw new ArchiveRestoreApplyConflictException(
                        "A proposed account ID is no longer available.");
                }
                accountsToInsert.Add(new GameAccountRow
                {
                    Id = targetAccountId.ToString("D"),
                    PlayerArchiveId = targetArchiveId.ToString("D"),
                    GameRoleIdentityId = mapping.TargetRoleIdentityId?
                        .ToString("D"),
                    Uid = source.LegacyUid,
                    ServerRegion = (int)source.LegacyServer,
                    DisplayName = source.DisplayName,
                    IsPlaceholder = source.IsPlaceholder,
                    CreatedAtUtcTicks = source.CreatedAt.UtcDateTime.Ticks,
                    UpdatedAtUtcTicks = source.UpdatedAt.UtcDateTime.Ticks,
                });
            }
            else
            {
                PortableAccountStateRow? current = connection
                    .Query<PortableAccountStateRow>(
                        "SELECT Id, PlayerArchiveId, GameRoleIdentityId, UpdatedAtUtcTicks FROM GameAccounts WHERE Id = ?;",
                        targetAccountId.ToString("D"))
                    .SingleOrDefault();
                if (current is null ||
                    current.PlayerArchiveId != targetArchiveId.ToString("D") ||
                    !string.Equals(
                        current.GameRoleIdentityId,
                        mapping.TargetRoleIdentityId?.ToString("D"),
                        StringComparison.Ordinal))
                {
                    throw new ArchiveRestoreApplyConflictException(
                        "A mapped target account changed after preview.");
                }
            }
            PrepareArchiveRoleAlias(connection, mapping, aliases);
        }

        return new PreparedArchiveParents(
            targetArchives,
            targetAccounts,
            targetRoleIds,
            archivesToInsert,
            identitiesToInsert.Values.ToArray(),
            accountsToInsert,
            aliases,
            newUndoSettings);
    }

    private static void PrepareArchiveRoleIdentity(
        SQLiteConnection connection,
        ArchiveRestoreAccountMapping mapping,
        IDictionary<Guid, GameRoleIdentityRow> identitiesToInsert)
    {
        if (mapping.TargetRoleIdentityId is not Guid targetRoleId ||
            mapping.NaturalIdentity is not GameRoleNaturalIdentity natural)
        {
            return;
        }
        PortableIdentityStateRow? existing = connection
            .Query<PortableIdentityStateRow>(
                "SELECT Id, GameBiz, Server, Uid FROM GameRoleIdentities WHERE Id = ?;",
                targetRoleId.ToString("D"))
            .SingleOrDefault();
        if (existing is null)
        {
            identitiesToInsert.TryAdd(
                targetRoleId,
                GameRoleIdentityRow.FromDomain(new GameRoleIdentity(
                    new GameRoleIdentityId(targetRoleId),
                    natural)));
        }
        else if (existing.ToNaturalIdentity() != natural)
        {
            throw new ArchiveRestoreApplyConflictException(
                "A target role identity changed after preview.");
        }
    }

    private static void PrepareArchiveRoleAlias(
        SQLiteConnection connection,
        ArchiveRestoreAccountMapping mapping,
        ICollection<(Guid Source, Guid Target)> aliases)
    {
        if (mapping.IdentityKind !=
                ArchiveRestoreIdentityMappingKind.AliasSourceToTarget ||
            mapping.SourceRoleIdentityId is not Guid sourceRoleId ||
            mapping.TargetRoleIdentityId is not Guid targetRoleId)
        {
            return;
        }
        string? existingTarget = connection.ExecuteScalar<string?>(
            "SELECT TargetIdentityId FROM RoleIdentityAliases WHERE SourceIdentityId = ?;",
            sourceRoleId.ToString("D"));
        if (existingTarget is null)
        {
            aliases.Add((sourceRoleId, targetRoleId));
        }
        else if (existingTarget != targetRoleId.ToString("D"))
        {
            throw new ArchiveRestoreApplyConflictException(
                "A role identity alias changed after preview.");
        }
    }
}

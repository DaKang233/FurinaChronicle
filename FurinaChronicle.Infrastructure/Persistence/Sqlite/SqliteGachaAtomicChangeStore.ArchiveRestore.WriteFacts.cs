// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private static void WriteArchiveRestoreParents(
        SQLiteConnection connection,
        PreparedArchiveRestore prepared)
    {
        foreach (PlayerArchiveRow archive in prepared.ArchivesToInsert)
        {
            connection.Insert(archive);
        }
        foreach (GameRoleIdentityRow identity in prepared.IdentitiesToInsert)
        {
            connection.Insert(identity);
        }
        foreach (GameAccountRow account in prepared.AccountsToInsert)
        {
            connection.Insert(account);
        }
        foreach ((Guid ArchiveId, int UndoLimit) setting in
                 prepared.NewArchiveUndoSettings)
        {
            connection.Execute(
                "INSERT INTO ArchiveUndoSettings (ArchiveId, UndoLimit) VALUES (?, ?);",
                setting.ArchiveId.ToString("D"),
                setting.UndoLimit);
        }
    }

    private static void WriteArchiveRestoreAliases(
        SQLiteConnection connection,
        Guid changeSetId,
        IEnumerable<(Guid Source, Guid Target)> aliases)
    {
        foreach ((Guid source, Guid target) in aliases)
        {
            connection.Execute(
                """
                INSERT INTO RoleIdentityAliases
                    (SourceIdentityId, TargetIdentityId, CreatedByChangeSetId)
                VALUES (?, ?, ?);
                """,
                source.ToString("D"),
                target.ToString("D"),
                changeSetId.ToString("D"));
        }
    }

    private long WriteArchiveRestoreFacts(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        PreparedArchiveRestore prepared,
        Guid changeSetId)
    {
        long materialBytes = 0;
        foreach (PreparedMutation mutation in prepared.GachaMutations)
        {
            ApplyBusinessMutation(connection, mutation);
            InsertGachaRestoreEntityChange(
                connection,
                changeSetId,
                request.CommittedAt,
                mutation,
                !prepared.GachaFactsWithSourceRevision.Contains((
                    mutation.Reference.GameAccountId,
                    mutation.Reference.ExternalRecordId)));
            if (prepared.UndoEligible)
            {
                materialBytes += InsertUndoMaterial(
                    connection,
                    changeSetId,
                    mutation);
            }
        }
        archiveRestoreFailureInjector.ThrowIfRequested(
            ArchiveRestoreFailurePoint.AfterGacha);
        materialBytes += WriteArchiveDomainFacts(
            connection,
            request,
            prepared.ItemMutations,
            changeSetId,
            prepared.UndoEligible,
            ArchiveRestoreFailurePoint.AfterItemObservations);
        materialBytes += WriteArchiveDomainFacts(
            connection,
            request,
            prepared.ManualMutations,
            changeSetId,
            prepared.UndoEligible,
            ArchiveRestoreFailurePoint.AfterManualAcquisitions,
            prepared.ManualFactsWithSourceRevision);
        materialBytes += WriteArchiveDomainFacts(
            connection,
            request,
            prepared.ChallengeMutations,
            changeSetId,
            prepared.UndoEligible,
            ArchiveRestoreFailurePoint.AfterChallenges);
        return materialBytes;
    }

    private long WriteArchiveDomainFacts(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        IReadOnlyList<PreparedDomainMutation> mutations,
        Guid changeSetId,
        bool undoEligible,
        ArchiveRestoreFailurePoint failurePoint,
        IReadOnlySet<(Guid AccountId, string EntityId)>? factsWithSourceRevision = null)
    {
        long materialBytes = 0;
        foreach (PreparedDomainMutation mutation in mutations)
        {
            ApplyDomainMutation(connection, mutation);
            ArchiveDomainFactRow identity = mutation.After ?? mutation.Before!;
            bool insertRevision = factsWithSourceRevision is null ||
                !factsWithSourceRevision.Contains((
                    ParseRequiredGuid(identity.GameAccountId, "game account"),
                    identity.EntityId));
            InsertDomainEntityChangeAndRevision(
                connection,
                changeSetId,
                request.CommittedAt,
                mutation,
                insertRevision);
            if (undoEligible)
            {
                materialBytes += InsertDomainUndoMaterial(
                    connection,
                    changeSetId,
                    mutation);
            }
        }
        archiveRestoreFailureInjector.ThrowIfRequested(failurePoint);
        return materialBytes;
    }

    private static void InsertGachaRestoreEntityChange(
        SQLiteConnection connection,
        Guid changeSetId,
        DateTimeOffset committedAt,
        PreparedMutation mutation,
        bool insertRevision)
    {
        connection.Execute(
            """
            INSERT INTO EntityChanges
                (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                 BeforeVersion, AfterVersion)
            VALUES (?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            EntityKind,
            mutation.Reference.ToString(),
            (int)mutation.EntityChangeKind,
            mutation.Before?.Version,
            mutation.After?.Version);
        if (!insertRevision)
        {
            return;
        }
        connection.Execute(
            """
            INSERT INTO GachaRevisions
                (RevisionId, ChangeSetId, GameAccountId, ExternalRecordId,
                 ChangeKind, BeforeVersion, AfterVersion,
                 SnapshotFormatVersion, BeforeSnapshotJson, AfterSnapshotJson,
                 CreatedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            Guid.NewGuid().ToString("D"),
            changeSetId.ToString("D"),
            mutation.Reference.GameAccountId.ToString("D"),
            mutation.Reference.ExternalRecordId,
            (int)mutation.EntityChangeKind,
            mutation.Before?.Version,
            mutation.After?.Version,
            SnapshotFormatVersion,
            SerializeSnapshot(mutation.Before),
            SerializeSnapshot(mutation.After),
            committedAt.UtcDateTime.Ticks);
    }

    private static void InsertSourceRevisions(
        SQLiteConnection connection,
        Guid changeSetId,
        IReadOnlyList<MaterializedArchiveFact> revisions)
    {
        foreach (MaterializedArchiveFact item in revisions)
        {
            if (item.Value is GachaRevisionItem gacha)
            {
                InsertSourceGachaRevision(
                    connection,
                    changeSetId,
                    item.TargetAccountId,
                    gacha);
            }
            else if (item.Value is ManualAcquisitionRevisionItem manual)
            {
                InsertSourceManualRevision(
                    connection,
                    changeSetId,
                    item.TargetAccountId,
                    item.Plan.TargetArchiveId,
                    manual);
            }
        }
    }

    private static void InsertSourceGachaRevision(
        SQLiteConnection connection,
        Guid changeSetId,
        Guid targetAccountId,
        GachaRevisionItem revision)
    {
        GachaRecordRow? before = MapGachaRevisionSnapshot(
            revision.Before,
            targetAccountId,
            revision.BeforeVersion);
        GachaRecordRow? after = MapGachaRevisionSnapshot(
            revision.After,
            targetAccountId,
            revision.AfterVersion);
        connection.Execute(
            """
            INSERT INTO GachaRevisions
                (RevisionId, ChangeSetId, GameAccountId, ExternalRecordId,
                 ChangeKind, BeforeVersion, AfterVersion,
                 SnapshotFormatVersion, BeforeSnapshotJson,
                 AfterSnapshotJson, CreatedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            revision.RevisionId.ToString("D"),
            changeSetId.ToString("D"),
            targetAccountId.ToString("D"),
            revision.Reference.ExternalRecordId,
            (int)revision.ChangeKind,
            revision.BeforeVersion?.Value,
            revision.AfterVersion?.Value,
            SnapshotFormatVersion,
            SerializeSnapshot(before),
            SerializeSnapshot(after),
            revision.CreatedAt.UtcDateTime.Ticks);
    }

    private static void InsertSourceManualRevision(
        SQLiteConnection connection,
        Guid changeSetId,
        Guid targetAccountId,
        Guid targetArchiveId,
        ManualAcquisitionRevisionItem revision)
    {
        ArchiveDomainFactRow? before = MapManualRevisionSnapshot(
            revision.Before,
            targetAccountId,
            targetArchiveId,
            revision.BeforeVersion);
        ArchiveDomainFactRow? after = MapManualRevisionSnapshot(
            revision.After,
            targetAccountId,
            targetArchiveId,
            revision.AfterVersion);
        ArchiveDomainFactRow identity = after ?? before ??
            throw new InvalidDataException(
                "A source manual Revision has no fact snapshot.");
        connection.Execute(
            """
            INSERT INTO ArchiveDomainRevisions
                (RevisionId, ChangeSetId, DomainKind, EntityId,
                 GameAccountId, ChangeKind, BeforeVersion, AfterVersion,
                 SnapshotFormatVersion, BeforeSnapshotJson,
                 AfterSnapshotJson, CreatedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            revision.RevisionId.ToString("D"),
            changeSetId.ToString("D"),
            ManualAcquisitionEntityKind,
            identity.EntityId,
            targetAccountId.ToString("D"),
            (int)revision.ChangeKind,
            revision.BeforeVersion?.Value,
            revision.AfterVersion?.Value,
            DomainSnapshotFormatVersion,
            before is null ? null : SerializeDomainSnapshot(before),
            after is null ? null : SerializeDomainSnapshot(after),
            revision.CreatedAt.UtcDateTime.Ticks);
    }

    private static GachaRecordRow? MapGachaRevisionSnapshot(
        GachaRecord? source,
        Guid targetAccountId,
        FactVersion? version)
    {
        if (source is null)
        {
            return null;
        }
        if (version is null)
        {
            throw new InvalidDataException(
                "A Gacha Revision snapshot has no source version.");
        }
        GachaRecordRow row = GachaRecordRow.FromDomain(source with
        {
            GameAccountId = targetAccountId,
        });
        row.Version = version.Value.Value;
        return row;
    }

    private static ArchiveDomainFactRow? MapManualRevisionSnapshot(
        ManualAcquisition? source,
        Guid targetAccountId,
        Guid targetArchiveId,
        FactVersion? version)
    {
        if (source is null)
        {
            return null;
        }
        if (version is null)
        {
            throw new InvalidDataException(
                "A manual acquisition Revision snapshot has no source version.");
        }
        return CreatePortableAcquisitionRow(
            MapAcquisition(source, targetAccountId),
            targetArchiveId,
            version.Value.Value);
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGachaAtomicChangeStore
    : IGachaAtomicChangeStore,
      IGachaPortableImportApplier
{
    internal const int SnapshotFormatVersion = 1;
    internal const string EntityKind = "gacha_record";
    private const long FixedWriteReserveBytes = 8192;
    private const long PerMutationWriteReserveBytes = 1024;

    private readonly FurinaDatabase database;
    private readonly IHistoryStorageCapacityProvider capacityProvider;

    public SqliteGachaAtomicChangeStore(FurinaDatabase database)
        : this(database, UnknownStorageCapacityProvider.Instance)
    {
    }

    public SqliteGachaAtomicChangeStore(
        FurinaDatabase database,
        IHistoryStorageCapacityProvider capacityProvider)
    {
        this.database =
            database ?? throw new ArgumentNullException(nameof(database));
        this.capacityProvider =
            capacityProvider ??
            throw new ArgumentNullException(nameof(capacityProvider));
    }

    public async Task<GachaFactState?> GetCurrentAsync(
        GachaFactReference reference,
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GachaFactReadRow> rows =
            await database.Connection.QueryAsync<GachaFactReadRow>(
                """
                SELECT g.*, a.PlayerArchiveId AS ArchiveId
                FROM GachaRecords g
                INNER JOIN GameAccounts a ON a.Id = g.GameAccountId
                WHERE g.GameAccountId = ? AND g.ExternalRecordId = ?;
                """,
                reference.GameAccountId.ToString("D"),
                reference.ExternalRecordId);
        GachaFactReadRow? row = rows.SingleOrDefault();
        return row is null
            ? null
            : new GachaFactState(
                row.ToGachaRow().ToDomain(),
                new FactVersion(row.Version),
                ParseRequiredGuid(row.ArchiveId, "archive"));
    }

    public async Task<IReadOnlyList<TombstoneReintroductionWarning>>
        FindActiveTombstonesAsync(
            IReadOnlyCollection<GachaFactReference> references,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(references);
        await database.InitializeAsync(cancellationToken);
        var warnings = new List<TombstoneReintroductionWarning>();
        await database.Connection.RunInTransactionAsync(connection =>
        {
            foreach (GachaFactReference reference in references.Distinct())
            {
                cancellationToken.ThrowIfCancellationRequested();
                Guid archiveId = ReadArchiveId(
                    connection,
                    reference.GameAccountId);
                ActiveTombstone? tombstone = ReadActiveTombstone(
                    connection,
                    archiveId,
                    reference);
                if (tombstone is not null)
                {
                    warnings.Add(new TombstoneReintroductionWarning(
                        tombstone.TombstoneKey,
                        tombstone.TombstoneVersion,
                        reference,
                        archiveId));
                }
            }
        });

        return warnings;
    }

    public async Task<GachaAtomicChangeResult> CommitAsync(
        GachaAtomicChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        GachaAtomicChangeResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            CommitResultRow? committed = connection
                .Query<CommitResultRow>(
                    """
                    SELECT OperationId, ChangeSetId, Status,
                           AffectedRecordCount, CommittedAtUtcTicks
                    FROM OperationCommitResults
                    WHERE OperationId = ?;
                    """,
                    request.OperationId.ToString())
                .SingleOrDefault();
            if (committed is not null)
            {
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.AlreadyCommitted,
                    ParseOptionalGuid(committed.ChangeSetId),
                    committed.AffectedRecordCount,
                    OriginalStatus: (ChangeExecutionStatus)committed.Status);
                return;
            }

            var prepared = new List<PreparedMutation>(
                request.Mutations.Count);
            foreach (GachaFactMutation mutation in request.Mutations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GachaFactReadRow? current = ReadCurrent(
                    connection,
                    mutation.Reference);
                if (mutation.ExpectedVersion is FactVersion expected)
                {
                    if (current is null || current.Version != expected.Value)
                    {
                        result = new GachaAtomicChangeResult(
                            ChangeExecutionStatus.Conflict,
                            ChangeSetId: null,
                            AffectedRecordCount: 0,
                            $"Gacha fact {mutation.Reference} no longer has expected version {expected.Value}.");
                        return;
                    }
                }
                else if (current is not null)
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        $"Gacha fact {mutation.Reference} already exists; an expected version is required to change it.");
                    return;
                }

                Guid archiveId = current is null
                    ? ReadArchiveId(
                        connection,
                        mutation.Reference.GameAccountId)
                    : ParseRequiredGuid(current.ArchiveId, "archive");
                PreparedMutation? item = PrepareMutation(
                    connection,
                    mutation,
                    current,
                    archiveId);
                if (item is not null)
                {
                    prepared.Add(item);
                }
            }

            if (result?.Status == ChangeExecutionStatus.Conflict)
            {
                return;
            }

            var confirmedReintroductions =
                new List<ActiveTombstone>();
            foreach (PreparedMutation mutation in prepared.Where(
                         item => item.EntityChangeKind ==
                             EntityChangeKind.Insert))
            {
                ActiveTombstone? tombstone = ReadActiveTombstone(
                    connection,
                    mutation.ArchiveId,
                    mutation.Reference);
                if (tombstone is null)
                {
                    continue;
                }

                bool confirmed = request.ReintroductionConfirmations.Any(
                    confirmation =>
                        confirmation.TombstoneKey ==
                            tombstone.TombstoneKey &&
                        confirmation.TombstoneVersion ==
                            tombstone.TombstoneVersion &&
                        confirmation.Reference == mutation.Reference);
                if (!confirmed)
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Suppressed,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        ConflictReason:
                            "The Gacha fact was irreversibly deleted on this device.",
                        ReintroductionWarning:
                            new TombstoneReintroductionWarning(
                                tombstone.TombstoneKey,
                                tombstone.TombstoneVersion,
                                mutation.Reference,
                                mutation.ArchiveId));
                    return;
                }

                confirmedReintroductions.Add(tombstone);
            }

            if (prepared.Count == 0)
            {
                InsertCommitResult(
                    connection,
                    request,
                    changeSetId: null,
                    ChangeExecutionStatus.NoOp,
                    affectedRecordCount: 0);
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.NoOp,
                    ChangeSetId: null,
                    AffectedRecordCount: 0);
                return;
            }

            PreparedMutation[] substantive = prepared
                .Where(mutation =>
                    mutation.ChangeKind !=
                    GachaRecordChangeKind.AcquisitionMetadataOnly)
                .ToArray();
            if (substantive.Length > 0)
            {
                HistoryCleanupPlan? cleanupPlan = BuildCleanupPlan(
                    connection,
                    request.OperationId,
                    request.CaptureUndo,
                    substantive);
                if (cleanupPlan is not null)
                {
                    if (cleanupPlan.Candidates.Count == 0 ||
                        cleanupPlan.AvailableBytes +
                        cleanupPlan.ReclaimableBytes <
                        cleanupPlan.RequiredBytes)
                    {
                        throw new IOException(
                            "There is not enough storage for the atomic Gacha change, even after eligible undo material cleanup.");
                    }
                    if (request.CleanupConfirmationId !=
                        cleanupPlan.ConfirmationId)
                    {
                        result = new GachaAtomicChangeResult(
                            ChangeExecutionStatus.NeedsConfirmation,
                            ChangeSetId: null,
                            AffectedRecordCount: 0,
                            CleanupPlan: cleanupPlan);
                        return;
                    }

                    EvictChangeSets(
                        connection,
                        cleanupPlan.Candidates.Select(
                            candidate => candidate.ChangeSetId));
                }
            }

            foreach (PreparedMutation mutation in prepared)
            {
                ApplyBusinessMutation(connection, mutation);
            }

            if (substantive.Length == 0)
            {
                InsertCommitResult(
                    connection,
                    request,
                    changeSetId: null,
                    ChangeExecutionStatus.Applied,
                    prepared.Count);
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Applied,
                    ChangeSetId: null,
                    prepared.Count);
                return;
            }

            Guid changeSetId = Guid.NewGuid();
            Guid[] archiveIds = substantive
                .Select(mutation => mutation.ArchiveId)
                .Distinct()
                .ToArray();
            InsertChangeSet(
                connection,
                request,
                changeSetId,
                archiveIds,
                prepared.Count);

            bool undoEligible =
                request.CaptureUndo &&
                request.OperationKind is not DataChangeOperationKind.Undo and
                    not DataChangeOperationKind.IrreversibleDelete &&
                UndoRetentionPolicy.ShouldCaptureNewMaterial(
                    archiveIds.Select(
                        archiveId => ReadUndoLimit(
                            connection,
                            archiveId)));
            UndoIneligibilityReason reason = undoEligible
                ? UndoIneligibilityReason.None
                : request.OperationKind ==
                    DataChangeOperationKind.IrreversibleDelete
                    ? UndoIneligibilityReason.IrreversibleDeletion
                    : request.OperationKind ==
                        DataChangeOperationKind.Undo
                        ? UndoIneligibilityReason.InverseOperation
                        : UndoIneligibilityReason.Disabled;
            long materialBytes = 0;

            foreach (PreparedMutation mutation in substantive)
            {
                InsertEntityChangeAndRevision(
                    connection,
                    changeSetId,
                    request.CommittedAt,
                    mutation);
                if (undoEligible)
                {
                    materialBytes += InsertUndoMaterial(
                        connection,
                        changeSetId,
                        mutation);
                }
            }

            connection.Execute(
                """
                INSERT INTO OperationHistory
                    (ChangeSetId, IsUndoEligible, IneligibilityReason,
                     UndoneByChangeSetId, MaterialBytes)
                VALUES (?, ?, ?, NULL, ?);
                """,
                changeSetId.ToString("D"),
                undoEligible,
                (int)reason,
                materialBytes);

            InsertCommitResult(
                connection,
                request,
                changeSetId,
                ChangeExecutionStatus.Applied,
                prepared.Count);

            foreach (ActiveTombstone tombstone in
                     confirmedReintroductions)
            {
                connection.Execute(
                    """
                    UPDATE LocalOnlyTombstones
                    SET IsActive = 0, ReintroducedByChangeSetId = ?
                    WHERE TombstoneKey = ? AND TombstoneVersion = ?
                        AND IsActive = 1;
                    """,
                    changeSetId.ToString("D"),
                    tombstone.TombstoneKey,
                    tombstone.TombstoneVersion);
            }

            if (undoEligible)
            {
                ApplyCapacityLimits(connection, archiveIds);
            }

            result = new GachaAtomicChangeResult(
                ChangeExecutionStatus.Applied,
                changeSetId,
                prepared.Count);
        });

        return result ?? throw new InvalidOperationException(
            "The Gacha atomic transaction did not produce a result.");
    }

    public async Task<GachaAtomicChangeResult> UndoLatestAsync(
        GachaUndoRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        GachaAtomicChangeResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitResultRow? committed = ReadCommitResult(
                connection,
                request.OperationId);
            if (committed is not null)
            {
                result = ToAlreadyCommittedResult(committed);
                return;
            }

            string? originalChangeSetId = connection.ExecuteScalar<string?>(
                """
                SELECT d.ChangeSetId
                FROM DataChangeSets d
                INNER JOIN DataChangeSetArchives a
                    ON a.ChangeSetId = d.ChangeSetId
                INNER JOIN OperationHistory h
                    ON h.ChangeSetId = d.ChangeSetId
                WHERE a.ArchiveId = ? AND h.IsUndoEligible = 1
                ORDER BY d.rowid DESC
                LIMIT 1;
                """,
                request.ArchiveId.ToString("D"));
            if (originalChangeSetId is null)
            {
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    "The archive has no eligible Gacha operation to undo.");
                return;
            }

            Guid[] archiveIds = connection.Query<ArchiveIdRow>(
                    """
                    SELECT ArchiveId
                    FROM DataChangeSetArchives
                    WHERE ChangeSetId = ?;
                    """,
                    originalChangeSetId)
                .Select(row => ParseRequiredGuid(row.ArchiveId, "archive"))
                .ToArray();
            foreach (Guid archiveId in archiveIds)
            {
                string? latest = connection.ExecuteScalar<string?>(
                    """
                    SELECT d.ChangeSetId
                    FROM DataChangeSets d
                    INNER JOIN DataChangeSetArchives a
                        ON a.ChangeSetId = d.ChangeSetId
                    INNER JOIN OperationHistory h
                        ON h.ChangeSetId = d.ChangeSetId
                    WHERE a.ArchiveId = ? AND h.IsUndoEligible = 1
                    ORDER BY d.rowid DESC
                    LIMIT 1;
                    """,
                    archiveId.ToString("D"));
                if (!string.Equals(
                        latest,
                        originalChangeSetId,
                        StringComparison.Ordinal))
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        "A cross-archive change is not the latest eligible operation in every archive.");
                    return;
                }
            }

            List<UndoMaterialRow> materials =
                connection.Query<UndoMaterialRow>(
                    """
                    SELECT ChangeSetId, EntityKind, EntityReference,
                           ChangeKind, ExpectedAfterVersion,
                           SnapshotFormatVersion,
                           BeforeSnapshotJson, AfterSnapshotJson,
                           MaterialBytes
                    FROM UndoMaterials
                    WHERE ChangeSetId = ?
                    ORDER BY Id;
                    """,
                    originalChangeSetId);
            if (materials.Count == 0 ||
                materials.Any(material =>
                    material.EntityKind != EntityKind ||
                    material.SnapshotFormatVersion !=
                        SnapshotFormatVersion))
            {
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    "Undo material is missing or uses an unsupported format.");
                return;
            }

            var inverse = new List<PreparedMutation>(materials.Count);
            foreach (UndoMaterialRow material in materials)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GachaRecordRow? originalBefore =
                    DeserializeSnapshot(material.BeforeSnapshotJson);
                GachaRecordRow? originalAfter =
                    DeserializeSnapshot(material.AfterSnapshotJson);
                GachaRecordRow identity =
                    originalAfter ?? originalBefore ??
                    throw new InvalidDataException(
                        "Undo material has no Gacha snapshot.");
                var reference = new GachaFactReference(
                    ParseRequiredGuid(
                        identity.GameAccountId,
                        "game account"),
                    identity.ExternalRecordId);
                GachaFactReadRow? current =
                    ReadCurrent(connection, reference);
                EntityChangeKind originalKind =
                    (EntityChangeKind)material.ChangeKind;

                if (originalKind is EntityChangeKind.Insert or
                    EntityChangeKind.Update)
                {
                    if (current is null ||
                        material.ExpectedAfterVersion is null ||
                        current.Version != material.ExpectedAfterVersion.Value)
                    {
                        result = new GachaAtomicChangeResult(
                            ChangeExecutionStatus.Conflict,
                            ChangeSetId: null,
                            AffectedRecordCount: 0,
                            $"Gacha fact {reference} changed after the original operation.");
                        return;
                    }
                    if (originalAfter is null ||
                        !GachaFactContentEquals(
                            current.ToGachaRow(),
                            originalAfter))
                    {
                        result = new GachaAtomicChangeResult(
                            ChangeExecutionStatus.Conflict,
                            ChangeSetId: null,
                            AffectedRecordCount: 0,
                            $"Gacha fact {reference} no longer matches the original after-state.");
                        return;
                    }
                }
                else if (originalKind == EntityChangeKind.Delete &&
                         current is not null)
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        $"Deleted Gacha fact {reference} has been reintroduced.");
                    return;
                }

                Guid factArchiveId = ReadArchiveId(
                    connection,
                    reference.GameAccountId);
                Guid archiveId = archiveIds.Contains(factArchiveId)
                    ? factArchiveId
                    : throw new InvalidDataException(
                        "Undo material references an unrelated archive.");
                if (ReadActiveTombstone(
                        connection,
                        archiveId,
                        reference) is not null)
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        $"Gacha fact {reference} was irreversibly deleted.");
                    return;
                }
                switch (originalKind)
                {
                    case EntityChangeKind.Insert:
                        inverse.Add(new PreparedMutation(
                            archiveId,
                            current!.ToGachaRow(),
                            After: null,
                            GachaRecordChangeKind.Substantive,
                            EntityChangeKind.Delete));
                        break;
                    case EntityChangeKind.Update:
                        GachaRecordRow restored =
                            originalBefore ??
                            throw new InvalidDataException(
                                "Update undo material has no before snapshot.");
                        restored.Id = current!.Id;
                        restored.Version = checked(current.Version + 1);
                        inverse.Add(new PreparedMutation(
                            archiveId,
                            current.ToGachaRow(),
                            restored,
                            GachaRecordChangeKind.Substantive,
                            EntityChangeKind.Update));
                        break;
                    case EntityChangeKind.Delete:
                        GachaRecordRow reinserted =
                            originalBefore ??
                            throw new InvalidDataException(
                                "Delete undo material has no before snapshot.");
                        reinserted.Id = 0;
                        reinserted.Version = NextFactVersion(
                            connection,
                            reference);
                        inverse.Add(new PreparedMutation(
                            archiveId,
                            Before: null,
                            reinserted,
                            GachaRecordChangeKind.Substantive,
                            EntityChangeKind.Insert));
                        break;
                    default:
                        throw new InvalidDataException(
                            "Undo material has an invalid change kind.");
                }
            }

            List<TombstoneRow> reintroducedTombstones = connection
                .Query<TombstoneRow>(
                    """
                    SELECT TombstoneKey, TombstoneVersion, ArchiveId, IsActive,
                           ReintroducedByChangeSetId
                    FROM LocalOnlyTombstones
                    WHERE ReintroducedByChangeSetId = ?;
                    """,
                    originalChangeSetId);
            if (reintroducedTombstones.Any(tombstone => tombstone.IsActive))
            {
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    "A reintroduction Tombstone changed before Undo.");
                return;
            }

            foreach (PreparedMutation mutation in inverse)
            {
                ApplyBusinessMutation(connection, mutation);
            }

            Guid undoChangeSetId = Guid.NewGuid();
            InsertUndoChangeSet(
                connection,
                request,
                undoChangeSetId,
                ParseRequiredGuid(
                    originalChangeSetId,
                    "original change set"),
                archiveIds,
                inverse.Count);
            foreach (PreparedMutation mutation in inverse)
            {
                InsertEntityChangeAndRevision(
                    connection,
                    undoChangeSetId,
                    request.CommittedAt,
                    mutation);
            }
            connection.Execute(
                """
                INSERT INTO OperationHistory
                    (ChangeSetId, IsUndoEligible, IneligibilityReason,
                     UndoneByChangeSetId, MaterialBytes)
                VALUES (?, 0, ?, NULL, 0);
                """,
                undoChangeSetId.ToString("D"),
                (int)UndoIneligibilityReason.InverseOperation);
            connection.Execute(
                """
                UPDATE OperationHistory
                SET IsUndoEligible = 0, IneligibilityReason = ?,
                    UndoneByChangeSetId = ?, MaterialBytes = 0
                WHERE ChangeSetId = ? AND IsUndoEligible = 1;
                """,
                (int)UndoIneligibilityReason.AlreadyUndone,
                undoChangeSetId.ToString("D"),
                originalChangeSetId);
            connection.Execute(
                "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
                originalChangeSetId);
            foreach (TombstoneRow tombstone in reintroducedTombstones)
            {
                int updated = connection.Execute(
                    """
                    UPDATE LocalOnlyTombstones
                    SET IsActive = 1, ReintroducedByChangeSetId = NULL
                    WHERE TombstoneKey = ? AND TombstoneVersion = ?
                        AND IsActive = 0 AND ReintroducedByChangeSetId = ?;
                    """,
                    tombstone.TombstoneKey,
                    tombstone.TombstoneVersion,
                    originalChangeSetId);
                if (updated != 1)
                {
                    throw new InvalidOperationException(
                        "The reintroduction Tombstone changed during Undo.");
                }
            }
            InsertUndoCommitResult(
                connection,
                request,
                undoChangeSetId,
                inverse.Count);
            result = new GachaAtomicChangeResult(
                ChangeExecutionStatus.Applied,
                undoChangeSetId,
                inverse.Count);
        });

        return result ?? throw new InvalidOperationException(
            "The Gacha undo transaction did not produce a result.");
    }

    public async Task<GachaAtomicChangeResult> IrreversiblyDeleteAsync(
        GachaIrreversibleDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        GachaAtomicChangeResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitResultRow? committed = ReadCommitResult(
                connection,
                request.OperationId);
            if (committed is not null)
            {
                result = ToAlreadyCommittedResult(committed);
                return;
            }

            GachaFactReadRow? current =
                ReadCurrent(connection, request.Reference);
            if (current is null ||
                current.Version != request.ExpectedVersion.Value)
            {
                result = new GachaAtomicChangeResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    AffectedRecordCount: 0,
                    "The Gacha fact no longer has the expected version.");
                return;
            }

            Guid archiveId = ParseRequiredGuid(
                current.ArchiveId,
                "archive");
            string tombstoneKey = CreateTombstoneKey(
                connection,
                archiveId,
                request.Reference);
            string entityReference = request.Reference.ToString();
            Guid[] affectedChangeSetIds =
                connection.Query<ChangeSetIdRow>(
                        """
                        SELECT DISTINCT ChangeSetId
                        FROM EntityChanges
                        WHERE EntityKind = ? AND EntityReference = ?;
                        """,
                        EntityKind,
                        entityReference)
                    .Select(row => ParseRequiredGuid(
                        row.ChangeSetId,
                        "change set"))
                    .ToArray();

            if (connection.Delete(current.ToGachaRow()) != 1)
            {
                throw new InvalidOperationException(
                    "The irreversible Gacha deletion did not affect one row.");
            }
            connection.Execute(
                """
                DELETE FROM GachaRevisions
                WHERE GameAccountId = ? AND ExternalRecordId = ?;
                """,
                request.Reference.GameAccountId.ToString("D"),
                request.Reference.ExternalRecordId);
            foreach (Guid affectedChangeSetId in affectedChangeSetIds)
            {
                connection.Execute(
                    "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
                    affectedChangeSetId.ToString("D"));
                connection.Execute(
                    """
                    UPDATE OperationHistory
                    SET IsUndoEligible = 0, IneligibilityReason = ?,
                        MaterialBytes = 0
                    WHERE ChangeSetId = ?;
                    """,
                    (int)UndoIneligibilityReason.IrreversibleDeletion,
                    affectedChangeSetId.ToString("D"));
            }

            Guid changeSetId = Guid.NewGuid();
            InsertIrreversibleDeleteChangeSet(
                connection,
                request,
                changeSetId,
                archiveId);
            connection.Execute(
                """
                INSERT INTO EntityChanges
                    (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                     BeforeVersion, AfterVersion)
                VALUES (?, ?, ?, ?, ?, NULL);
                """,
                changeSetId.ToString("D"),
                EntityKind,
                entityReference,
                (int)EntityChangeKind.Delete,
                current.Version);
            connection.Execute(
                """
                INSERT INTO OperationHistory
                    (ChangeSetId, IsUndoEligible, IneligibilityReason,
                     UndoneByChangeSetId, MaterialBytes)
                VALUES (?, 0, ?, NULL, 0);
                """,
                changeSetId.ToString("D"),
                (int)UndoIneligibilityReason.IrreversibleDeletion);
            connection.Execute(
                """
                INSERT INTO LocalOnlyTombstones
                    (TombstoneKey, TombstoneVersion, ArchiveId, IsActive,
                     DeletedByChangeSetId, DeletedAtUtcTicks,
                     ReintroducedByChangeSetId)
                VALUES (?, 1, ?, 1, ?, ?, NULL)
                ON CONFLICT(TombstoneKey)
                DO UPDATE SET
                    TombstoneVersion = TombstoneVersion + 1,
                    IsActive = 1,
                    DeletedByChangeSetId = excluded.DeletedByChangeSetId,
                    DeletedAtUtcTicks = excluded.DeletedAtUtcTicks,
                    ReintroducedByChangeSetId = NULL;
                """,
                tombstoneKey,
                archiveId.ToString("D"),
                changeSetId.ToString("D"),
                request.CommittedAt.UtcDateTime.Ticks);
            InsertIrreversibleDeleteCommitResult(
                connection,
                request,
                changeSetId);
            result = new GachaAtomicChangeResult(
                ChangeExecutionStatus.Applied,
                changeSetId,
                AffectedRecordCount: 1);
        });

        return result ?? throw new InvalidOperationException(
            "The irreversible Gacha deletion did not produce a result.");
    }

    public async Task<GachaPortableApplyResult> ApplyAsync(
        GachaPortableApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Package);
        ArgumentNullException.ThrowIfNull(request.Plan);
        if (request.ReceiptBatchId == Guid.Empty)
        {
            throw new ArgumentException(
                "Receipt batch ID cannot be empty.",
                nameof(request));
        }
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        GachaPortableApplyResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitResultRow? committed = ReadCommitResult(
                connection,
                request.OperationId);
            if (committed is not null)
            {
                result = ReadCommittedPortableResult(
                    connection,
                    committed);
                return;
            }

            PortablePreparation preparation;
            try
            {
                preparation = PreparePortableApply(
                    connection,
                    request,
                    cancellationToken);
            }
            catch (PortableApplySuppressedException exception)
            {
                result = new GachaPortableApplyResult(
                    ChangeExecutionStatus.Suppressed,
                    ChangeSetId: null,
                    TargetArchiveId: exception.Warning.ArchiveId,
                    Accounts: [],
                    InsertedRecordCount: 0,
                    SkippedRecordCount: 0,
                    exception.Message,
                    exception.Warning);
                return;
            }
            catch (PortableApplyConflictException exception)
            {
                result = new GachaPortableApplyResult(
                    ChangeExecutionStatus.Conflict,
                    ChangeSetId: null,
                    TargetArchiveId: null,
                    Accounts: [],
                    InsertedRecordCount: 0,
                    SkippedRecordCount: 0,
                    exception.Message);
                return;
            }

            bool hasPersistentChange =
                preparation.CreateArchive ||
                preparation.IdentitiesToInsert.Count > 0 ||
                preparation.Accounts.Any(account => account.CreateAccount) ||
                preparation.AliasesToInsert.Count > 0 ||
                preparation.Mutations.Count > 0;
            if (!hasPersistentChange)
            {
                InsertPortableNoOpCommitResult(connection, request);
                result = new GachaPortableApplyResult(
                    ChangeExecutionStatus.NoOp,
                    ChangeSetId: null,
                    preparation.ArchiveId,
                    preparation.Accounts.Select(
                        account => account.ToResult()).ToArray(),
                    InsertedRecordCount: 0,
                    preparation.Accounts.Sum(
                        account => account.SkippedRecordCount));
                return;
            }

            bool infrastructureChanged =
                preparation.CreateArchive ||
                preparation.IdentitiesToInsert.Count > 0 ||
                preparation.Accounts.Any(account => account.CreateAccount) ||
                preparation.AliasesToInsert.Count > 0;
            if (preparation.Mutations.Count > 0)
            {
                HistoryCleanupPlan? cleanupPlan = BuildCleanupPlan(
                    connection,
                    request.OperationId,
                    captureUndoRequested: !infrastructureChanged,
                    preparation.Mutations);
                if (cleanupPlan is not null)
                {
                    if (cleanupPlan.Candidates.Count == 0 ||
                        cleanupPlan.AvailableBytes +
                        cleanupPlan.ReclaimableBytes <
                        cleanupPlan.RequiredBytes)
                    {
                        throw new IOException(
                            "There is not enough storage for the Portable Gacha apply, even after eligible undo material cleanup.");
                    }
                    if (request.CleanupConfirmationId !=
                        cleanupPlan.ConfirmationId)
                    {
                        result = new GachaPortableApplyResult(
                            ChangeExecutionStatus.NeedsConfirmation,
                            ChangeSetId: null,
                            preparation.ArchiveId,
                            Accounts: [],
                            InsertedRecordCount: 0,
                            SkippedRecordCount: 0,
                            CleanupPlan: cleanupPlan);
                        return;
                    }

                    EvictChangeSets(
                        connection,
                        cleanupPlan.Candidates.Select(
                            candidate => candidate.ChangeSetId));
                }
            }

            if (preparation.CreateArchive)
            {
                connection.Execute(
                    """
                    INSERT INTO PlayerArchives
                        (Id, Name, CreatedAtUtcTicks, UpdatedAtUtcTicks)
                    VALUES (?, ?, ?, ?);
                    """,
                    preparation.ArchiveId.ToString("D"),
                    request.Plan.Archive.ProposedName,
                    request.ReceivedAt.UtcDateTime.Ticks,
                    request.ReceivedAt.UtcDateTime.Ticks);
            }
            foreach (GameRoleIdentity identity in
                     preparation.IdentitiesToInsert)
            {
                connection.Insert(GameRoleIdentityRow.FromDomain(identity));
            }
            foreach (PreparedPortableAccount account in
                     preparation.Accounts.Where(account =>
                         account.CreateAccount))
            {
                connection.Execute(
                    """
                    INSERT INTO GameAccounts
                        (Id, PlayerArchiveId, GameRoleIdentityId, Uid,
                         ServerRegion, DisplayName, IsPlaceholder,
                         CreatedAtUtcTicks, UpdatedAtUtcTicks)
                    VALUES (?, ?, ?, ?, ?, ?, 0, ?, ?);
                    """,
                    account.TargetAccountId.ToString("D"),
                    preparation.ArchiveId.ToString("D"),
                    account.TargetIdentityId.ToString(),
                    account.Source.RoleIdentity.NaturalIdentity.Uid,
                    (int)MapServerRegion(
                        account.Source.RoleIdentity.NaturalIdentity),
                    account.Source.DisplayName,
                    request.ReceivedAt.UtcDateTime.Ticks,
                    request.ReceivedAt.UtcDateTime.Ticks);
            }
            foreach (PreparedMutation mutation in preparation.Mutations)
            {
                ApplyBusinessMutation(connection, mutation);
            }

            Guid changeSetId = Guid.NewGuid();
            InsertPortableChangeSet(
                connection,
                request,
                changeSetId,
                preparation.ArchiveId,
                preparation.Mutations.Count);
            foreach ((GameRoleIdentityId source, GameRoleIdentityId target)
                     in preparation.AliasesToInsert)
            {
                connection.Execute(
                    """
                    INSERT INTO RoleIdentityAliases
                        (SourceIdentityId, TargetIdentityId,
                         CreatedByChangeSetId)
                    VALUES (?, ?, ?);
                    """,
                    source.ToString(),
                    target.ToString(),
                    changeSetId.ToString("D"));
            }

            bool undoEligible =
                !infrastructureChanged &&
                preparation.Mutations.Count > 0 &&
                ReadUndoLimit(connection, preparation.ArchiveId) > 0;
            long materialBytes = 0;
            foreach (PreparedMutation mutation in preparation.Mutations)
            {
                InsertEntityChangeAndRevision(
                    connection,
                    changeSetId,
                    request.ReceivedAt,
                    mutation);
                if (undoEligible)
                {
                    materialBytes += InsertUndoMaterial(
                        connection,
                        changeSetId,
                        mutation);
                }
            }
            connection.Execute(
                """
                INSERT INTO OperationHistory
                    (ChangeSetId, IsUndoEligible, IneligibilityReason,
                     UndoneByChangeSetId, MaterialBytes)
                VALUES (?, ?, ?, NULL, ?);
                """,
                changeSetId.ToString("D"),
                undoEligible,
                (int)(undoEligible
                    ? UndoIneligibilityReason.None
                    : UndoIneligibilityReason.Disabled),
                materialBytes);

            Guid receiptId = Guid.NewGuid();
            connection.Execute(
                """
                INSERT INTO PortableImportReceipts
                    (ReceiptId, ChangeSetId, PackageFingerprint,
                     ReceivedAtUtcTicks, ReceivedAtOffsetMinutes,
                     ReceiptBatchId, SourceArchiveId, TargetArchiveId,
                     AccountCount, RecordCount)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                receiptId.ToString("D"),
                changeSetId.ToString("D"),
                request.Plan.PackageFingerprint,
                request.ReceivedAt.UtcDateTime.Ticks,
                checked((int)request.ReceivedAt.Offset.TotalMinutes),
                request.ReceiptBatchId.ToString("D"),
                request.Package.SourceArchive.ArchiveReference.ToString("D"),
                preparation.ArchiveId.ToString("D"),
                preparation.Accounts.Count,
                preparation.Mutations.Count);
            foreach (PreparedPortableAccount account in
                     preparation.Accounts)
            {
                connection.Execute(
                    """
                    INSERT INTO PortableImportReceiptAccounts
                        (ReceiptId, SourceAccountId, TargetAccountId,
                         SourceRoleIdentityId, TargetRoleIdentityId)
                    VALUES (?, ?, ?, ?, ?);
                    """,
                    receiptId.ToString("D"),
                    account.Source.AccountReference.ToString("D"),
                    account.TargetAccountId.ToString("D"),
                    account.Source.RoleIdentity.Id.ToString(),
                    account.TargetIdentityId.ToString());
            }
            foreach (ActiveTombstone tombstone in
                     preparation.ConfirmedReintroductions)
            {
                connection.Execute(
                    """
                    UPDATE LocalOnlyTombstones
                    SET IsActive = 0, ReintroducedByChangeSetId = ?
                    WHERE TombstoneKey = ? AND TombstoneVersion = ?
                        AND IsActive = 1;
                    """,
                    changeSetId.ToString("D"),
                    tombstone.TombstoneKey,
                    tombstone.TombstoneVersion);
            }
            InsertPortableCommitResult(
                connection,
                request,
                changeSetId,
                preparation.Mutations.Count);
            if (undoEligible)
            {
                ApplyCapacityLimits(
                    connection,
                    [preparation.ArchiveId]);
            }

            result = new GachaPortableApplyResult(
                ChangeExecutionStatus.Applied,
                changeSetId,
                preparation.ArchiveId,
                preparation.Accounts.Select(
                    account => account.ToResult()).ToArray(),
                preparation.Mutations.Count,
                preparation.Accounts.Sum(
                    account => account.SkippedRecordCount));
        });

        return result ?? throw new InvalidOperationException(
            "The Portable Gacha apply transaction did not produce a result.");
    }

    public async Task<int> GetUndoLimitAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveId(archiveId);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureArchiveExistsAsync(archiveId);
        return await database.Connection.ExecuteScalarAsync<int?>(
                """
                SELECT UndoLimit
                FROM ArchiveUndoSettings
                WHERE ArchiveId = ?;
                """,
                archiveId.ToString("D")) ??
            UndoRetentionPolicy.DefaultLimit;
    }

    public async Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int undoLimit,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveId(archiveId);
        UndoRetentionPolicy.ValidateLimit(undoLimit);
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        UndoLimitChangeResult? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureArchiveExists(connection, archiveId);
            int previous = ReadUndoLimit(connection, archiveId);
            connection.Execute(
                """
                INSERT INTO ArchiveUndoSettings (ArchiveId, UndoLimit)
                VALUES (?, ?)
                ON CONFLICT(ArchiveId)
                DO UPDATE SET UndoLimit = excluded.UndoLimit;
                """,
                archiveId.ToString("D"),
                undoLimit);

            Guid[] evictions = undoLimit == 0
                ? []
                : FindCapacityEvictions(
                    connection,
                    archiveId,
                    undoLimit);
            EvictChangeSets(connection, evictions);
            result = new UndoLimitChangeResult(
                previous,
                undoLimit,
                evictions);
        });
        return result!;
    }

    public async Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
        Guid archiveId,
        int count,
        CancellationToken cancellationToken = default)
    {
        ValidateArchiveId(archiveId);
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "History count must be positive.");
        }
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureArchiveExistsAsync(archiveId);

        List<OperationHistoryReadRow> rows =
            await database.Connection.QueryAsync<OperationHistoryReadRow>(
                """
                SELECT d.ChangeSetId, d.OperationKind,
                       d.CommittedAtUtcTicks, d.CommittedAtOffsetMinutes,
                       d.Summary, d.AffectedRecordCount,
                       h.IsUndoEligible, h.IneligibilityReason,
                       h.UndoneByChangeSetId
                FROM DataChangeSets d
                INNER JOIN DataChangeSetArchives a
                    ON a.ChangeSetId = d.ChangeSetId
                INNER JOIN OperationHistory h
                    ON h.ChangeSetId = d.ChangeSetId
                WHERE a.ArchiveId = ?
                ORDER BY d.rowid DESC
                LIMIT ?;
                """,
                archiveId.ToString("D"),
                count);
        return rows.Select(row => new OperationHistoryItem(
                ParseRequiredGuid(row.ChangeSetId, "change set"),
                (DataChangeOperationKind)row.OperationKind,
                ReadTimestamp(
                    row.CommittedAtUtcTicks,
                    row.CommittedAtOffsetMinutes),
                row.Summary,
                row.AffectedRecordCount,
                row.IsUndoEligible,
                (UndoIneligibilityReason)row.IneligibilityReason,
                ParseOptionalGuid(row.UndoneByChangeSetId)))
            .ToArray();
    }

    private static PortablePreparation PreparePortableApply(
        SQLiteConnection connection,
        GachaPortableApplyRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.Plan.CanApply)
        {
            throw new PortableApplyConflictException(
                "The Portable import plan is not applicable.");
        }
        string fingerprint =
            GachaPortablePackageFingerprint.Compute(request.Package);
        if (!string.Equals(
                fingerprint,
                request.Plan.PackageFingerprint,
                StringComparison.Ordinal))
        {
            throw new PortableApplyConflictException(
                "The Portable package changed after it was planned.");
        }
        if (request.Package.Accounts.Count != request.Plan.Accounts.Count)
        {
            throw new PortableApplyConflictException(
                "The Portable account set changed after it was planned.");
        }

        Guid archiveId;
        bool createArchive;
        if (request.Plan.Archive.Kind ==
            GachaPortableArchivePlanKind.CreateNew)
        {
            archiveId = request.Plan.Archive.ProposedArchiveId ??
                throw new PortableApplyConflictException(
                    "The plan has no proposed archive ID.");
            createArchive = true;
            if (connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives WHERE Id = ?;",
                    archiveId.ToString("D")) != 0)
            {
                throw new PortableApplyConflictException(
                    "The proposed archive ID is already in use.");
            }
            if (request.Plan.RequireUniqueArchiveNames &&
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives WHERE Name = ?;",
                    request.Plan.Archive.ProposedName) != 0)
            {
                throw new PortableApplyConflictException(
                    "The proposed archive name is no longer unique.");
            }
        }
        else if (request.Plan.Archive.Kind ==
                 GachaPortableArchivePlanKind.MapExisting)
        {
            archiveId = request.Plan.Archive.TargetArchiveId ??
                throw new PortableApplyConflictException(
                    "The plan has no target archive ID.");
            createArchive = false;
            PortableArchiveStateRow? archive =
                connection.Query<PortableArchiveStateRow>(
                        """
                        SELECT Id, Name, UpdatedAtUtcTicks
                        FROM PlayerArchives
                        WHERE Id = ?;
                        """,
                        archiveId.ToString("D"))
                    .SingleOrDefault();
            if (archive is null ||
                archive.Name != request.Plan.Archive.ProposedName ||
                request.Plan.Archive.TargetArchiveUpdatedAt is null ||
                archive.UpdatedAtUtcTicks != request.Plan.Archive
                    .TargetArchiveUpdatedAt.Value.UtcDateTime.Ticks)
            {
                throw new PortableApplyConflictException(
                    "The target archive changed after it was planned.");
            }
        }
        else
        {
            throw new PortableApplyConflictException(
                "The Portable import still requires an archive selection.");
        }

        Dictionary<Guid, GachaPortableAccountImportPlan> plans =
            request.Plan.Accounts.ToDictionary(
                plan => plan.SourceAccountReference);
        if (plans.Count != request.Plan.Accounts.Count ||
            request.Package.Accounts.Any(account =>
                !plans.ContainsKey(account.AccountReference)))
        {
            throw new PortableApplyConflictException(
                "The Portable account mapping is ambiguous.");
        }

        var identitiesToInsert = new Dictionary<
            GameRoleIdentityId,
            GameRoleIdentity>();
        var aliasesToInsert = new List<(
            GameRoleIdentityId Source,
            GameRoleIdentityId Target)>();
        var accounts = new List<PreparedPortableAccount>(
            request.Package.Accounts.Count);
        var mutations = new List<PreparedMutation>();
        var confirmedReintroductions = new List<ActiveTombstone>();
        IReadOnlyList<TombstoneReintroductionConfirmation> confirmations =
            request.ReintroductionConfirmations ?? [];

        foreach (GachaPortableAccount source in request.Package.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaPortableAccountImportPlan plan =
                plans[source.AccountReference];
            GameRoleIdentityId targetIdentityId =
                plan.Identity.TargetIdentityId ??
                throw new PortableApplyConflictException(
                    "The account plan has no target identity.");
            PortableIdentityStateRow? sourceIdentity =
                ReadIdentity(connection, source.RoleIdentity.Id);
            if (sourceIdentity is not null &&
                sourceIdentity.ToNaturalIdentity() !=
                    source.RoleIdentity.NaturalIdentity)
            {
                throw new PortableApplyConflictException(
                    "The source identity GUID now refers to another natural identity.");
            }

            PortableIdentityStateRow? targetIdentity =
                ReadIdentity(connection, targetIdentityId);
            if (targetIdentity is null)
            {
                if (plan.Identity.Kind !=
                        GachaPortableIdentityPlanKind.PreserveSource ||
                    targetIdentityId != source.RoleIdentity.Id)
                {
                    throw new PortableApplyConflictException(
                        "The planned target identity no longer exists.");
                }

                identitiesToInsert[targetIdentityId] =
                    source.RoleIdentity;
            }
            else if (targetIdentity.ToNaturalIdentity() !=
                     source.RoleIdentity.NaturalIdentity)
            {
                throw new PortableApplyConflictException(
                    "The target identity no longer matches the source natural identity.");
            }

            if (source.RoleIdentity.Id != targetIdentityId)
            {
                PortableAliasRow? alias = connection
                    .Query<PortableAliasRow>(
                        """
                        SELECT SourceIdentityId, TargetIdentityId
                        FROM RoleIdentityAliases
                        WHERE SourceIdentityId = ?;
                        """,
                        source.RoleIdentity.Id.ToString())
                    .SingleOrDefault();
                if (alias is not null &&
                    alias.TargetIdentityId != targetIdentityId.ToString())
                {
                    throw new PortableApplyConflictException(
                        "The source identity already has another alias target.");
                }
                if (connection.ExecuteScalar<int>(
                        """
                        SELECT COUNT(*)
                        FROM RoleIdentityAliases
                        WHERE SourceIdentityId = ?;
                        """,
                        targetIdentityId.ToString()) != 0)
                {
                    throw new PortableApplyConflictException(
                        "The planned identity alias would create an alias chain.");
                }
                if (alias is null)
                {
                    aliasesToInsert.Add(
                        (source.RoleIdentity.Id, targetIdentityId));
                }
            }

            Guid targetAccountId;
            bool createAccount;
            PortableAccountStateRow? targetAccount = null;
            if (plan.TargetAccountId is Guid existingAccountId)
            {
                targetAccountId = existingAccountId;
                createAccount = false;
                targetAccount = ReadPortableAccount(
                    connection,
                    targetAccountId);
                if (targetAccount is null ||
                    targetAccount.PlayerArchiveId !=
                        archiveId.ToString("D") ||
                    targetAccount.GameRoleIdentityId !=
                        targetIdentityId.ToString() ||
                    plan.TargetAccountUpdatedAt is null ||
                    targetAccount.UpdatedAtUtcTicks !=
                        plan.TargetAccountUpdatedAt.Value.UtcDateTime.Ticks)
                {
                    throw new PortableApplyConflictException(
                        "A target account changed after it was planned.");
                }
                int actualCount = connection.ExecuteScalar<int>(
                    """
                    SELECT COUNT(*)
                    FROM GachaRecords
                    WHERE GameAccountId = ?;
                    """,
                    targetAccountId.ToString("D"));
                if (actualCount != plan.TargetRecordCountAtPlan)
                {
                    throw new PortableApplyConflictException(
                        "A target account record count changed after it was planned.");
                }
            }
            else
            {
                targetAccountId = plan.ProposedAccountId ??
                    throw new PortableApplyConflictException(
                        "The plan has no proposed account ID.");
                createAccount = true;
                if (connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM GameAccounts WHERE Id = ?;",
                        targetAccountId.ToString("D")) != 0)
                {
                    throw new PortableApplyConflictException(
                        "The proposed account ID is already in use.");
                }
            }

            int inserted = 0;
            int skipped = 0;
            foreach (GachaRecord sourceRecord in source.Records)
            {
                var reference = new GachaFactReference(
                    targetAccountId,
                    sourceRecord.ExternalRecordId);
                GachaRecord targetRecord =
                    sourceRecord with { GameAccountId = targetAccountId };
                GachaRecordRow proposed =
                    GachaRecordRow.FromDomain(targetRecord);
                GachaFactReadRow? current =
                    ReadCurrent(connection, reference);
                if (current is not null)
                {
                    if (!PortableFactsEqual(
                            current.ToGachaRow().ToDomain(),
                            targetRecord))
                    {
                        throw new PortableApplyConflictException(
                            $"Gacha fact {reference} changed after planning.");
                    }
                    skipped++;
                    continue;
                }

                proposed.Version = NextFactVersion(connection, reference);
                string tombstoneKey = CreateTombstoneKey(
                    archiveId,
                    targetIdentityId,
                    reference.ExternalRecordId);
                TombstoneRow? tombstone = connection
                    .Query<TombstoneRow>(
                        """
                        SELECT TombstoneKey, TombstoneVersion,
                               ArchiveId, IsActive
                        FROM LocalOnlyTombstones
                        WHERE TombstoneKey = ? AND IsActive = 1;
                        """,
                        tombstoneKey)
                    .SingleOrDefault();
                if (tombstone is not null)
                {
                    bool confirmed = confirmations.Any(confirmation =>
                        confirmation.TombstoneKey == tombstoneKey &&
                        confirmation.TombstoneVersion ==
                            tombstone.TombstoneVersion &&
                        confirmation.Reference == reference);
                    if (!confirmed)
                    {
                        throw new PortableApplySuppressedException(
                            new TombstoneReintroductionWarning(
                                tombstoneKey,
                                tombstone.TombstoneVersion,
                                reference,
                                archiveId));
                    }
                    confirmedReintroductions.Add(
                        new ActiveTombstone(
                            tombstoneKey,
                            tombstone.TombstoneVersion));
                }

                mutations.Add(new PreparedMutation(
                    archiveId,
                    Before: null,
                    proposed,
                    GachaRecordChangeKind.Substantive,
                    EntityChangeKind.Insert));
                inserted++;
            }

            if (inserted != plan.AddCount ||
                skipped != plan.SkipCount ||
                plan.ConflictCount != 0)
            {
                throw new PortableApplyConflictException(
                    "The target Gacha facts changed after they were planned.");
            }
            accounts.Add(new PreparedPortableAccount(
                source,
                targetAccountId,
                targetIdentityId,
                createAccount,
                inserted,
                skipped));
        }

        if (mutations.Count != request.Plan.Preview.AddCount ||
            accounts.Sum(account => account.SkippedRecordCount) !=
                request.Plan.Preview.SkipCount)
        {
            throw new PortableApplyConflictException(
                "The Portable preview no longer matches the current target.");
        }

        return new PortablePreparation(
            archiveId,
            createArchive,
            identitiesToInsert.Values.ToArray(),
            aliasesToInsert,
            accounts,
            mutations,
            confirmedReintroductions);
    }

    private static PreparedMutation? PrepareMutation(
        SQLiteConnection connection,
        GachaFactMutation mutation,
        GachaFactReadRow? current,
        Guid archiveId)
    {
        if (current is null)
        {
            if (mutation.ProposedRecord is null)
            {
                return null;
            }

            GachaRecordRow after =
                GachaRecordRow.FromDomain(mutation.ProposedRecord);
            after.Version = NextFactVersion(connection, mutation.Reference);
            return new PreparedMutation(
                archiveId,
                Before: null,
                after,
                GachaRecordChangeKind.Substantive,
                EntityChangeKind.Insert);
        }

        GachaRecordRow before = current.ToGachaRow();
        if (mutation.ProposedRecord is null)
        {
            return new PreparedMutation(
                archiveId,
                before,
                After: null,
                GachaRecordChangeKind.Substantive,
                EntityChangeKind.Delete);
        }

        GachaRecordRow proposed =
            GachaRecordRow.FromDomain(mutation.ProposedRecord);
        proposed.Id = before.Id;
        proposed.Version = checked(before.Version + 1);
        GachaRecordChange change = GachaRecordChangeClassifier.Compare(
            before.ToDomain(),
            proposed.ToDomain());
        if (change.Kind == GachaRecordChangeKind.NoOp)
        {
            return null;
        }

        return new PreparedMutation(
            archiveId,
            before,
            proposed,
            change.Kind,
            EntityChangeKind.Update);
    }

    private static void ApplyBusinessMutation(
        SQLiteConnection connection,
        PreparedMutation mutation)
    {
        switch (mutation.EntityChangeKind)
        {
            case EntityChangeKind.Insert:
                connection.Insert(mutation.After!);
                break;
            case EntityChangeKind.Update:
                if (connection.Update(mutation.After!) != 1)
                {
                    throw new InvalidOperationException(
                        "The Gacha fact update did not affect one row.");
                }
                break;
            case EntityChangeKind.Delete:
                if (connection.Delete(mutation.Before!) != 1)
                {
                    throw new InvalidOperationException(
                        "The Gacha fact deletion did not affect one row.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    private static long NextFactVersion(
        SQLiteConnection connection,
        GachaFactReference reference)
    {
        long previous = connection.ExecuteScalar<long>(
            """
            SELECT COALESCE(MAX(COALESCE(AfterVersion, BeforeVersion, 0)), 0)
            FROM EntityChanges
            WHERE EntityKind = ? AND EntityReference = ?;
            """,
            EntityKind,
            reference.ToString());
        return checked(previous + 1);
    }

    private static bool GachaFactContentEquals(
        GachaRecordRow current,
        GachaRecordRow expected) =>
        current.ToDomain() == expected.ToDomain();

    private static void InsertChangeSet(
        SQLiteConnection connection,
        GachaAtomicChangeRequest request,
        Guid changeSetId,
        IReadOnlyCollection<Guid> archiveIds,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO DataChangeSets
                (ChangeSetId, OperationId, OperationKind,
                 StartedAtUtcTicks, StartedAtOffsetMinutes,
                 CommittedAtUtcTicks, CommittedAtOffsetMinutes,
                 Origin, Summary, AffectedRecordCount, UndoOfChangeSetId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            request.OperationId.ToString(),
            (int)request.OperationKind,
            request.StartedAt.UtcDateTime.Ticks,
            checked((int)request.StartedAt.Offset.TotalMinutes),
            request.CommittedAt.UtcDateTime.Ticks,
            checked((int)request.CommittedAt.Offset.TotalMinutes),
            (int)request.Origin,
            request.Summary,
            affectedRecordCount,
            request.UndoOfChangeSetId?.ToString("D"));

        foreach (Guid archiveId in archiveIds)
        {
            connection.Execute(
                """
                INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId)
                VALUES (?, ?);
                """,
                changeSetId.ToString("D"),
                archiveId.ToString("D"));
        }
    }

    private static void InsertUndoChangeSet(
        SQLiteConnection connection,
        GachaUndoRequest request,
        Guid changeSetId,
        Guid originalChangeSetId,
        IEnumerable<Guid> archiveIds,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO DataChangeSets
                (ChangeSetId, OperationId, OperationKind,
                 StartedAtUtcTicks, StartedAtOffsetMinutes,
                 CommittedAtUtcTicks, CommittedAtOffsetMinutes,
                 Origin, Summary, AffectedRecordCount, UndoOfChangeSetId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            request.OperationId.ToString(),
            (int)DataChangeOperationKind.Undo,
            request.StartedAt.UtcDateTime.Ticks,
            checked((int)request.StartedAt.Offset.TotalMinutes),
            request.CommittedAt.UtcDateTime.Ticks,
            checked((int)request.CommittedAt.Offset.TotalMinutes),
            (int)DataOrigin.UserEntered,
            request.Summary,
            affectedRecordCount,
            originalChangeSetId.ToString("D"));
        foreach (Guid archiveId in archiveIds)
        {
            connection.Execute(
                """
                INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId)
                VALUES (?, ?);
                """,
                changeSetId.ToString("D"),
                archiveId.ToString("D"));
        }
    }

    private static void InsertIrreversibleDeleteChangeSet(
        SQLiteConnection connection,
        GachaIrreversibleDeleteRequest request,
        Guid changeSetId,
        Guid archiveId)
    {
        connection.Execute(
            """
            INSERT INTO DataChangeSets
                (ChangeSetId, OperationId, OperationKind,
                 StartedAtUtcTicks, StartedAtOffsetMinutes,
                 CommittedAtUtcTicks, CommittedAtOffsetMinutes,
                 Origin, Summary, AffectedRecordCount, UndoOfChangeSetId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 1, NULL);
            """,
            changeSetId.ToString("D"),
            request.OperationId.ToString(),
            (int)DataChangeOperationKind.IrreversibleDelete,
            request.StartedAt.UtcDateTime.Ticks,
            checked((int)request.StartedAt.Offset.TotalMinutes),
            request.CommittedAt.UtcDateTime.Ticks,
            checked((int)request.CommittedAt.Offset.TotalMinutes),
            (int)DataOrigin.UserEntered,
            request.Summary);
        connection.Execute(
            """
            INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId)
            VALUES (?, ?);
            """,
            changeSetId.ToString("D"),
            archiveId.ToString("D"));
    }

    private static void InsertPortableChangeSet(
        SQLiteConnection connection,
        GachaPortableApplyRequest request,
        Guid changeSetId,
        Guid archiveId,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO DataChangeSets
                (ChangeSetId, OperationId, OperationKind,
                 StartedAtUtcTicks, StartedAtOffsetMinutes,
                 CommittedAtUtcTicks, CommittedAtOffsetMinutes,
                 Origin, Summary, AffectedRecordCount, UndoOfChangeSetId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL);
            """,
            changeSetId.ToString("D"),
            request.OperationId.ToString(),
            (int)DataChangeOperationKind.Import,
            request.ReceivedAt.UtcDateTime.Ticks,
            checked((int)request.ReceivedAt.Offset.TotalMinutes),
            request.ReceivedAt.UtcDateTime.Ticks,
            checked((int)request.ReceivedAt.Offset.TotalMinutes),
            (int)DataOrigin.FurinaImport,
            "Apply Gacha Portable v1 package",
            affectedRecordCount);
        connection.Execute(
            """
            INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId)
            VALUES (?, ?);
            """,
            changeSetId.ToString("D"),
            archiveId.ToString("D"));
    }

    private static void InsertEntityChangeAndRevision(
        SQLiteConnection connection,
        Guid changeSetId,
        DateTimeOffset committedAt,
        PreparedMutation mutation)
    {
        string entityReference = mutation.Reference.ToString();
        connection.Execute(
            """
            INSERT INTO EntityChanges
                (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                 BeforeVersion, AfterVersion)
            VALUES (?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            EntityKind,
            entityReference,
            (int)mutation.EntityChangeKind,
            mutation.Before?.Version,
            mutation.After?.Version);

        string? beforeJson = SerializeSnapshot(mutation.Before);
        string? afterJson = SerializeSnapshot(mutation.After);
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
            beforeJson,
            afterJson,
            committedAt.UtcDateTime.Ticks);
    }

    private static long InsertUndoMaterial(
        SQLiteConnection connection,
        Guid changeSetId,
        PreparedMutation mutation)
    {
        string? beforeJson = SerializeSnapshot(mutation.Before);
        string? afterJson = SerializeSnapshot(mutation.After);
        long bytes =
            (beforeJson is null ? 0 : Encoding.UTF8.GetByteCount(beforeJson)) +
            (afterJson is null ? 0 : Encoding.UTF8.GetByteCount(afterJson));
        connection.Execute(
            """
            INSERT INTO UndoMaterials
                (ChangeSetId, EntityKind, EntityReference, ChangeKind,
                 ExpectedAfterVersion, SnapshotFormatVersion,
                 BeforeSnapshotJson, AfterSnapshotJson, MaterialBytes)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            changeSetId.ToString("D"),
            EntityKind,
            mutation.Reference.ToString(),
            (int)mutation.EntityChangeKind,
            mutation.After?.Version,
            SnapshotFormatVersion,
            beforeJson,
            afterJson,
            bytes);
        return bytes;
    }

    private static void InsertCommitResult(
        SQLiteConnection connection,
        GachaAtomicChangeRequest request,
        Guid? changeSetId,
        ChangeExecutionStatus status,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?);
            """,
            request.OperationId.ToString(),
            changeSetId?.ToString("D"),
            (int)status,
            affectedRecordCount,
            request.CommittedAt.UtcDateTime.Ticks);
    }

    private static void InsertUndoCommitResult(
        SQLiteConnection connection,
        GachaUndoRequest request,
        Guid changeSetId,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?);
            """,
            request.OperationId.ToString(),
            changeSetId.ToString("D"),
            (int)ChangeExecutionStatus.Applied,
            affectedRecordCount,
            request.CommittedAt.UtcDateTime.Ticks);
    }

    private static void InsertIrreversibleDeleteCommitResult(
        SQLiteConnection connection,
        GachaIrreversibleDeleteRequest request,
        Guid changeSetId)
    {
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, ?, ?, 1, ?);
            """,
            request.OperationId.ToString(),
            changeSetId.ToString("D"),
            (int)ChangeExecutionStatus.Applied,
            request.CommittedAt.UtcDateTime.Ticks);
    }

    private static void InsertPortableCommitResult(
        SQLiteConnection connection,
        GachaPortableApplyRequest request,
        Guid changeSetId,
        int affectedRecordCount)
    {
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, ?, ?, ?, ?);
            """,
            request.OperationId.ToString(),
            changeSetId.ToString("D"),
            (int)ChangeExecutionStatus.Applied,
            affectedRecordCount,
            request.ReceivedAt.UtcDateTime.Ticks);
    }

    private static void InsertPortableNoOpCommitResult(
        SQLiteConnection connection,
        GachaPortableApplyRequest request)
    {
        connection.Execute(
            """
            INSERT INTO OperationCommitResults
                (OperationId, ChangeSetId, Status,
                 AffectedRecordCount, CommittedAtUtcTicks)
            VALUES (?, NULL, ?, 0, ?);
            """,
            request.OperationId.ToString(),
            (int)ChangeExecutionStatus.NoOp,
            request.ReceivedAt.UtcDateTime.Ticks);
    }

    private static GachaPortableApplyResult ReadCommittedPortableResult(
        SQLiteConnection connection,
        CommitResultRow committed)
    {
        if (committed.ChangeSetId is null)
        {
            return new GachaPortableApplyResult(
                ChangeExecutionStatus.AlreadyCommitted,
                ChangeSetId: null,
                TargetArchiveId: null,
                Accounts: [],
                InsertedRecordCount: 0,
                SkippedRecordCount: 0);
        }

        PortableReceiptReadRow? receipt =
            connection.Query<PortableReceiptReadRow>(
                    """
                    SELECT ReceiptId, TargetArchiveId, RecordCount
                    FROM PortableImportReceipts
                    WHERE ChangeSetId = ?;
                    """,
                    committed.ChangeSetId)
                .SingleOrDefault();
        if (receipt is null)
        {
            return new GachaPortableApplyResult(
                ChangeExecutionStatus.AlreadyCommitted,
                ParseOptionalGuid(committed.ChangeSetId),
                TargetArchiveId: null,
                Accounts: [],
                committed.AffectedRecordCount,
                SkippedRecordCount: 0);
        }

        GachaPortableAccountApplyResult[] accounts =
            connection.Query<PortableReceiptAccountReadRow>(
                    """
                    SELECT SourceAccountId, TargetAccountId,
                           TargetRoleIdentityId
                    FROM PortableImportReceiptAccounts
                    WHERE ReceiptId = ?;
                    """,
                    receipt.ReceiptId)
                .Select(row => new GachaPortableAccountApplyResult(
                    ParseRequiredGuid(
                        row.SourceAccountId,
                        "source account"),
                    ParseRequiredGuid(
                        row.TargetAccountId,
                        "target account"),
                    new GameRoleIdentityId(
                        ParseRequiredGuid(
                            row.TargetRoleIdentityId,
                            "target role identity")),
                    InsertedRecordCount: 0,
                    SkippedRecordCount: 0))
                .ToArray();
        return new GachaPortableApplyResult(
            ChangeExecutionStatus.AlreadyCommitted,
            ParseRequiredGuid(committed.ChangeSetId, "change set"),
            ParseRequiredGuid(receipt.TargetArchiveId, "target archive"),
            accounts,
            receipt.RecordCount,
            SkippedRecordCount: 0);
    }

    private static CommitResultRow? ReadCommitResult(
        SQLiteConnection connection,
        OperationId operationId) =>
        connection.Query<CommitResultRow>(
            """
            SELECT OperationId, ChangeSetId, Status,
                   AffectedRecordCount, CommittedAtUtcTicks
            FROM OperationCommitResults
            WHERE OperationId = ?;
            """,
            operationId.ToString())
        .SingleOrDefault();

    private static GachaAtomicChangeResult ToAlreadyCommittedResult(
        CommitResultRow committed) =>
        new(
            ChangeExecutionStatus.AlreadyCommitted,
            ParseOptionalGuid(committed.ChangeSetId),
            committed.AffectedRecordCount,
            OriginalStatus: (ChangeExecutionStatus)committed.Status);

    private static void ApplyCapacityLimits(
        SQLiteConnection connection,
        IEnumerable<Guid> archiveIds)
    {
        var evictions = new HashSet<string>(StringComparer.Ordinal);
        foreach (Guid archiveId in archiveIds)
        {
            int limit = ReadUndoLimit(connection, archiveId);
            if (limit == 0)
            {
                continue;
            }

            evictions.UnionWith(
                FindCapacityEvictions(
                    connection,
                    archiveId,
                    limit)
                .Select(id => id.ToString("D")));
        }

        EvictChangeSets(
            connection,
            evictions.Select(Guid.Parse));
    }

    private static Guid[] FindCapacityEvictions(
        SQLiteConnection connection,
        Guid archiveId,
        int limit) =>
        connection.Query<ChangeSetIdRow>(
            """
            SELECT d.ChangeSetId
            FROM DataChangeSets d
            INNER JOIN DataChangeSetArchives a
                ON a.ChangeSetId = d.ChangeSetId
            INNER JOIN OperationHistory h
                ON h.ChangeSetId = d.ChangeSetId
            WHERE a.ArchiveId = ? AND h.IsUndoEligible = 1
            ORDER BY d.rowid DESC
            LIMIT -1 OFFSET ?;
            """,
            archiveId.ToString("D"),
            limit)
        .Select(row => ParseRequiredGuid(
            row.ChangeSetId,
            "change set"))
        .ToArray();

    private static void EvictChangeSets(
        SQLiteConnection connection,
        IEnumerable<Guid> changeSetIds)
    {
        foreach (Guid changeSetId in changeSetIds.Distinct())
        {
            connection.Execute(
                """
                UPDATE OperationHistory
                SET IsUndoEligible = 0, IneligibilityReason = ?,
                    MaterialBytes = 0
                WHERE ChangeSetId = ?;
                """,
                (int)UndoIneligibilityReason.CapacityEvicted,
                changeSetId.ToString("D"));
            connection.Execute(
                "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
                changeSetId.ToString("D"));
        }
    }

    private HistoryCleanupPlan? BuildCleanupPlan(
        SQLiteConnection connection,
        OperationId operationId,
        bool captureUndoRequested,
        IReadOnlyCollection<PreparedMutation> mutations)
    {
        HistoryStorageCapacity capacity = capacityProvider.GetCapacity();
        if (capacity.AvailableBytes is not long availableBytes)
        {
            return null;
        }

        Guid[] archiveIds = mutations
            .Select(mutation => mutation.ArchiveId)
            .Distinct()
            .ToArray();
        bool captureUndo =
            captureUndoRequested &&
            UndoRetentionPolicy.ShouldCaptureNewMaterial(
                archiveIds.Select(
                    archiveId => ReadUndoLimit(
                        connection,
                        archiveId)));
        long snapshotBytes = mutations.Sum(mutation =>
        {
            string? before = SerializeSnapshot(mutation.Before);
            string? after = SerializeSnapshot(mutation.After);
            return (before is null
                    ? 0
                    : Encoding.UTF8.GetByteCount(before)) +
                (after is null
                    ? 0
                    : Encoding.UTF8.GetByteCount(after));
        });
        long requiredBytes = checked(
            FixedWriteReserveBytes +
            mutations.Count * PerMutationWriteReserveBytes +
            snapshotBytes * (captureUndo ? 2 : 1));
        if (availableBytes >= requiredBytes)
        {
            return null;
        }

        var candidates = new List<HistoryCleanupCandidate>();
        long reclaimableBytes = 0;
        foreach (CleanupCandidateRow row in
                 connection.Query<CleanupCandidateRow>(
                     """
                     SELECT d.ChangeSetId, h.MaterialBytes
                     FROM DataChangeSets d
                     INNER JOIN OperationHistory h
                         ON h.ChangeSetId = d.ChangeSetId
                     WHERE h.IsUndoEligible = 1 AND h.MaterialBytes > 0
                     ORDER BY d.rowid ASC;
                     """))
        {
            Guid changeSetId = ParseRequiredGuid(
                row.ChangeSetId,
                "change set");
            Guid[] candidateArchives =
                connection.Query<ArchiveIdRow>(
                        """
                        SELECT ArchiveId
                        FROM DataChangeSetArchives
                        WHERE ChangeSetId = ?;
                        """,
                        row.ChangeSetId)
                    .Select(candidate => ParseRequiredGuid(
                        candidate.ArchiveId,
                        "archive"))
                    .ToArray();
            if (!candidateArchives.Intersect(archiveIds).Any())
            {
                continue;
            }

            candidates.Add(new HistoryCleanupCandidate(
                changeSetId,
                candidateArchives,
                row.MaterialBytes));
            reclaimableBytes = checked(
                reclaimableBytes + row.MaterialBytes);
            if (availableBytes + reclaimableBytes >= requiredBytes)
            {
                break;
            }
        }

        Guid confirmationId = CreateCleanupConfirmationId(
            operationId,
            requiredBytes,
            candidates.Select(candidate => candidate.ChangeSetId));
        return new HistoryCleanupPlan(
            confirmationId,
            requiredBytes,
            availableBytes,
            reclaimableBytes,
            candidates);
    }

    private static Guid CreateCleanupConfirmationId(
        OperationId operationId,
        long requiredBytes,
        IEnumerable<Guid> changeSetIds)
    {
        string value =
            $"{operationId}|{requiredBytes}|" +
            string.Join(
                ",",
                changeSetIds.Select(id => id.ToString("D")));
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static int ReadUndoLimit(
        SQLiteConnection connection,
        Guid archiveId)
    {
        int? configured = connection.ExecuteScalar<int?>(
            """
            SELECT UndoLimit
            FROM ArchiveUndoSettings
            WHERE ArchiveId = ?;
            """,
            archiveId.ToString("D"));
        return configured ?? UndoRetentionPolicy.DefaultLimit;
    }

    private async Task EnsureArchiveExistsAsync(Guid archiveId)
    {
        int count = await database.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM PlayerArchives WHERE Id = ?;",
            archiveId.ToString("D"));
        if (count != 1)
        {
            throw new KeyNotFoundException(
                $"Archive {archiveId:D} does not exist.");
        }
    }

    private static void EnsureArchiveExists(
        SQLiteConnection connection,
        Guid archiveId)
    {
        int count = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM PlayerArchives WHERE Id = ?;",
            archiveId.ToString("D"));
        if (count != 1)
        {
            throw new KeyNotFoundException(
                $"Archive {archiveId:D} does not exist.");
        }
    }

    private static void ValidateArchiveId(Guid archiveId)
    {
        if (archiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "Archive ID cannot be empty.",
                nameof(archiveId));
        }
    }

    private static DateTimeOffset ReadTimestamp(
        long utcTicks,
        int offsetMinutes)
    {
        if (offsetMinutes is < -840 or > 840)
        {
            throw new InvalidDataException(
                "The stored timestamp offset is invalid.");
        }

        return new DateTimeOffset(utcTicks, TimeSpan.Zero)
            .ToOffset(TimeSpan.FromMinutes(offsetMinutes));
    }

    private static GachaFactReadRow? ReadCurrent(
        SQLiteConnection connection,
        GachaFactReference reference) =>
        connection.Query<GachaFactReadRow>(
            """
            SELECT g.*, a.PlayerArchiveId AS ArchiveId
            FROM GachaRecords g
            INNER JOIN GameAccounts a ON a.Id = g.GameAccountId
            WHERE g.GameAccountId = ? AND g.ExternalRecordId = ?;
            """,
            reference.GameAccountId.ToString("D"),
            reference.ExternalRecordId)
        .SingleOrDefault();

    private static PortableIdentityStateRow? ReadIdentity(
        SQLiteConnection connection,
        GameRoleIdentityId identityId) =>
        connection.Query<PortableIdentityStateRow>(
                """
                SELECT Id, GameBiz, Server, Uid
                FROM GameRoleIdentities
                WHERE Id = ?;
                """,
                identityId.ToString())
            .SingleOrDefault();

    private static PortableAccountStateRow? ReadPortableAccount(
        SQLiteConnection connection,
        Guid accountId) =>
        connection.Query<PortableAccountStateRow>(
                """
                SELECT Id, PlayerArchiveId, GameRoleIdentityId,
                       UpdatedAtUtcTicks
                FROM GameAccounts
                WHERE Id = ?;
                """,
                accountId.ToString("D"))
            .SingleOrDefault();

    private static bool PortableFactsEqual(
        GachaRecord target,
        GachaRecord source) =>
        target.ItemName == source.ItemName &&
        target.ItemId == source.ItemId &&
        target.ItemType == source.ItemType &&
        target.GachaType == source.GachaType &&
        target.UigfGachaType == source.UigfGachaType &&
        target.RankType == source.RankType &&
        target.Count == source.Count &&
        target.Time.UtcDateTime.Ticks ==
            source.Time.UtcDateTime.Ticks &&
        target.Time.Offset == source.Time.Offset;

    private static GameServerRegion MapServerRegion(
        GameRoleNaturalIdentity identity)
    {
        return identity.Server switch
        {
            "cn_gf01" => GameServerRegion.ChinaOfficial,
            "cn_qd01" => GameServerRegion.ChinaBilibili,
            "os_usa" => GameServerRegion.America,
            "os_euro" => GameServerRegion.Europe,
            "os_asia" => GameServerRegion.Asia,
            "os_cht" => GameServerRegion.TaiwanHongKongMacao,
            _ => throw new PortableApplyConflictException(
                $"Unsupported Genshin server {identity.Server}.")
        };
    }

    private static Guid ReadArchiveId(
        SQLiteConnection connection,
        Guid gameAccountId)
    {
        string? archiveId = connection.ExecuteScalar<string?>(
            "SELECT PlayerArchiveId FROM GameAccounts WHERE Id = ?;",
            gameAccountId.ToString("D"));
        return archiveId is null
            ? throw new InvalidOperationException(
                $"Game account {gameAccountId:D} does not exist.")
            : ParseRequiredGuid(archiveId, "archive");
    }

    private static ActiveTombstone? ReadActiveTombstone(
        SQLiteConnection connection,
        Guid archiveId,
        GachaFactReference reference)
    {
        string? key = TryCreateTombstoneKey(
            connection,
            archiveId,
            reference);
        if (key is null)
        {
            return null;
        }
        TombstoneRow? row = connection.Query<TombstoneRow>(
                """
                SELECT TombstoneKey, TombstoneVersion, ArchiveId, IsActive
                FROM LocalOnlyTombstones
                WHERE TombstoneKey = ? AND IsActive = 1;
                """,
                key)
            .SingleOrDefault();
        return row is null
            ? null
            : new ActiveTombstone(
                row.TombstoneKey,
                row.TombstoneVersion);
    }

    private static string CreateTombstoneKey(
        SQLiteConnection connection,
        Guid archiveId,
        GachaFactReference reference)
    {
        return TryCreateTombstoneKey(
                connection,
                archiveId,
                reference) ??
            throw new InvalidOperationException(
                "Irreversible Gacha deletion requires a resolved role identity.");
    }

    private static string CreateTombstoneKey(
        Guid archiveId,
        GameRoleIdentityId roleIdentityId,
        string externalRecordId)
    {
        string canonical =
            $"gacha|{archiveId:D}|{roleIdentityId.ToString().ToLowerInvariant()}|" +
            externalRecordId;
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string? TryCreateTombstoneKey(
        SQLiteConnection connection,
        Guid archiveId,
        GachaFactReference reference)
    {
        AccountIdentityScopeRow? scope =
            connection.Query<AccountIdentityScopeRow>(
                    """
                    SELECT PlayerArchiveId, GameRoleIdentityId
                    FROM GameAccounts
                    WHERE Id = ?;
                    """,
                    reference.GameAccountId.ToString("D"))
                .SingleOrDefault();
        if (scope is null ||
            !Guid.TryParseExact(
                scope.PlayerArchiveId,
                "D",
                out Guid storedArchiveId) ||
            storedArchiveId != archiveId)
        {
            throw new InvalidDataException(
                "The Gacha fact account has an invalid archive scope.");
        }
        if (string.IsNullOrWhiteSpace(scope.GameRoleIdentityId))
        {
            return null;
        }

        return CreateTombstoneKey(
            archiveId,
            new GameRoleIdentityId(
                ParseRequiredGuid(
                    scope.GameRoleIdentityId,
                    "role identity")),
            reference.ExternalRecordId);
    }

    private static string? SerializeSnapshot(GachaRecordRow? row) =>
        row is null
            ? null
            : JsonSerializer.Serialize(
                GachaRevisionSnapshot.FromRow(row));

    private static GachaRecordRow? DeserializeSnapshot(string? json)
    {
        if (json is null)
        {
            return null;
        }

        GachaRevisionSnapshot snapshot =
            JsonSerializer.Deserialize<GachaRevisionSnapshot>(json) ??
            throw new InvalidDataException(
                "The stored Gacha revision snapshot is invalid.");
        return snapshot.ToRow();
    }

    private static Guid ParseRequiredGuid(string value, string field) =>
        Guid.TryParseExact(value, "D", out Guid parsed) &&
        parsed != Guid.Empty
            ? parsed
            : throw new InvalidDataException(
                $"The stored {field} ID is invalid.");

    private static Guid? ParseOptionalGuid(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : ParseRequiredGuid(value, "change set");

    private sealed record PreparedMutation(
        Guid ArchiveId,
        GachaRecordRow? Before,
        GachaRecordRow? After,
        GachaRecordChangeKind ChangeKind,
        EntityChangeKind EntityChangeKind)
    {
        public GachaFactReference Reference
        {
            get
            {
                GachaRecordRow row = After ?? Before!;
                return new GachaFactReference(
                    Guid.Parse(row.GameAccountId),
                    row.ExternalRecordId);
            }
        }
    }

    private sealed record GachaRevisionSnapshot(
        string GameAccountId,
        string ExternalRecordId,
        string? ItemName,
        string? ItemId,
        string? ItemType,
        string? GachaType,
        string? UigfGachaType,
        int? RankType,
        int Count,
        long TimeUtcTicks,
        int TimeOffsetMinutes,
        int Origin,
        long? FetchedAtUtcTicks,
        int? FetchedAtOffsetMinutes,
        long? ImportedAtUtcTicks,
        int? ImportedAtOffsetMinutes,
        string? AcquisitionBatchId,
        long Version)
    {
        public static GachaRevisionSnapshot FromRow(GachaRecordRow row) =>
            new(
                row.GameAccountId,
                row.ExternalRecordId,
                row.ItemName,
                row.ItemId,
                row.ItemType,
                row.GachaType,
                row.UigfGachaType,
                row.RankType,
                row.Count,
                row.TimeUtcTicks,
                row.TimeOffsetMinutes,
                row.Origin,
                row.FetchedAtUtcTicks,
                row.FetchedAtOffsetMinutes,
                row.ImportedAtUtcTicks,
                row.ImportedAtOffsetMinutes,
                row.AcquisitionBatchId,
                row.Version);

        public GachaRecordRow ToRow() =>
            new()
            {
                GameAccountId = GameAccountId,
                ExternalRecordId = ExternalRecordId,
                ItemName = ItemName,
                ItemId = ItemId,
                ItemType = ItemType,
                GachaType = GachaType,
                UigfGachaType = UigfGachaType,
                RankType = RankType,
                Count = Count,
                TimeUtcTicks = TimeUtcTicks,
                TimeOffsetMinutes = TimeOffsetMinutes,
                Origin = Origin,
                FetchedAtUtcTicks = FetchedAtUtcTicks,
                FetchedAtOffsetMinutes = FetchedAtOffsetMinutes,
                ImportedAtUtcTicks = ImportedAtUtcTicks,
                ImportedAtOffsetMinutes = ImportedAtOffsetMinutes,
                AcquisitionBatchId = AcquisitionBatchId,
                Version = Version
            };
    }

    private sealed class GachaFactReadRow : GachaRecordRow
    {
        public string ArchiveId { get; set; } = string.Empty;

        public GachaRecordRow ToGachaRow() =>
            new()
            {
                Id = Id,
                GameAccountId = GameAccountId,
                ExternalRecordId = ExternalRecordId,
                ItemName = ItemName,
                ItemId = ItemId,
                ItemType = ItemType,
                GachaType = GachaType,
                UigfGachaType = UigfGachaType,
                RankType = RankType,
                Count = Count,
                TimeUtcTicks = TimeUtcTicks,
                TimeOffsetMinutes = TimeOffsetMinutes,
                Origin = Origin,
                FetchedAtUtcTicks = FetchedAtUtcTicks,
                FetchedAtOffsetMinutes = FetchedAtOffsetMinutes,
                ImportedAtUtcTicks = ImportedAtUtcTicks,
                ImportedAtOffsetMinutes = ImportedAtOffsetMinutes,
                AcquisitionBatchId = AcquisitionBatchId,
                Version = Version
            };
    }

    private sealed class CommitResultRow
    {
        public string OperationId { get; set; } = string.Empty;

        public string? ChangeSetId { get; set; }

        public int Status { get; set; }

        public int AffectedRecordCount { get; set; }

        public long CommittedAtUtcTicks { get; set; }
    }

    private sealed class ChangeSetIdRow
    {
        public string ChangeSetId { get; set; } = string.Empty;
    }

    private sealed class ArchiveIdRow
    {
        public string ArchiveId { get; set; } = string.Empty;
    }

    private sealed class UndoMaterialRow
    {
        public string ChangeSetId { get; set; } = string.Empty;

        public string EntityKind { get; set; } = string.Empty;

        public string EntityReference { get; set; } = string.Empty;

        public int ChangeKind { get; set; }

        public long? ExpectedAfterVersion { get; set; }

        public int SnapshotFormatVersion { get; set; }

        public string? BeforeSnapshotJson { get; set; }

        public string? AfterSnapshotJson { get; set; }

        public long MaterialBytes { get; set; }
    }

    private sealed class CleanupCandidateRow
    {
        public string ChangeSetId { get; set; } = string.Empty;

        public long MaterialBytes { get; set; }
    }

    private sealed class OperationHistoryReadRow
    {
        public string ChangeSetId { get; set; } = string.Empty;

        public int OperationKind { get; set; }

        public long CommittedAtUtcTicks { get; set; }

        public int CommittedAtOffsetMinutes { get; set; }

        public string Summary { get; set; } = string.Empty;

        public int AffectedRecordCount { get; set; }

        public bool IsUndoEligible { get; set; }

        public int IneligibilityReason { get; set; }

        public string? UndoneByChangeSetId { get; set; }
    }

    private sealed class TombstoneRow
    {
        public string TombstoneKey { get; set; } = string.Empty;

        public long TombstoneVersion { get; set; }

        public string ArchiveId { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }

    private sealed record ActiveTombstone(
        string TombstoneKey,
        long TombstoneVersion);

    private sealed class AccountIdentityScopeRow
    {
        public string PlayerArchiveId { get; set; } = string.Empty;

        public string? GameRoleIdentityId { get; set; }
    }

    private sealed record PortablePreparation(
        Guid ArchiveId,
        bool CreateArchive,
        IReadOnlyList<GameRoleIdentity> IdentitiesToInsert,
        IReadOnlyList<(
            GameRoleIdentityId Source,
            GameRoleIdentityId Target)> AliasesToInsert,
        IReadOnlyList<PreparedPortableAccount> Accounts,
        IReadOnlyList<PreparedMutation> Mutations,
        IReadOnlyList<ActiveTombstone> ConfirmedReintroductions);

    private sealed record PreparedPortableAccount(
        GachaPortableAccount Source,
        Guid TargetAccountId,
        GameRoleIdentityId TargetIdentityId,
        bool CreateAccount,
        int InsertedRecordCount,
        int SkippedRecordCount)
    {
        public GachaPortableAccountApplyResult ToResult() =>
            new(
                Source.AccountReference,
                TargetAccountId,
                TargetIdentityId,
                InsertedRecordCount,
                SkippedRecordCount);
    }

    private sealed class PortableArchiveStateRow
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public long UpdatedAtUtcTicks { get; set; }
    }

    private sealed class PortableIdentityStateRow
    {
        public string Id { get; set; } = string.Empty;

        public string GameBiz { get; set; } = string.Empty;

        public string Server { get; set; } = string.Empty;

        public string Uid { get; set; } = string.Empty;

        public GameRoleNaturalIdentity ToNaturalIdentity() =>
            new(GameBiz, Server, Uid);
    }

    private sealed class PortableAliasRow
    {
        public string SourceIdentityId { get; set; } = string.Empty;

        public string TargetIdentityId { get; set; } = string.Empty;
    }

    private sealed class PortableAccountStateRow
    {
        public string Id { get; set; } = string.Empty;

        public string PlayerArchiveId { get; set; } = string.Empty;

        public string? GameRoleIdentityId { get; set; }

        public long UpdatedAtUtcTicks { get; set; }
    }

    private sealed class PortableReceiptReadRow
    {
        public string ReceiptId { get; set; } = string.Empty;

        public string TargetArchiveId { get; set; } = string.Empty;

        public int RecordCount { get; set; }
    }

    private sealed class PortableReceiptAccountReadRow
    {
        public string SourceAccountId { get; set; } = string.Empty;

        public string TargetAccountId { get; set; } = string.Empty;

        public string TargetRoleIdentityId { get; set; } = string.Empty;
    }

    private sealed class PortableApplyConflictException(string message)
        : Exception(message);

    private sealed class PortableApplySuppressedException(
        TombstoneReintroductionWarning warning)
        : Exception(
            "The Portable import would reintroduce a locally deleted Gacha fact.")
    {
        public TombstoneReintroductionWarning Warning { get; } =
            warning;
    }

    private sealed class UnknownStorageCapacityProvider
        : IHistoryStorageCapacityProvider
    {
        public static UnknownStorageCapacityProvider Instance { get; } =
            new();

        public HistoryStorageCapacity GetCapacity() =>
            new(null);
    }
}

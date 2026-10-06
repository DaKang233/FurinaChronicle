// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGachaAtomicChangeStore(FurinaDatabase database)
    : IGachaAtomicChangeStore
{
    internal const int SnapshotFormatVersion = 1;
    internal const string EntityKind = "gacha_record";

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
                if (mutation.ExpectedVersion is FactVersion expected &&
                    (current is null || current.Version != expected.Value))
                {
                    result = new GachaAtomicChangeResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        AffectedRecordCount: 0,
                        $"Gacha fact {mutation.Reference} no longer has expected version {expected.Value}.");
                    return;
                }

                Guid archiveId = current is null
                    ? ReadArchiveId(
                        connection,
                        mutation.Reference.GameAccountId)
                    : ParseRequiredGuid(current.ArchiveId, "archive");
                PreparedMutation? item = PrepareMutation(
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

            foreach (PreparedMutation mutation in prepared)
            {
                ApplyBusinessMutation(connection, mutation);
            }

            PreparedMutation[] substantive = prepared
                .Where(mutation =>
                    mutation.ChangeKind !=
                    GachaRecordChangeKind.AcquisitionMetadataOnly)
                .ToArray();
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

    private static PreparedMutation? PrepareMutation(
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
            after.Version = 1;
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

            foreach (ChangeSetIdRow row in connection.Query<ChangeSetIdRow>(
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
                limit))
            {
                evictions.Add(row.ChangeSetId);
            }
        }

        foreach (string changeSetId in evictions)
        {
            connection.Execute(
                """
                UPDATE OperationHistory
                SET IsUndoEligible = 0, IneligibilityReason = ?,
                    MaterialBytes = 0
                WHERE ChangeSetId = ?;
                """,
                (int)UndoIneligibilityReason.CapacityEvicted,
                changeSetId);
            connection.Execute(
                "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
                changeSetId);
        }
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

    private static string? SerializeSnapshot(GachaRecordRow? row) =>
        row is null
            ? null
            : JsonSerializer.Serialize(
                GachaRevisionSnapshot.FromRow(row));

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
}

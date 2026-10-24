// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private static void InsertArchiveRestoreChangeSet(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        Guid changeSetId,
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
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL);
            """,
            changeSetId.ToString("D"),
            request.OperationId.ToString(),
            (int)DataChangeOperationKind.Import,
            request.StartedAt.UtcDateTime.Ticks,
            checked((int)request.StartedAt.Offset.TotalMinutes),
            request.CommittedAt.UtcDateTime.Ticks,
            checked((int)request.CommittedAt.Offset.TotalMinutes),
            (int)DataOrigin.FurinaImport,
            request.Summary,
            affectedRecordCount);
        foreach (Guid archiveId in archiveIds.Distinct())
        {
            connection.Execute(
                "INSERT INTO DataChangeSetArchives (ChangeSetId, ArchiveId) VALUES (?, ?);",
                changeSetId.ToString("D"),
                archiveId.ToString("D"));
        }
    }

    private static void ApplyArchiveRestoreTombstones(
        SQLiteConnection connection,
        Guid changeSetId,
        IEnumerable<ArchiveRestoreTombstoneHit> tombstones)
    {
        foreach (ArchiveRestoreTombstoneHit tombstone in tombstones)
        {
            int changed = connection.Execute(
                """
                UPDATE LocalOnlyTombstones
                SET IsActive = 0, ReintroducedByChangeSetId = ?
                WHERE TombstoneKey = ? AND TombstoneVersion = ?
                    AND IsActive = 1;
                """,
                changeSetId.ToString("D"),
                tombstone.TombstoneKey,
                tombstone.TombstoneVersion);
            if (changed != 1)
            {
                throw new ArchiveRestoreApplyConflictException(
                    "A confirmed Tombstone changed while applying the Archive restore.");
            }
        }
    }

    private static void ApplyArchiveRestoreSettings(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        PreparedArchiveRestore prepared)
    {
        foreach (ArchiveRestoreSettingDifference difference in
                 request.Plan.Settings.Differences.Where(item => item.WillApply))
        {
            if (difference.Setting == "RequireUniqueArchiveNames")
            {
                bool value = bool.Parse(difference.SourceValue ??
                    throw new InvalidDataException(
                        "The Archive unique-name setting has no source value."));
                if (value && connection.ExecuteScalar<int>(
                        """
                        SELECT COUNT(*) FROM
                        (
                            SELECT Name FROM PlayerArchives
                            GROUP BY Name HAVING COUNT(*) > 1
                        );
                        """) != 0)
                {
                    throw new ArchiveRestoreApplyConflictException(
                        "Unique Archive names cannot be enabled while duplicate names exist.");
                }
                connection.Execute(
                    "UPDATE PortableApplicationSettings SET RequireUniqueArchiveNames = ? WHERE Id = 1;",
                    value);
                continue;
            }
            if (difference.Setting == "UndoLimit" &&
                difference.SourceArchiveReference is Guid sourceArchiveId)
            {
                Guid targetArchiveId = prepared.TargetArchives[sourceArchiveId];
                int limit = int.Parse(
                    difference.SourceValue ?? throw new InvalidDataException(
                        "The Archive Undo limit has no source value."),
                    CultureInfo.InvariantCulture);
                connection.Execute(
                    """
                    INSERT INTO ArchiveUndoSettings (ArchiveId, UndoLimit)
                    VALUES (?, ?)
                    ON CONFLICT(ArchiveId)
                    DO UPDATE SET UndoLimit = excluded.UndoLimit;
                    """,
                    targetArchiveId.ToString("D"),
                    limit);
                continue;
            }
            throw new InvalidDataException(
                $"Unsupported portable setting '{difference.Setting}'.");
        }
    }

    private static void InvalidateArchiveRestoreChronicle(
        SQLiteConnection connection,
        PreparedArchiveRestore prepared)
    {
        var scopes = new HashSet<(Guid ArchiveId, Guid AccountId)>();
        foreach (PreparedMutation mutation in prepared.GachaMutations)
        {
            scopes.Add((
                mutation.ArchiveId,
                mutation.Reference.GameAccountId));
        }
        foreach (PreparedDomainMutation mutation in
                 prepared.ItemMutations
                     .Concat(prepared.ManualMutations)
                     .Concat(prepared.ChallengeMutations))
        {
            ArchiveDomainFactRow row = mutation.After ?? mutation.Before ??
                throw new InvalidDataException(
                    "An Archive domain mutation has no fact identity.");
            scopes.Add((
                mutation.ArchiveId,
                ParseRequiredGuid(row.GameAccountId, "game account")));
        }
        foreach ((Guid archiveId, Guid accountId) in scopes)
        {
            connection.Execute(
                """
                UPDATE ChronicleGenerations
                SET IsInvalidated = 1
                WHERE (ScopeKind = 1 AND ScopeId = ?)
                   OR (ScopeKind = 2 AND ScopeId = ?);
                """,
                accountId.ToString("D"),
                archiveId.ToString("D"));
        }
        if (scopes.Count > 0)
        {
            connection.Execute(
                """
                DELETE FROM ChronicleEntries
                WHERE GenerationId IN
                    (SELECT GenerationId FROM ChronicleGenerations
                     WHERE IsInvalidated = 1);
                """);
        }
    }

    private static void InsertArchiveRestoreReceipt(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        Guid receiptId,
        Guid? changeSetId,
        ChangeExecutionStatus status,
        int affectedRecordCount,
        int factCount)
    {
        connection.Execute(
            """
            INSERT INTO ArchiveRestoreReceipts
                (ReceiptId, OperationId, ChangeSetId, PackageFingerprint,
                 PlanFingerprint, Status, ReceivedAtUtcTicks,
                 ReceivedAtOffsetMinutes, AffectedRecordCount,
                 ArchiveCount, AccountCount, FactCount)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
            """,
            receiptId.ToString("D"),
            request.OperationId.ToString(),
            changeSetId?.ToString("D"),
            request.Package.PackageFingerprint,
            request.Plan.PlanFingerprint,
            (int)status,
            request.CommittedAt.UtcDateTime.Ticks,
            checked((int)request.CommittedAt.Offset.TotalMinutes),
            affectedRecordCount,
            request.Plan.Archives.Count,
            request.Plan.Accounts.Count,
            factCount);
        foreach (ArchiveRestoreArchiveMapping mapping in request.Plan.Archives)
        {
            Guid targetArchiveId = mapping.TargetArchiveId ??
                mapping.ProposedArchiveId ??
                throw new InvalidDataException(
                    "The Archive receipt mapping has no target ID.");
            connection.Execute(
                """
                INSERT INTO ArchiveRestoreReceiptArchives
                    (ReceiptId, SourceArchiveReference, TargetArchiveId)
                VALUES (?, ?, ?);
                """,
                receiptId.ToString("D"),
                mapping.SourceArchiveReference.ToString("D"),
                targetArchiveId.ToString("D"));
        }
        foreach (ArchiveRestoreAccountMapping mapping in request.Plan.Accounts)
        {
            Guid targetArchiveId = mapping.TargetArchiveId ??
                throw new InvalidDataException(
                    "The account receipt mapping has no target Archive.");
            Guid targetAccountId = mapping.TargetAccountId ??
                mapping.ProposedAccountId ??
                throw new InvalidDataException(
                    "The account receipt mapping has no target account.");
            connection.Execute(
                """
                INSERT INTO ArchiveRestoreReceiptAccounts
                    (ReceiptId, SourceArchiveReference,
                     SourceAccountReference, TargetArchiveId,
                     TargetAccountId, TargetRoleIdentityId)
                VALUES (?, ?, ?, ?, ?, ?);
                """,
                receiptId.ToString("D"),
                mapping.SourceArchiveReference.ToString("D"),
                mapping.SourceAccountReference.ToString("D"),
                targetArchiveId.ToString("D"),
                targetAccountId.ToString("D"),
                mapping.TargetRoleIdentityId?.ToString("D"));
        }
    }

    private static void InsertArchiveRestoreCommitResult(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request,
        Guid? changeSetId,
        ChangeExecutionStatus status,
        int affectedRecordCount) =>
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

    private static ArchiveRestoreReceipt? ReadArchiveRestoreReceipt(
        SQLiteConnection connection,
        OperationId operationId)
    {
        ArchiveRestoreReceiptRow? row = connection
            .Query<ArchiveRestoreReceiptRow>(
                """
                SELECT ReceiptId, OperationId, ChangeSetId,
                       PackageFingerprint, PlanFingerprint, Status,
                       ReceivedAtUtcTicks, ReceivedAtOffsetMinutes,
                       AffectedRecordCount, ArchiveCount, AccountCount,
                       FactCount
                FROM ArchiveRestoreReceipts
                WHERE OperationId = ?;
                """,
                operationId.ToString())
            .SingleOrDefault();
        if (row is null)
        {
            return null;
        }
        ArchiveRestoreReceiptArchiveMapping[] archives = connection
            .Query<ArchiveRestoreReceiptArchiveRow>(
                """
                SELECT ReceiptId, SourceArchiveReference, TargetArchiveId
                FROM ArchiveRestoreReceiptArchives
                WHERE ReceiptId = ?
                ORDER BY SourceArchiveReference;
                """,
                row.ReceiptId)
            .Select(item => new ArchiveRestoreReceiptArchiveMapping(
                ParseRequiredGuid(
                    item.SourceArchiveReference,
                    "source Archive reference"),
                ParseRequiredGuid(item.TargetArchiveId, "target Archive")))
            .ToArray();
        ArchiveRestoreReceiptAccountMapping[] accounts = connection
            .Query<ArchiveRestoreReceiptAccountRow>(
                """
                SELECT ReceiptId, SourceArchiveReference,
                       SourceAccountReference, TargetArchiveId,
                       TargetAccountId, TargetRoleIdentityId
                FROM ArchiveRestoreReceiptAccounts
                WHERE ReceiptId = ?
                ORDER BY SourceArchiveReference, SourceAccountReference;
                """,
                row.ReceiptId)
            .Select(item => new ArchiveRestoreReceiptAccountMapping(
                ParseRequiredGuid(
                    item.SourceArchiveReference,
                    "source Archive reference"),
                ParseRequiredGuid(
                    item.SourceAccountReference,
                    "source account reference"),
                ParseRequiredGuid(item.TargetArchiveId, "target Archive"),
                ParseRequiredGuid(item.TargetAccountId, "target account"),
                ParseOptionalGuid(item.TargetRoleIdentityId)))
            .ToArray();
        if (archives.Length != row.ArchiveCount ||
            accounts.Length != row.AccountCount)
        {
            throw new InvalidDataException(
                "The Archive restore receipt mapping count is inconsistent.");
        }
        return new ArchiveRestoreReceipt(
            ParseRequiredGuid(row.ReceiptId, "Archive restore receipt"),
            new OperationId(ParseRequiredGuid(row.OperationId, "operation")),
            ParseOptionalGuid(row.ChangeSetId),
            row.PackageFingerprint,
            row.PlanFingerprint,
            (ChangeExecutionStatus)row.Status,
            new DateTimeOffset(
                new DateTime(row.ReceivedAtUtcTicks, DateTimeKind.Utc))
                .ToOffset(TimeSpan.FromMinutes(row.ReceivedAtOffsetMinutes)),
            row.AffectedRecordCount,
            row.FactCount,
            archives,
            accounts);
    }

    private static bool ReceiptMatches(
        ArchiveRestoreReceipt receipt,
        ArchiveRestoreApplyRequest request) =>
        receipt.OperationId == request.OperationId &&
        string.Equals(
            receipt.PackageFingerprint,
            request.Package.PackageFingerprint,
            StringComparison.Ordinal) &&
        string.Equals(
            receipt.PlanFingerprint,
            request.Plan.PlanFingerprint,
            StringComparison.Ordinal);

    private static string ComputeTransactionStateToken(
        SQLiteConnection connection)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"furina-archive-restore-state-{Guid.NewGuid():N}.db");
        try
        {
            connection.Backup(path, "main");
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream))
                .ToLowerInvariant();
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}

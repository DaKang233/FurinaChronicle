// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private const int FurinaDatabaseApplicationId = 0x46554348;

    private sealed record PreparedArchiveRecoveryPoint(
        Guid RecoveryPointId,
        string FileName,
        string Path,
        IReadOnlyList<string> SupersededFileNames);

    public async Task<ArchiveReplaceRecoveryPoint?>
        FindCurrentRecoveryPointAsync(
            CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ArchiveReplaceRecoveryPointRow? row = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            row = connection.Query<ArchiveReplaceRecoveryPointRow>(
                    """
                    SELECT RecoveryPointId, OperationId, FileName,
                           PackageFingerprint, PlanFingerprint,
                           DatabaseApplicationId, DatabaseSchemaVersion,
                           Length, Sha256, CreatedAtUtcTicks,
                           CreatedAtOffsetMinutes, CommittedAtUtcTicks,
                           CommittedAtOffsetMinutes, IsCurrent
                    FROM ArchiveReplaceRecoveryPoints
                    WHERE IsCurrent = 1;
                    """)
                .SingleOrDefault();
        });
        if (row is null)
        {
            return null;
        }
        string path = GetRecoveryPointPath(row.FileName);
        bool valid = await ValidateRecoveryPointFileAsync(
            path,
            row,
            cancellationToken);
        return ToRecoveryPoint(row, valid);
    }

    public async Task<int> CleanupOrphanedRecoveryPointsAsync(
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        await ArchiveRestoreWriterGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string directory = GetRecoveryPointDirectory();
            if (!Directory.Exists(directory))
            {
                return 0;
            }
            List<string> referenced = await database.Connection
                .QueryScalarsAsync<string>(
                    "SELECT FileName FROM ArchiveReplaceRecoveryPoints;");
            HashSet<string> names = referenced.ToHashSet(
                StringComparer.Ordinal);
            int removed = 0;
            foreach (string path in Directory.EnumerateFiles(
                         directory,
                         "replace-*.db",
                         SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (names.Contains(Path.GetFileName(path)))
                {
                    continue;
                }
                File.Delete(path);
                removed++;
            }
            return removed;
        }
        finally
        {
            ArchiveRestoreWriterGate.Release();
        }
    }

    private string GetRecoveryPointDirectory() =>
        Path.Combine(
            Path.GetDirectoryName(database.DatabasePath) ??
                throw new InvalidOperationException(
                    "The database path has no parent directory."),
            "archive-recovery");

    private string GetRecoveryPointPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(
                Path.GetFileName(fileName),
                fileName,
                StringComparison.Ordinal) ||
            !fileName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The Archive recovery point file name is invalid.");
        }
        return Path.Combine(GetRecoveryPointDirectory(), fileName);
    }

    private static async Task<bool> ValidateRecoveryPointFileAsync(
        string path,
        ArchiveReplaceRecoveryPointRow row,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != row.Length)
            {
                return false;
            }
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            string hash = Convert.ToHexString(
                    await SHA256.HashDataAsync(stream, cancellationToken))
                .ToLowerInvariant();
            if (!string.Equals(hash, row.Sha256, StringComparison.Ordinal))
            {
                return false;
            }
            using var connection = new SQLiteConnection(
                path,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex);
            return connection.ExecuteScalar<int>("PRAGMA application_id;") ==
                    row.DatabaseApplicationId &&
                connection.ExecuteScalar<int>("PRAGMA user_version;") ==
                    row.DatabaseSchemaVersion &&
                string.Equals(
                    connection.ExecuteScalar<string>("PRAGMA quick_check;"),
                    "ok",
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            SQLiteException or CryptographicException)
        {
            return false;
        }
    }

    private static ArchiveReplaceRecoveryPoint ToRecoveryPoint(
        ArchiveReplaceRecoveryPointRow row,
        bool valid) => new(
        ParseRequiredGuid(row.RecoveryPointId, "Archive recovery point"),
        new OperationId(ParseRequiredGuid(row.OperationId, "operation")),
        row.FileName,
        row.PackageFingerprint,
        row.PlanFingerprint,
        row.DatabaseApplicationId,
        row.DatabaseSchemaVersion,
        row.Length,
        row.Sha256,
        new DateTimeOffset(
            new DateTime(row.CreatedAtUtcTicks, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromMinutes(row.CreatedAtOffsetMinutes)),
        new DateTimeOffset(
            new DateTime(row.CommittedAtUtcTicks, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromMinutes(row.CommittedAtOffsetMinutes)),
        row.IsCurrent != 0,
        valid);

    private PreparedArchiveRecoveryPoint CreateArchiveRecoveryPoint(
        SQLiteConnection connection,
        ArchiveRestoreApplyRequest request)
    {
        string directory = GetRecoveryPointDirectory();
        Directory.CreateDirectory(directory);
        Guid recoveryPointId = Guid.NewGuid();
        string fileName = $"replace-{recoveryPointId:N}.db";
        string path = GetRecoveryPointPath(fileName);
        try
        {
            connection.Backup(path, "main");
            var info = new FileInfo(path);
            string sha256;
            using (var stream = new FileStream(
                       path,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       64 * 1024,
                       FileOptions.SequentialScan))
            {
                sha256 = Convert.ToHexString(SHA256.HashData(stream))
                    .ToLowerInvariant();
            }
            using (var snapshot = new SQLiteConnection(
                       path,
                       SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex))
            {
                if (snapshot.ExecuteScalar<int>("PRAGMA application_id;") !=
                        FurinaDatabaseApplicationId ||
                    snapshot.ExecuteScalar<int>("PRAGMA user_version;") !=
                        ArchiveRestoreSchemaVersion ||
                    !string.Equals(
                        snapshot.ExecuteScalar<string>("PRAGMA quick_check;"),
                        "ok",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "The Archive Replace recovery point failed validation.");
                }
            }

            string[] superseded = connection
                .QueryScalars<string>(
                    "SELECT FileName FROM ArchiveReplaceRecoveryPoints;")
                .ToArray();
            connection.Execute("DELETE FROM ArchiveReplaceRecoveryPoints;");
            connection.Execute(
                """
                INSERT INTO ArchiveReplaceRecoveryPoints
                    (RecoveryPointId, OperationId, FileName,
                     PackageFingerprint, PlanFingerprint,
                     DatabaseApplicationId, DatabaseSchemaVersion,
                     Length, Sha256, CreatedAtUtcTicks,
                     CreatedAtOffsetMinutes, CommittedAtUtcTicks,
                     CommittedAtOffsetMinutes, IsCurrent)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1);
                """,
                recoveryPointId.ToString("D"),
                request.OperationId.ToString(),
                fileName,
                request.Package.PackageFingerprint,
                request.Plan.PlanFingerprint,
                FurinaDatabaseApplicationId,
                ArchiveRestoreSchemaVersion,
                info.Length,
                sha256,
                request.StartedAt.UtcDateTime.Ticks,
                checked((int)request.StartedAt.Offset.TotalMinutes),
                request.CommittedAt.UtcDateTime.Ticks,
                checked((int)request.CommittedAt.Offset.TotalMinutes));
            return new PreparedArchiveRecoveryPoint(
                recoveryPointId,
                fileName,
                path,
                superseded);
        }
        catch
        {
            TryDeleteRecoveryPoint(path);
            throw;
        }
    }

    private static void InvalidateArchiveReplaceUndo(
        SQLiteConnection connection,
        ArchiveRestorePlan plan)
    {
        var selected = new HashSet<(Guid AccountId, string EntityKind)>();
        foreach (ArchiveReplaceSelection selection in plan.ReplaceSelections)
        {
            IEnumerable<ArchiveRestoreAccountMapping> scopedAccounts =
                selection.Kind == ArchiveReplaceScopeKind.Archive
                    ? plan.Accounts.Where(item =>
                        item.SourceArchiveReference ==
                            selection.SourceArchiveReference)
                    : plan.Accounts.Where(item =>
                        item.SourceAccountReference ==
                            selection.SourceAccountReference);
            IReadOnlySet<ArchiveReplaceDomain> domains =
                selection.Kind == ArchiveReplaceScopeKind.Domain
                    ? new HashSet<ArchiveReplaceDomain>
                    {
                        selection.Domain!.Value,
                    }
                    : Enum.GetValues<ArchiveReplaceDomain>().ToHashSet();
            foreach (ArchiveRestoreAccountMapping mapping in scopedAccounts)
            {
                Guid targetAccountId = mapping.TargetAccountId ??
                    mapping.ProposedAccountId ??
                    throw new InvalidDataException(
                        "A Replace mapping has no target account.");
                foreach (ArchiveReplaceDomain domain in domains)
                {
                    selected.Add((targetAccountId, domain switch
                    {
                        ArchiveReplaceDomain.Gacha => EntityKind,
                        ArchiveReplaceDomain.ItemObservation =>
                            ItemObservationEntityKind,
                        ArchiveReplaceDomain.ManualAcquisition =>
                            ManualAcquisitionEntityKind,
                        ArchiveReplaceDomain.ChallengeObservation =>
                            ChallengeObservationEntityKind,
                        _ => throw new ArgumentOutOfRangeException(),
                    }));
                }
            }
        }
        foreach (ArchiveRestoreAccountRemoval removal in plan.AccountRemovals)
        foreach (string entityKind in new[]
                 {
                     EntityKind,
                     ItemObservationEntityKind,
                     ManualAcquisitionEntityKind,
                     ChallengeObservationEntityKind,
                 })
        {
            selected.Add((removal.TargetAccountId, entityKind));
        }

        var affected = new HashSet<string>(StringComparer.Ordinal);
        foreach (UndoMaterialRow material in connection.Query<UndoMaterialRow>(
                     """
                     SELECT u.ChangeSetId, u.EntityKind, u.EntityReference,
                            u.ChangeKind, u.ExpectedAfterVersion,
                            u.SnapshotFormatVersion, u.BeforeSnapshotJson,
                            u.AfterSnapshotJson, u.MaterialBytes
                     FROM UndoMaterials u
                     INNER JOIN OperationHistory h
                         ON h.ChangeSetId = u.ChangeSetId
                     WHERE h.IsUndoEligible = 1;
                     """))
        {
            foreach ((Guid accountId, string entityKind) in selected)
            {
                if (string.Equals(
                        material.EntityKind,
                        entityKind,
                        StringComparison.Ordinal) &&
                    material.EntityReference.StartsWith(
                        $"{accountId:D}/",
                        StringComparison.Ordinal))
                {
                    affected.Add(material.ChangeSetId);
                    break;
                }
            }
        }
        foreach (string changeSetId in affected)
        {
            connection.Execute(
                """
                UPDATE OperationHistory
                SET IsUndoEligible = 0,
                    IneligibilityReason = ?,
                    MaterialBytes = 0
                WHERE ChangeSetId = ?;
                """,
                (int)UndoIneligibilityReason.SupersededByArchiveReplace,
                changeSetId);
            connection.Execute(
                "DELETE FROM UndoMaterials WHERE ChangeSetId = ?;",
                changeSetId);
        }
    }

    private void DeleteSupersededRecoveryPointFiles(
        PreparedArchiveRecoveryPoint prepared)
    {
        foreach (string fileName in prepared.SupersededFileNames)
        {
            TryDeleteRecoveryPoint(GetRecoveryPointPath(fileName));
        }
    }

    private static void TryDeleteRecoveryPoint(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Startup or explicit orphan cleanup can retry later.
        }
        catch (UnauthorizedAccessException)
        {
            // Keep the unreferenced private file for later cleanup.
        }
    }
}

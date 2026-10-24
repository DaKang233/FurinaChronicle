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
}

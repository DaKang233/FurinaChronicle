// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha.Portable;
using SQLite;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed class SqliteGachaPortableExportSnapshotFactory(
    FurinaDatabase database,
    SqliteDatabaseOptions options,
    TimeProvider timeProvider)
    : IGachaPortableExportSnapshotFactory
{
    public async Task<IGachaPortableExportSnapshot> OpenAsync(
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "The player archive ID cannot be empty.",
                nameof(playerArchiveId));
        }

        ArgumentNullException.ThrowIfNull(gameAccountIds);
        Guid[] selectedAccountIds = gameAccountIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();
        if (selectedAccountIds.Length != gameAccountIds.Count ||
            selectedAccountIds.Length == 0)
        {
            throw new ArgumentException(
                "At least one unique, non-empty game account ID is required.",
                nameof(gameAccountIds));
        }

        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var connection = new SQLiteConnection(
            options.DatabasePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
        try
        {
            connection.BusyTimeout = TimeSpan.FromSeconds(5);
            connection.BeginTransaction();

            PlayerArchiveRow? archive = connection
                .Query<PlayerArchiveRow>(
                    """
                    SELECT Id, Name, CreatedAtUtcTicks, UpdatedAtUtcTicks
                    FROM PlayerArchives
                    WHERE Id = ?
                    LIMIT 1;
                    """,
                    playerArchiveId.ToString("D"))
                .FirstOrDefault();
            if (archive is null)
            {
                throw new KeyNotFoundException(
                    "The player archive selected for export does not exist.");
            }

            var exportAccounts = new List<GachaPortableExportAccount>(
                selectedAccountIds.Length);
            foreach (Guid accountId in selectedAccountIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GameAccountReadRow? row = connection
                    .Query<GameAccountReadRow>(
                        """
                        SELECT
                            ga.Id,
                            ga.PlayerArchiveId,
                            ga.GameRoleIdentityId,
                            ga.Uid,
                            ga.ServerRegion,
                            ga.DisplayName,
                            ga.IsPlaceholder,
                            ga.CreatedAtUtcTicks,
                            ga.UpdatedAtUtcTicks,
                            gri.GameBiz AS IdentityGameBiz,
                            gri.Server AS IdentityServer,
                            gri.Uid AS IdentityUid
                        FROM GameAccounts AS ga
                        LEFT JOIN GameRoleIdentities AS gri
                            ON gri.Id = ga.GameRoleIdentityId
                        WHERE ga.Id = ? AND ga.PlayerArchiveId = ?
                        LIMIT 1;
                        """,
                        accountId.ToString("D"),
                        playerArchiveId.ToString("D"))
                    .FirstOrDefault();
                if (row is null)
                {
                    throw new InvalidOperationException(
                        "A selected game account does not belong to the source archive.");
                }

                GameAccount account = row.ToDomain();
                if (account.RoleIdentity is null)
                {
                    throw new GachaPortableException(
                        GachaPortableErrorCode.UnsupportedAccount,
                        $"Game account {account.Id:D} has an unresolved role identity.");
                }

                int recordCount = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GachaRecords WHERE GameAccountId = ?;",
                    account.Id.ToString("D"));
                exportAccounts.Add(new GachaPortableExportAccount(
                    account.Id,
                    account.RoleIdentity,
                    account.DisplayName,
                    recordCount));
            }

            return new Snapshot(
                connection,
                timeProvider.GetUtcNow(),
                new GachaPortableArchive(playerArchiveId, archive.Name),
                exportAccounts);
        }
        catch
        {
            if (connection.IsInTransaction)
            {
                connection.Rollback();
            }

            connection.Dispose();
            throw;
        }
    }

    private sealed class Snapshot(
        SQLiteConnection connection,
        DateTimeOffset generatedAt,
        GachaPortableArchive sourceArchive,
        IReadOnlyList<GachaPortableExportAccount> accounts)
        : IGachaPortableExportSnapshot
    {
        private const int PageSize = 500;
        private readonly HashSet<Guid> accountReferences =
            accounts.Select(account => account.AccountReference).ToHashSet();
        private bool disposed;

        public DateTimeOffset GeneratedAt { get; } = generatedAt;

        public GachaPortableArchive SourceArchive { get; } = sourceArchive;

        public IReadOnlyList<GachaPortableExportAccount> Accounts { get; } =
            accounts;

        public async IAsyncEnumerable<GachaRecord> ReadRecordsAsync(
            Guid accountReference,
            [EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!accountReferences.Contains(accountReference))
            {
                throw new ArgumentException(
                    "The account is not part of this export snapshot.",
                    nameof(accountReference));
            }

            long lastRowId = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<GachaRecordRow> rows = connection.Query<GachaRecordRow>(
                    """
                    SELECT
                        Id,
                        GameAccountId,
                        ExternalRecordId,
                        ItemName,
                        ItemId,
                        ItemType,
                        GachaType,
                        UigfGachaType,
                        RankType,
                        Count,
                        TimeUtcTicks,
                        TimeOffsetMinutes,
                        Origin,
                        FetchedAtUtcTicks,
                        FetchedAtOffsetMinutes,
                        ImportedAtUtcTicks,
                        ImportedAtOffsetMinutes,
                        AcquisitionBatchId
                    FROM GachaRecords
                    WHERE GameAccountId = ? AND Id > ?
                    ORDER BY Id
                    LIMIT ?;
                    """,
                    accountReference.ToString("D"),
                    lastRowId,
                    PageSize);
                if (rows.Count == 0)
                {
                    yield break;
                }

                foreach (GachaRecordRow row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lastRowId = row.Id;
                    yield return row.ToDomain();
                }

                await Task.Yield();
            }
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            if (connection.IsInTransaction)
            {
                connection.Rollback();
            }

            connection.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

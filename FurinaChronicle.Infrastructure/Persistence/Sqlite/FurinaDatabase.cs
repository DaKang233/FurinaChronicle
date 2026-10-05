// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabase : IAsyncDisposable
{
    private const int CurrentSchemaVersion = 3;
    private const int PreSharedIdentitySchemaVersion = 2;
    private const int LegacyGachaSchemaVersion = 1;
    private const string LegacyGachaRecordsTableName = "WishRecords";
    private const string LegacyGachaRecordsUniqueIndexName =
        "UX_WishRecords_Account_ExternalId";
    private const string LegacyGachaRecordsTimeIndexName =
        "IX_WishRecords_TimeUtcTicks";
    private const string LegacyGachaRecordsAccountIndexName =
        "IX_WishRecords_GameAccountId";

    // "FUCH" distinguishes the current v1 generation from pre-release
    // databases that also used PRAGMA user_version values such as 1.
    private const int CurrentApplicationId = 0x46554348;

    private readonly SemaphoreSlim initializeGate = new(1, 1);
    private bool initialized;

    internal SQLiteAsyncConnection Connection { get; }

    public FurinaDatabase(SqliteDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? directory = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        SQLiteOpenFlags flags =
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create |
            SQLiteOpenFlags.FullMutex;
        Connection = new SQLiteAsyncConnection(
            options.DatabasePath,
            flags,
            storeDateTimeAsTicks: true);
    }

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializeGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await Connection.SetBusyTimeoutAsync(TimeSpan.FromSeconds(5));
            cancellationToken.ThrowIfCancellationRequested();
            await Connection.ExecuteAsync("PRAGMA foreign_keys = ON;");

            int schemaVersion = await Connection.ExecuteScalarAsync<int>(
                "PRAGMA user_version;");
            int applicationId = await Connection.ExecuteScalarAsync<int>(
                "PRAGMA application_id;");

            if (applicationId == CurrentApplicationId &&
                schemaVersion == CurrentSchemaVersion)
            {
                initialized = true;
                return;
            }

            if (applicationId == CurrentApplicationId &&
                schemaVersion == LegacyGachaSchemaVersion)
            {
                await MigrateVersionOneToVersionThreeAsync(cancellationToken);
            }
            else if (applicationId == CurrentApplicationId &&
                     schemaVersion == PreSharedIdentitySchemaVersion)
            {
                await MigrateVersionTwoToVersionThreeAsync(cancellationToken);
            }
            else if (applicationId == 0 &&
                     schemaVersion == 0 &&
                     await IsEmptyDatabaseAsync(cancellationToken))
            {
                await CreateCurrentSchemaAsync(cancellationToken);
            }
            else
            {
                throw new FurinaDatabaseCompatibilityException(
                    applicationId,
                    schemaVersion,
                    CurrentApplicationId,
                    CurrentSchemaVersion);
            }

            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    private async Task MigrateVersionOneToVersionThreeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            connection.Execute(
                $"DROP INDEX IF EXISTS {LegacyGachaRecordsUniqueIndexName};");
            connection.Execute(
                $"DROP INDEX IF EXISTS {LegacyGachaRecordsTimeIndexName};");
            connection.Execute(
                $"DROP INDEX IF EXISTS {LegacyGachaRecordsAccountIndexName};");
            connection.Execute(
                $"ALTER TABLE {LegacyGachaRecordsTableName} " +
                "RENAME TO GachaRecords;");
            CreateGachaRecordIndexes(connection);
            MigrateVersionTwoToVersionThree(connection, cancellationToken);
        });
    }

    private async Task MigrateVersionTwoToVersionThreeAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            MigrateVersionTwoToVersionThree(connection, cancellationToken);
        });
    }

    private static void MigrateVersionTwoToVersionThree(
        SQLiteConnection connection,
        CancellationToken cancellationToken)
    {
        CreateGameRoleIdentityTable(connection);
        connection.Execute(
            """
            ALTER TABLE GameAccounts
            ADD COLUMN GameRoleIdentityId TEXT
                REFERENCES GameRoleIdentities(Id);
            """);

        List<LegacyGameAccountIdentityRow> accounts = connection
            .Query<LegacyGameAccountIdentityRow>(
                """
                SELECT Id, Uid, ServerRegion, IsPlaceholder
                FROM GameAccounts;
                """);

        foreach (LegacyGameAccountIdentityRow account in accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameServerRegion storedRegion =
                (GameServerRegion)account.ServerRegion;
            GameServerRegion inferredRegion =
                GameServerRegionResolver.Resolve(account.Uid);
            GameServerRegion resolvedRegion =
                inferredRegion == GameServerRegion.Unknown
                    ? storedRegion
                    : inferredRegion;

            if (account.IsPlaceholder ||
                !GenshinGameRoleIdentity.TryCreate(
                    account.Uid,
                    resolvedRegion,
                    out GameRoleNaturalIdentity? naturalIdentity))
            {
                continue;
            }

            var roleIdentity = new GameRoleIdentity(
                GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
                naturalIdentity);
            GameRoleIdentityRow row =
                GameRoleIdentityRow.FromDomain(roleIdentity);

            connection.Execute(
                """
                INSERT OR IGNORE INTO GameRoleIdentities
                    (Id, GameBiz, Server, Uid)
                VALUES (?, ?, ?, ?);
                """,
                row.Id,
                row.GameBiz,
                row.Server,
                row.Uid);

            int matchingIdentityCount = connection.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM GameRoleIdentities
                WHERE Id = ? AND GameBiz = ? AND Server = ? AND Uid = ?;
                """,
                row.Id,
                row.GameBiz,
                row.Server,
                row.Uid);
            if (matchingIdentityCount != 1)
            {
                throw new InvalidDataException(
                    $"Role identity collision while migrating game account {account.Id}.");
            }

            connection.Execute(
                """
                UPDATE GameAccounts
                SET GameRoleIdentityId = ?, ServerRegion = ?
                WHERE Id = ?;
                """,
                row.Id,
                (int)resolvedRegion,
                account.Id);
        }

        connection.Execute(
            "DROP INDEX IF EXISTS UX_GameAccounts_PlayerArchiveId_Uid;");
        CreateGameAccountIdentityIndexes(connection);
        connection.Execute(
            $"PRAGMA user_version = {CurrentSchemaVersion};");
    }

    private async Task<bool> IsEmptyDatabaseAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int schemaObjectCount = await Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE name NOT LIKE 'sqlite_%';
            """);

        cancellationToken.ThrowIfCancellationRequested();
        return schemaObjectCount == 0;
    }

    private async Task CreateCurrentSchemaAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            connection.Execute(
                """
                CREATE TABLE PlayerArchives
                (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT NOT NULL,
                    CreatedAtUtcTicks INTEGER NOT NULL,
                    UpdatedAtUtcTicks INTEGER NOT NULL
                );
                """);

            CreateGameRoleIdentityTable(connection);

            connection.Execute(
                """
                CREATE TABLE GameAccounts
                (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PlayerArchiveId TEXT NOT NULL,
                    GameRoleIdentityId TEXT,
                    Uid TEXT NOT NULL,
                    ServerRegion INTEGER NOT NULL,
                    DisplayName TEXT,
                    IsPlaceholder INTEGER NOT NULL,
                    CreatedAtUtcTicks INTEGER NOT NULL,
                    UpdatedAtUtcTicks INTEGER NOT NULL,
                    FOREIGN KEY (PlayerArchiveId)
                        REFERENCES PlayerArchives(Id)
                        ON DELETE CASCADE,
                    FOREIGN KEY (GameRoleIdentityId)
                        REFERENCES GameRoleIdentities(Id)
                );
                """);

            connection.Execute(
                """
                CREATE TABLE GachaRecords
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameAccountId TEXT NOT NULL,
                    ExternalRecordId TEXT NOT NULL,
                    ItemName TEXT,
                    ItemId TEXT,
                    ItemType TEXT,
                    GachaType TEXT,
                    UigfGachaType TEXT,
                    RankType INTEGER,
                    Count INTEGER NOT NULL,
                    TimeUtcTicks INTEGER NOT NULL,
                    TimeOffsetMinutes INTEGER NOT NULL,
                    FOREIGN KEY (GameAccountId)
                        REFERENCES GameAccounts(Id)
                        ON DELETE CASCADE
                );
                """);

            connection.Execute(
                """
                CREATE INDEX IX_GameAccounts_PlayerArchiveId
                ON GameAccounts(PlayerArchiveId);
                """);

            CreateGameAccountIdentityIndexes(connection);

            CreateGachaRecordIndexes(connection);

            connection.Execute(
                $"PRAGMA application_id = {CurrentApplicationId};");
            connection.Execute(
                $"PRAGMA user_version = {CurrentSchemaVersion};");
        });
    }

    private static void CreateGachaRecordIndexes(SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE UNIQUE INDEX UX_GachaRecords_Account_ExternalId
            ON GachaRecords(GameAccountId, ExternalRecordId);
            """);

        connection.Execute(
            """
            CREATE INDEX IX_GachaRecords_TimeUtcTicks
            ON GachaRecords(TimeUtcTicks);
            """);

        connection.Execute(
            """
            CREATE INDEX IX_GachaRecords_GameAccountId
            ON GachaRecords(GameAccountId);
            """);
    }

    private static void CreateGameRoleIdentityTable(
        SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE GameRoleIdentities
            (
                Id TEXT PRIMARY KEY NOT NULL,
                GameBiz TEXT NOT NULL,
                Server TEXT NOT NULL,
                Uid TEXT NOT NULL
            );
            """);

        connection.Execute(
            """
            CREATE UNIQUE INDEX UX_GameRoleIdentities_NaturalIdentity
            ON GameRoleIdentities(GameBiz, Server, Uid);
            """);
    }

    private static void CreateGameAccountIdentityIndexes(
        SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE INDEX IX_GameAccounts_GameRoleIdentityId
            ON GameAccounts(GameRoleIdentityId);
            """);

        connection.Execute(
            """
            CREATE UNIQUE INDEX UX_GameAccounts_Archive_RoleIdentity
            ON GameAccounts(PlayerArchiveId, GameRoleIdentityId)
            WHERE GameRoleIdentityId IS NOT NULL;
            """);

        connection.Execute(
            """
            CREATE UNIQUE INDEX UX_GameAccounts_Archive_UnresolvedIdentity
            ON GameAccounts(PlayerArchiveId, Uid, ServerRegion)
            WHERE GameRoleIdentityId IS NULL;
            """);
    }

    private sealed class LegacyGameAccountIdentityRow
    {
        public string Id { get; set; } = string.Empty;

        public string Uid { get; set; } = string.Empty;

        public int ServerRegion { get; set; }

        public bool IsPlaceholder { get; set; }
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        initializeGate.Dispose();
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabase : IAsyncDisposable
{
    private const int CurrentSchemaVersion = 2;
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
                schemaVersion == LegacyGachaSchemaVersion)
            {
                await MigrateVersionOneToVersionTwoAsync(cancellationToken);
            }
            else if (schemaVersion != CurrentSchemaVersion ||
                     applicationId != CurrentApplicationId)
            {
                await RecreateCurrentSchemaAsync(cancellationToken);
            }

            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    private async Task MigrateVersionOneToVersionTwoAsync(
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
            connection.Execute(
                $"PRAGMA user_version = {CurrentSchemaVersion};");
        });
    }

    private async Task RecreateCurrentSchemaAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Pre-release schemas are intentionally unsupported. Drop children
            // before parents so the operation also works with foreign keys on.
            connection.Execute("DROP TABLE IF EXISTS GachaRecords;");
            connection.Execute(
                $"DROP TABLE IF EXISTS {LegacyGachaRecordsTableName};");
            connection.Execute("DROP TABLE IF EXISTS GameAccounts;");
            connection.Execute("DROP TABLE IF EXISTS PlayerArchives;");

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

            connection.Execute(
                """
                CREATE TABLE GameAccounts
                (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PlayerArchiveId TEXT NOT NULL,
                    Uid TEXT NOT NULL,
                    ServerRegion INTEGER NOT NULL,
                    DisplayName TEXT,
                    IsPlaceholder INTEGER NOT NULL,
                    CreatedAtUtcTicks INTEGER NOT NULL,
                    UpdatedAtUtcTicks INTEGER NOT NULL,
                    FOREIGN KEY (PlayerArchiveId)
                        REFERENCES PlayerArchives(Id)
                        ON DELETE CASCADE
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

            connection.Execute(
                """
                CREATE UNIQUE INDEX UX_GameAccounts_PlayerArchiveId_Uid
                ON GameAccounts(PlayerArchiveId, Uid);
                """);

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

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        initializeGate.Dispose();
    }
}

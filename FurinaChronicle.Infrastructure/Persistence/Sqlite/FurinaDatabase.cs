// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabase : IAsyncDisposable
{
    private const int CurrentSchemaVersion = 5;
    private const int ProvenanceSchemaVersion = 4;
    private const int SharedIdentitySchemaVersion = 3;
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
                await ValidateCurrentSchemaAsync(cancellationToken);
                initialized = true;
                return;
            }

            if (applicationId == CurrentApplicationId &&
                schemaVersion == LegacyGachaSchemaVersion)
            {
                await MigrateVersionOneToCurrentAsync(cancellationToken);
            }
            else if (applicationId == CurrentApplicationId &&
                     schemaVersion == PreSharedIdentitySchemaVersion)
            {
                await MigrateVersionTwoToCurrentAsync(cancellationToken);
            }
            else if (applicationId == CurrentApplicationId &&
                     schemaVersion == SharedIdentitySchemaVersion)
            {
                await MigrateVersionThreeToCurrentAsync(cancellationToken);
            }
            else if (applicationId == CurrentApplicationId &&
                     schemaVersion == ProvenanceSchemaVersion)
            {
                await MigrateVersionFourToCurrentAsync(cancellationToken);
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

            await ValidateCurrentSchemaAsync(cancellationToken);
            initialized = true;
        }
        finally
        {
            initializeGate.Release();
        }
    }

    private async Task MigrateVersionOneToCurrentAsync(
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
            MigrateVersionThreeToVersionFour(connection);
            MigrateVersionFourToVersionFive(connection);
            SetCurrentSchemaVersion(connection);
        });
    }

    private async Task MigrateVersionTwoToCurrentAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            MigrateVersionTwoToVersionThree(connection, cancellationToken);
            MigrateVersionThreeToVersionFour(connection);
            MigrateVersionFourToVersionFive(connection);
            SetCurrentSchemaVersion(connection);
        });
    }

    private async Task MigrateVersionThreeToCurrentAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            MigrateVersionThreeToVersionFour(connection);
            MigrateVersionFourToVersionFive(connection);
            SetCurrentSchemaVersion(connection);
        });
    }

    private async Task MigrateVersionFourToCurrentAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            MigrateVersionFourToVersionFive(connection);
            SetCurrentSchemaVersion(connection);
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
    }

    private static void MigrateVersionThreeToVersionFour(
        SQLiteConnection connection)
    {
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN Origin INTEGER NOT NULL DEFAULT 0;");
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN FetchedAtUtcTicks INTEGER;");
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN FetchedAtOffsetMinutes INTEGER;");
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN ImportedAtUtcTicks INTEGER;");
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN ImportedAtOffsetMinutes INTEGER;");
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN AcquisitionBatchId TEXT;");
    }

    private static void MigrateVersionFourToVersionFive(
        SQLiteConnection connection)
    {
        connection.Execute(
            "ALTER TABLE GachaRecords ADD COLUMN Version INTEGER NOT NULL DEFAULT 1;");
        CreateHistoryTables(connection);
    }

    private static void SetCurrentSchemaVersion(SQLiteConnection connection)
    {
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

    private async Task ValidateCurrentSchemaAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string quickCheck = await Connection.ExecuteScalarAsync<string>(
            "PRAGMA quick_check;");
        if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"SQLite integrity check failed: {quickCheck}. The database was not modified.");
        }

        await ValidateTableColumnsAsync(
            "PlayerArchives",
            ["Id", "Name", "CreatedAtUtcTicks", "UpdatedAtUtcTicks"],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "GameRoleIdentities",
            ["Id", "GameBiz", "Server", "Uid"],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "GameAccounts",
            [
                "Id",
                "PlayerArchiveId",
                "GameRoleIdentityId",
                "Uid",
                "ServerRegion",
                "DisplayName",
                "IsPlaceholder",
                "CreatedAtUtcTicks",
                "UpdatedAtUtcTicks",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "GachaRecords",
            [
                "Id",
                "GameAccountId",
                "ExternalRecordId",
                "ItemName",
                "ItemId",
                "ItemType",
                "GachaType",
                "UigfGachaType",
                "RankType",
                "Count",
                "TimeUtcTicks",
                "TimeOffsetMinutes",
                "Origin",
                "FetchedAtUtcTicks",
                "FetchedAtOffsetMinutes",
                "ImportedAtUtcTicks",
                "ImportedAtOffsetMinutes",
                "AcquisitionBatchId",
                "Version",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "DataChangeSets",
            [
                "ChangeSetId",
                "OperationId",
                "OperationKind",
                "StartedAtUtcTicks",
                "StartedAtOffsetMinutes",
                "CommittedAtUtcTicks",
                "CommittedAtOffsetMinutes",
                "Origin",
                "Summary",
                "AffectedRecordCount",
                "UndoOfChangeSetId",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "DataChangeSetArchives",
            ["ChangeSetId", "ArchiveId"],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "EntityChanges",
            [
                "Id",
                "ChangeSetId",
                "EntityKind",
                "EntityReference",
                "ChangeKind",
                "BeforeVersion",
                "AfterVersion",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "GachaRevisions",
            [
                "RevisionId",
                "ChangeSetId",
                "GameAccountId",
                "ExternalRecordId",
                "ChangeKind",
                "BeforeVersion",
                "AfterVersion",
                "SnapshotFormatVersion",
                "BeforeSnapshotJson",
                "AfterSnapshotJson",
                "CreatedAtUtcTicks",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "OperationHistory",
            [
                "ChangeSetId",
                "IsUndoEligible",
                "IneligibilityReason",
                "UndoneByChangeSetId",
                "MaterialBytes",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "UndoMaterials",
            [
                "Id",
                "ChangeSetId",
                "EntityKind",
                "EntityReference",
                "ChangeKind",
                "ExpectedAfterVersion",
                "SnapshotFormatVersion",
                "BeforeSnapshotJson",
                "AfterSnapshotJson",
                "MaterialBytes",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "ArchiveUndoSettings",
            ["ArchiveId", "UndoLimit"],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "OperationCommitResults",
            [
                "OperationId",
                "ChangeSetId",
                "Status",
                "AffectedRecordCount",
                "CommittedAtUtcTicks",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "LocalOnlyTombstones",
            [
                "TombstoneKey",
                "TombstoneVersion",
                "ArchiveId",
                "IsActive",
                "DeletedByChangeSetId",
                "DeletedAtUtcTicks",
                "ReintroducedByChangeSetId",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "RoleIdentityAliases",
            [
                "SourceIdentityId",
                "TargetIdentityId",
                "CreatedByChangeSetId",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "PortableImportReceipts",
            [
                "ReceiptId",
                "ChangeSetId",
                "PackageFingerprint",
                "ReceivedAtUtcTicks",
                "ReceivedAtOffsetMinutes",
                "ReceiptBatchId",
                "SourceArchiveId",
                "TargetArchiveId",
                "AccountCount",
                "RecordCount",
            ],
            cancellationToken);
        await ValidateTableColumnsAsync(
            "PortableImportReceiptAccounts",
            [
                "ReceiptId",
                "SourceAccountId",
                "TargetAccountId",
                "SourceRoleIdentityId",
                "TargetRoleIdentityId",
            ],
            cancellationToken);

        List<ForeignKeyViolationRow> violations =
            await Connection.QueryAsync<ForeignKeyViolationRow>(
                "PRAGMA foreign_key_check;");
        if (violations.Count > 0)
        {
            throw new InvalidDataException(
                "SQLite foreign-key validation failed. The database was not modified.");
        }
    }

    private async Task ValidateTableColumnsAsync(
        string tableName,
        IReadOnlyCollection<string> requiredColumns,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        int tableCount = await Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = ?;",
            tableName);
        if (tableCount != 1)
        {
            throw new InvalidDataException(
                $"Required database table {tableName} is missing. The database was not modified.");
        }

        List<SchemaColumnRow> columns =
            await Connection.QueryAsync<SchemaColumnRow>(
                $"PRAGMA table_info({tableName});");
        HashSet<string> columnNames = columns
            .Select(column => column.Name)
            .ToHashSet(StringComparer.Ordinal);
        string[] missing = requiredColumns
            .Where(column => !columnNames.Contains(column))
            .ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidDataException(
                $"Database table {tableName} is missing required columns: " +
                $"{string.Join(", ", missing)}. The database was not modified.");
        }

        cancellationToken.ThrowIfCancellationRequested();
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
                    Origin INTEGER NOT NULL,
                    FetchedAtUtcTicks INTEGER,
                    FetchedAtOffsetMinutes INTEGER,
                    ImportedAtUtcTicks INTEGER,
                    ImportedAtOffsetMinutes INTEGER,
                    AcquisitionBatchId TEXT,
                    Version INTEGER NOT NULL DEFAULT 1,
                    FOREIGN KEY (GameAccountId)
                        REFERENCES GameAccounts(Id)
                        ON DELETE CASCADE
                );
                """);

            CreateHistoryTables(connection);

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

    private static void CreateHistoryTables(SQLiteConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE DataChangeSets
            (
                ChangeSetId TEXT PRIMARY KEY NOT NULL,
                OperationId TEXT NOT NULL UNIQUE,
                OperationKind INTEGER NOT NULL,
                StartedAtUtcTicks INTEGER NOT NULL,
                StartedAtOffsetMinutes INTEGER NOT NULL,
                CommittedAtUtcTicks INTEGER NOT NULL,
                CommittedAtOffsetMinutes INTEGER NOT NULL,
                Origin INTEGER NOT NULL,
                Summary TEXT NOT NULL,
                AffectedRecordCount INTEGER NOT NULL,
                UndoOfChangeSetId TEXT,
                FOREIGN KEY (UndoOfChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE DataChangeSetArchives
            (
                ChangeSetId TEXT NOT NULL,
                ArchiveId TEXT NOT NULL,
                PRIMARY KEY (ChangeSetId, ArchiveId),
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
                    ON DELETE CASCADE
            );
            """);

        connection.Execute(
            """
            CREATE TABLE EntityChanges
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChangeSetId TEXT NOT NULL,
                EntityKind TEXT NOT NULL,
                EntityReference TEXT NOT NULL,
                ChangeKind INTEGER NOT NULL,
                BeforeVersion INTEGER,
                AfterVersion INTEGER,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
                    ON DELETE CASCADE
            );
            """);

        connection.Execute(
            """
            CREATE TABLE GachaRevisions
            (
                RevisionId TEXT PRIMARY KEY NOT NULL,
                ChangeSetId TEXT NOT NULL,
                GameAccountId TEXT NOT NULL,
                ExternalRecordId TEXT NOT NULL,
                ChangeKind INTEGER NOT NULL,
                BeforeVersion INTEGER,
                AfterVersion INTEGER,
                SnapshotFormatVersion INTEGER NOT NULL,
                BeforeSnapshotJson TEXT,
                AfterSnapshotJson TEXT,
                CreatedAtUtcTicks INTEGER NOT NULL,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
                    ON DELETE CASCADE
            );
            """);

        connection.Execute(
            """
            CREATE TABLE OperationHistory
            (
                ChangeSetId TEXT PRIMARY KEY NOT NULL,
                IsUndoEligible INTEGER NOT NULL,
                IneligibilityReason INTEGER NOT NULL,
                UndoneByChangeSetId TEXT,
                MaterialBytes INTEGER NOT NULL,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
                    ON DELETE CASCADE,
                FOREIGN KEY (UndoneByChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE UndoMaterials
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChangeSetId TEXT NOT NULL,
                EntityKind TEXT NOT NULL,
                EntityReference TEXT NOT NULL,
                ChangeKind INTEGER NOT NULL,
                ExpectedAfterVersion INTEGER,
                SnapshotFormatVersion INTEGER NOT NULL,
                BeforeSnapshotJson TEXT,
                AfterSnapshotJson TEXT,
                MaterialBytes INTEGER NOT NULL,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
                    ON DELETE CASCADE,
                UNIQUE (ChangeSetId, EntityKind, EntityReference)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE ArchiveUndoSettings
            (
                ArchiveId TEXT PRIMARY KEY NOT NULL,
                UndoLimit INTEGER NOT NULL DEFAULT 3,
                FOREIGN KEY (ArchiveId)
                    REFERENCES PlayerArchives(Id)
                    ON DELETE CASCADE
            );
            """);

        connection.Execute(
            """
            CREATE TABLE OperationCommitResults
            (
                OperationId TEXT PRIMARY KEY NOT NULL,
                ChangeSetId TEXT UNIQUE,
                Status INTEGER NOT NULL,
                AffectedRecordCount INTEGER NOT NULL,
                CommittedAtUtcTicks INTEGER NOT NULL,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE LocalOnlyTombstones
            (
                TombstoneKey TEXT PRIMARY KEY NOT NULL,
                TombstoneVersion INTEGER NOT NULL,
                ArchiveId TEXT NOT NULL,
                IsActive INTEGER NOT NULL,
                DeletedByChangeSetId TEXT NOT NULL,
                DeletedAtUtcTicks INTEGER NOT NULL,
                ReintroducedByChangeSetId TEXT,
                FOREIGN KEY (DeletedByChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId),
                FOREIGN KEY (ReintroducedByChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE RoleIdentityAliases
            (
                SourceIdentityId TEXT PRIMARY KEY NOT NULL,
                TargetIdentityId TEXT NOT NULL,
                CreatedByChangeSetId TEXT NOT NULL,
                FOREIGN KEY (TargetIdentityId)
                    REFERENCES GameRoleIdentities(Id),
                FOREIGN KEY (CreatedByChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE PortableImportReceipts
            (
                ReceiptId TEXT PRIMARY KEY NOT NULL,
                ChangeSetId TEXT NOT NULL UNIQUE,
                PackageFingerprint TEXT NOT NULL,
                ReceivedAtUtcTicks INTEGER NOT NULL,
                ReceivedAtOffsetMinutes INTEGER NOT NULL,
                ReceiptBatchId TEXT NOT NULL,
                SourceArchiveId TEXT NOT NULL,
                TargetArchiveId TEXT NOT NULL,
                AccountCount INTEGER NOT NULL,
                RecordCount INTEGER NOT NULL,
                FOREIGN KEY (ChangeSetId)
                    REFERENCES DataChangeSets(ChangeSetId)
            );
            """);

        connection.Execute(
            """
            CREATE TABLE PortableImportReceiptAccounts
            (
                ReceiptId TEXT NOT NULL,
                SourceAccountId TEXT NOT NULL,
                TargetAccountId TEXT NOT NULL,
                SourceRoleIdentityId TEXT NOT NULL,
                TargetRoleIdentityId TEXT NOT NULL,
                PRIMARY KEY (ReceiptId, SourceAccountId),
                FOREIGN KEY (ReceiptId)
                    REFERENCES PortableImportReceipts(ReceiptId)
                    ON DELETE CASCADE
            );
            """);

        connection.Execute(
            """
            CREATE INDEX IX_DataChangeSetArchives_Archive_ChangeSet
            ON DataChangeSetArchives(ArchiveId, ChangeSetId);
            """);
        connection.Execute(
            """
            CREATE INDEX IX_EntityChanges_ChangeSet
            ON EntityChanges(ChangeSetId);
            """);
        connection.Execute(
            """
            CREATE INDEX IX_GachaRevisions_Fact
            ON GachaRevisions(GameAccountId, ExternalRecordId, CreatedAtUtcTicks);
            """);
        connection.Execute(
            """
            CREATE INDEX IX_OperationHistory_UndoEligible
            ON OperationHistory(IsUndoEligible, ChangeSetId);
            """);
        connection.Execute(
            """
            CREATE INDEX IX_LocalOnlyTombstones_Archive_Active
            ON LocalOnlyTombstones(ArchiveId, IsActive);
            """);
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

    private sealed class SchemaColumnRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyViolationRow
    {
        [Column("table")]
        public string Table { get; set; } = string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        initializeGate.Dispose();
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabaseMigrationTests
{
    private const int CurrentApplicationId = 0x46554348;
    private const int CurrentSchemaVersion = 3;

    [Fact]
    public async Task InitializeAsync_NewDatabase_CreatesCurrentVersionThreeSchema()
    {
        await using var fixture = MigrationFixture.Create();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        AssertCurrentSchema(connection);

        string[] gachaColumns = connection
            .Query<NameRow>("PRAGMA table_info(GachaRecords);")
            .Select(row => row.Name)
            .ToArray();
        Assert.Contains("ItemId", gachaColumns);
        Assert.Contains("ItemType", gachaColumns);
        Assert.Contains("GachaType", gachaColumns);
        Assert.Contains("UigfGachaType", gachaColumns);
        Assert.Contains("Count", gachaColumns);

        string[] indexes = connection.Query<NameRow>(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'index' AND name NOT LIKE 'sqlite_%';
                """)
            .Select(row => row.Name)
            .ToArray();
        Assert.Contains("IX_GameAccounts_PlayerArchiveId", indexes);
        Assert.Contains("IX_GameAccounts_GameRoleIdentityId", indexes);
        Assert.Contains("UX_GameAccounts_Archive_RoleIdentity", indexes);
        Assert.Contains("UX_GameAccounts_Archive_UnresolvedIdentity", indexes);
        Assert.Contains("UX_GameRoleIdentities_NaturalIdentity", indexes);
        Assert.DoesNotContain("UX_GameAccounts_PlayerArchiveId_Uid", indexes);
        Assert.Contains("UX_GachaRecords_Account_ExternalId", indexes);
        Assert.Contains("IX_GachaRecords_TimeUtcTicks", indexes);
        Assert.Contains("IX_GachaRecords_GameAccountId", indexes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task InitializeAsync_PreReleaseDatabase_RejectsAndPreservesOldData(
        int oldVersion)
    {
        await using var fixture = MigrationFixture.Create();
        fixture.CreatePreReleaseDatabase(oldVersion);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            FurinaDatabaseCompatibilityException exception =
                await Assert.ThrowsAsync<FurinaDatabaseCompatibilityException>(
                    () => database.InitializeAsync());

            Assert.Equal(0, exception.ActualApplicationId);
            Assert.Equal(oldVersion, exception.ActualSchemaVersion);
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(
            oldVersion,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>("PRAGMA application_id;"));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM PlayerArchives;"));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameAccounts;"));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GachaRecords;"));
        Assert.Equal(
            "old-gacha",
            connection.ExecuteScalar<string>(
                "SELECT ExternalRecordId FROM GachaRecords LIMIT 1;"));
    }

    [Fact]
    public async Task InitializeAsync_FutureSchemaVersion_RejectsAndPreservesOldData()
    {
        await using var fixture = MigrationFixture.Create();
        fixture.CreatePreReleaseDatabase(
            schemaVersion: CurrentSchemaVersion + 1,
            applicationId: CurrentApplicationId);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            FurinaDatabaseCompatibilityException exception =
                await Assert.ThrowsAsync<FurinaDatabaseCompatibilityException>(
                    () => database.InitializeAsync());

            Assert.Equal(CurrentApplicationId, exception.ActualApplicationId);
            Assert.Equal(
                CurrentSchemaVersion + 1,
                exception.ActualSchemaVersion);
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(
            CurrentApplicationId,
            connection.ExecuteScalar<int>("PRAGMA application_id;"));
        Assert.Equal(
            CurrentSchemaVersion + 1,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            "old-gacha",
            connection.ExecuteScalar<string>(
                "SELECT ExternalRecordId FROM GachaRecords LIMIT 1;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionOne_MigratesAndPreservesData()
    {
        await using var fixture = MigrationFixture.Create();
        Guid archiveId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        fixture.CreateVersionOneDatabase(archiveId, accountId);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection reopened = fixture.OpenRawConnection();
        AssertCurrentSchema(reopened);
        Assert.Equal(
            1,
            reopened.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM PlayerArchives;"));
        Assert.Equal(
            1,
            reopened.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameAccounts;"));
        Assert.Equal(
            1,
            reopened.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GachaRecords;"));
        Assert.Equal(
            "gacha-1",
            reopened.ExecuteScalar<string>(
                "SELECT ExternalRecordId FROM GachaRecords LIMIT 1;"));
        Assert.Equal(
            0,
            reopened.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'WishRecords';
                """));
        Assert.Equal(
            1,
            reopened.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameRoleIdentities;"));
        Assert.NotNull(
            reopened.ExecuteScalar<string?>(
                "SELECT GameRoleIdentityId FROM GameAccounts LIMIT 1;"));
    }

    [Fact]
    public async Task InitializeAsync_VersionTwo_MigratesSharedAndUnresolvedIdentities()
    {
        await using var fixture = MigrationFixture.Create();
        Guid firstArchiveId = Guid.NewGuid();
        Guid secondArchiveId = Guid.NewGuid();
        Guid firstResolvedAccountId = Guid.NewGuid();
        Guid secondResolvedAccountId = Guid.NewGuid();
        Guid unresolvedAccountId = Guid.NewGuid();
        fixture.CreateVersionTwoDatabase(
            firstArchiveId,
            secondArchiveId,
            firstResolvedAccountId,
            secondResolvedAccountId,
            unresolvedAccountId);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        AssertCurrentSchema(connection);

        string expectedIdentityId = GenshinGameRoleIdentity
            .CreateIdentity("800000001", GameServerRegion.Asia)
            .Id
            .ToString();
        string[] resolvedIdentityIds = connection.Query<IdentityReferenceRow>(
                """
                SELECT GameRoleIdentityId
                FROM GameAccounts
                WHERE Id IN (?, ?)
                ORDER BY Id;
                """,
                firstResolvedAccountId.ToString("D"),
                secondResolvedAccountId.ToString("D"))
            .Select(row => row.GameRoleIdentityId!)
            .ToArray();

        Assert.Equal(2, resolvedIdentityIds.Length);
        Assert.All(
            resolvedIdentityIds,
            identityId => Assert.Equal(expectedIdentityId, identityId));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameRoleIdentities;"));
        Assert.Null(
            connection.ExecuteScalar<string?>(
                """
                SELECT GameRoleIdentityId
                FROM GameAccounts
                WHERE Id = ?;
                """,
                unresolvedAccountId.ToString("D")));
        Assert.Equal(
            3,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GachaRecords;"));
    }

    [Fact]
    public async Task InitializeAsync_InvalidVersionOneSchema_FailsWithoutChangingDatabase()
    {
        await using var fixture = MigrationFixture.Create();
        fixture.CreatePreReleaseDatabase(
            schemaVersion: 1,
            applicationId: CurrentApplicationId);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await Assert.ThrowsAsync<SQLiteException>(
                () => database.InitializeAsync());
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(
            CurrentApplicationId,
            connection.ExecuteScalar<int>("PRAGMA application_id;"));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            "old-gacha",
            connection.ExecuteScalar<string>(
                "SELECT ExternalRecordId FROM GachaRecords LIMIT 1;"));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'WishRecords';
                """));
    }

    [Fact]
    public async Task InitializeAsync_VersionOneIdentityMigrationFailure_RollsBackEntireChain()
    {
        await using var fixture = MigrationFixture.Create();
        Guid archiveId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();
        fixture.CreateVersionOneDatabase(archiveId, accountId);
        fixture.RemoveVersionOneIdentityColumn();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await Assert.ThrowsAsync<SQLiteException>(
                () => database.InitializeAsync());
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            1,
            connection.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'WishRecords';
                """));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'GachaRecords';
                """));
        Assert.Equal(
            "gacha-1",
            connection.ExecuteScalar<string>(
                "SELECT ExternalRecordId FROM WishRecords LIMIT 1;"));
    }

    [Fact]
    public async Task InitializeAsync_CanceledToken_DoesNotCreateSchema()
    {
        await using var fixture = MigrationFixture.Create();
        await using FurinaDatabase database = fixture.OpenDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => database.InitializeAsync(cancellation.Token));

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>("PRAGMA application_id;"));
    }

    private static void AssertCurrentSchema(SQLiteConnection connection)
    {
        Assert.Equal(
            CurrentSchemaVersion,
            connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(
            CurrentApplicationId,
            connection.ExecuteScalar<int>("PRAGMA application_id;"));

        string[] tables = connection.Query<NameRow>(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table' AND name NOT LIKE 'sqlite_%';
                """)
            .Select(row => row.Name)
            .ToArray();
        Assert.Equal(
            [
                "GachaRecords",
                "GameAccounts",
                "GameRoleIdentities",
                "PlayerArchives",
            ],
            tables.Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(
            connection.Query<ForeignKeyCheckRow>(
                "PRAGMA foreign_key_check;"));

        List<ForeignKeyDefinitionRow> accountForeignKeys = connection
            .Query<ForeignKeyDefinitionRow>(
                "PRAGMA foreign_key_list(GameAccounts);");
        ForeignKeyDefinitionRow archiveForeignKey = Assert.Single(
            accountForeignKeys,
            foreignKey => foreignKey.Table == "PlayerArchives");
        Assert.Equal("CASCADE", archiveForeignKey.OnDelete);
        ForeignKeyDefinitionRow identityForeignKey = Assert.Single(
            accountForeignKeys,
            foreignKey => foreignKey.Table == "GameRoleIdentities");
        Assert.Equal("NO ACTION", identityForeignKey.OnDelete);

        ForeignKeyDefinitionRow gachaForeignKey = Assert.Single(
            connection.Query<ForeignKeyDefinitionRow>(
                "PRAGMA foreign_key_list(GachaRecords);"));
        Assert.Equal("GameAccounts", gachaForeignKey.Table);
        Assert.Equal("CASCADE", gachaForeignKey.OnDelete);
    }

    private sealed class NameRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyCheckRow
    {
        [Column("table")]
        public string Table { get; set; } = string.Empty;
    }

    private sealed class IdentityReferenceRow
    {
        public string? GameRoleIdentityId { get; set; }
    }

    private sealed class ForeignKeyDefinitionRow
    {
        [Column("table")]
        public string Table { get; set; } = string.Empty;

        [Column("on_delete")]
        public string OnDelete { get; set; } = string.Empty;
    }

    private sealed class MigrationFixture : IAsyncDisposable
    {
        private readonly string directory;

        private MigrationFixture(string directory)
        {
            this.directory = directory;
            DatabasePath = Path.Combine(directory, "migration.db3");
        }

        public string DatabasePath { get; }

        public static MigrationFixture Create()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleMigrationTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new MigrationFixture(directory);
        }

        public FurinaDatabase OpenDatabase() =>
            new(new SqliteDatabaseOptions(DatabasePath));

        public SQLiteConnection OpenRawConnection() =>
            new(
                DatabasePath,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create |
                SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);

        public void CreatePreReleaseDatabase(
            int schemaVersion,
            int applicationId = 0)
        {
            using SQLiteConnection connection = OpenRawConnection();
            connection.Execute(
                """
                CREATE TABLE PlayerArchives
                (
                    Id TEXT PRIMARY KEY NOT NULL,
                    Name TEXT
                );
                """);
            connection.Execute(
                """
                CREATE TABLE GameAccounts
                (
                    Id TEXT PRIMARY KEY NOT NULL,
                    PlayerArchiveId TEXT,
                    Uid TEXT
                );
                """);
            connection.Execute(
                """
                CREATE TABLE GachaRecords
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameAccountId TEXT,
                    ExternalRecordId TEXT
                );
                """);

            connection.Execute(
                "INSERT INTO PlayerArchives (Id, Name) VALUES ('old-archive', 'Old');");
            connection.Execute(
                """
                INSERT INTO GameAccounts (Id, PlayerArchiveId, Uid)
                VALUES ('old-account', 'old-archive', '800000001');
                """);
            connection.Execute(
                """
                INSERT INTO GachaRecords (GameAccountId, ExternalRecordId)
                VALUES ('old-account', 'old-gacha');
                """);
            connection.Execute($"PRAGMA application_id = {applicationId};");
            connection.Execute($"PRAGMA user_version = {schemaVersion};");
        }

        public void CreateVersionOneDatabase(Guid archiveId, Guid accountId)
        {
            using SQLiteConnection connection = OpenRawConnection();
            DateTimeOffset now = DateTimeOffset.UtcNow;

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
                CREATE TABLE WishRecords
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
            connection.Execute(
                """
                CREATE UNIQUE INDEX UX_WishRecords_Account_ExternalId
                ON WishRecords(GameAccountId, ExternalRecordId);
                """);
            connection.Execute(
                """
                CREATE INDEX IX_WishRecords_TimeUtcTicks
                ON WishRecords(TimeUtcTicks);
                """);
            connection.Execute(
                """
                CREATE INDEX IX_WishRecords_GameAccountId
                ON WishRecords(GameAccountId);
                """);

            connection.Execute(
                """
                INSERT INTO PlayerArchives
                    (Id, Name, CreatedAtUtcTicks, UpdatedAtUtcTicks)
                VALUES (?, ?, ?, ?);
                """,
                archiveId.ToString("D"),
                "Preserved archive",
                now.UtcDateTime.Ticks,
                now.UtcDateTime.Ticks);
            connection.Execute(
                """
                INSERT INTO GameAccounts
                    (Id, PlayerArchiveId, Uid, ServerRegion, DisplayName,
                     IsPlaceholder, CreatedAtUtcTicks, UpdatedAtUtcTicks)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                """,
                accountId.ToString("D"),
                archiveId.ToString("D"),
                "800000001",
                5,
                null,
                false,
                now.UtcDateTime.Ticks,
                now.UtcDateTime.Ticks);
            connection.Execute(
                """
                INSERT INTO WishRecords
                    (GameAccountId, ExternalRecordId, ItemName, ItemId,
                     ItemType, GachaType, UigfGachaType, RankType, Count,
                     TimeUtcTicks, TimeOffsetMinutes)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                accountId.ToString("D"),
                "gacha-1",
                "Furina",
                "10000089",
                "Character",
                "301",
                "301",
                5,
                1,
                now.UtcDateTime.Ticks,
                480);
            connection.Execute($"PRAGMA application_id = {CurrentApplicationId};");
            connection.Execute("PRAGMA user_version = 1;");
        }

        public void CreateVersionTwoDatabase(
            Guid firstArchiveId,
            Guid secondArchiveId,
            Guid firstResolvedAccountId,
            Guid secondResolvedAccountId,
            Guid unresolvedAccountId)
        {
            using SQLiteConnection connection = OpenRawConnection();
            DateTimeOffset now = DateTimeOffset.UtcNow;

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
                "CREATE INDEX IX_GameAccounts_PlayerArchiveId ON GameAccounts(PlayerArchiveId);");
            connection.Execute(
                "CREATE UNIQUE INDEX UX_GameAccounts_PlayerArchiveId_Uid ON GameAccounts(PlayerArchiveId, Uid);");
            connection.Execute(
                "CREATE UNIQUE INDEX UX_GachaRecords_Account_ExternalId ON GachaRecords(GameAccountId, ExternalRecordId);");
            connection.Execute(
                "CREATE INDEX IX_GachaRecords_TimeUtcTicks ON GachaRecords(TimeUtcTicks);");
            connection.Execute(
                "CREATE INDEX IX_GachaRecords_GameAccountId ON GachaRecords(GameAccountId);");

            InsertArchive(connection, firstArchiveId, "First", now);
            InsertArchive(connection, secondArchiveId, "Second", now);
            InsertAccount(
                connection,
                firstResolvedAccountId,
                firstArchiveId,
                "800000001",
                GameServerRegion.Asia,
                isPlaceholder: false,
                now);
            InsertAccount(
                connection,
                secondResolvedAccountId,
                secondArchiveId,
                "800000001",
                GameServerRegion.Asia,
                isPlaceholder: false,
                now);
            InsertAccount(
                connection,
                unresolvedAccountId,
                firstArchiveId,
                "legacy-id",
                GameServerRegion.Unknown,
                isPlaceholder: true,
                now);

            InsertGacha(connection, firstResolvedAccountId, "resolved-1", now);
            InsertGacha(connection, secondResolvedAccountId, "resolved-2", now);
            InsertGacha(connection, unresolvedAccountId, "unresolved-1", now);

            connection.Execute(
                $"PRAGMA application_id = {CurrentApplicationId};");
            connection.Execute("PRAGMA user_version = 2;");
        }

        public void RemoveVersionOneIdentityColumn()
        {
            using SQLiteConnection connection = OpenRawConnection();
            connection.Execute(
                "ALTER TABLE GameAccounts DROP COLUMN IsPlaceholder;");
        }

        private static void InsertArchive(
            SQLiteConnection connection,
            Guid archiveId,
            string name,
            DateTimeOffset now)
        {
            connection.Execute(
                """
                INSERT INTO PlayerArchives
                    (Id, Name, CreatedAtUtcTicks, UpdatedAtUtcTicks)
                VALUES (?, ?, ?, ?);
                """,
                archiveId.ToString("D"),
                name,
                now.UtcDateTime.Ticks,
                now.UtcDateTime.Ticks);
        }

        private static void InsertAccount(
            SQLiteConnection connection,
            Guid accountId,
            Guid archiveId,
            string uid,
            GameServerRegion region,
            bool isPlaceholder,
            DateTimeOffset now)
        {
            connection.Execute(
                """
                INSERT INTO GameAccounts
                    (Id, PlayerArchiveId, Uid, ServerRegion, DisplayName,
                     IsPlaceholder, CreatedAtUtcTicks, UpdatedAtUtcTicks)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                """,
                accountId.ToString("D"),
                archiveId.ToString("D"),
                uid,
                (int)region,
                null,
                isPlaceholder,
                now.UtcDateTime.Ticks,
                now.UtcDateTime.Ticks);
        }

        private static void InsertGacha(
            SQLiteConnection connection,
            Guid accountId,
            string externalRecordId,
            DateTimeOffset now)
        {
            connection.Execute(
                """
                INSERT INTO GachaRecords
                    (GameAccountId, ExternalRecordId, Count,
                     TimeUtcTicks, TimeOffsetMinutes)
                VALUES (?, ?, ?, ?, ?);
                """,
                accountId.ToString("D"),
                externalRecordId,
                1,
                now.UtcDateTime.Ticks,
                480);
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }
    }
}

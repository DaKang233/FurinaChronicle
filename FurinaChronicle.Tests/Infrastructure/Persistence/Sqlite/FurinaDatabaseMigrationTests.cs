using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabaseMigrationTests
{
    private const int CurrentApplicationId = 0x46554348;

    [Fact]
    public async Task InitializeAsync_NewDatabase_CreatesCurrentVersionOneSchema()
    {
        await using var fixture = MigrationFixture.Create();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        AssertCurrentSchema(connection);

        string[] wishColumns = connection
            .Query<NameRow>("PRAGMA table_info(WishRecords);")
            .Select(row => row.Name)
            .ToArray();
        Assert.Contains("ItemId", wishColumns);
        Assert.Contains("ItemType", wishColumns);
        Assert.Contains("GachaType", wishColumns);
        Assert.Contains("UigfGachaType", wishColumns);
        Assert.Contains("Count", wishColumns);

        string[] indexes = connection.Query<NameRow>(
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'index' AND name NOT LIKE 'sqlite_%';
                """)
            .Select(row => row.Name)
            .ToArray();
        Assert.Contains("IX_GameAccounts_PlayerArchiveId", indexes);
        Assert.Contains("UX_GameAccounts_PlayerArchiveId_Uid", indexes);
        Assert.Contains("UX_WishRecords_Account_ExternalId", indexes);
        Assert.Contains("IX_WishRecords_TimeUtcTicks", indexes);
        Assert.Contains("IX_WishRecords_GameAccountId", indexes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    public async Task InitializeAsync_PreReleaseDatabase_DiscardsOldData(
        int oldVersion)
    {
        await using var fixture = MigrationFixture.Create();
        fixture.CreatePreReleaseDatabase(oldVersion);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        AssertCurrentSchema(connection);
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM PlayerArchives;"));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM GameAccounts;"));
        Assert.Equal(
            0,
            connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM WishRecords;"));
    }

    [Fact]
    public async Task InitializeAsync_CurrentVersionOne_ReopeningPreservesData()
    {
        await using var fixture = MigrationFixture.Create();
        Guid archiveId = Guid.NewGuid();
        Guid accountId = Guid.NewGuid();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using (SQLiteConnection connection = fixture.OpenRawConnection())
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
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
                1,
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
                "wish-1",
                "Furina",
                "10000089",
                "Character",
                "301",
                "301",
                5,
                1,
                now.UtcDateTime.Ticks,
                480);
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
                "SELECT COUNT(*) FROM WishRecords;"));
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
            1,
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
            ["GameAccounts", "PlayerArchives", "WishRecords"],
            tables.Order(StringComparer.Ordinal).ToArray());

        Assert.Empty(
            connection.Query<ForeignKeyCheckRow>(
                "PRAGMA foreign_key_check;"));

        ForeignKeyDefinitionRow accountForeignKey = Assert.Single(
            connection.Query<ForeignKeyDefinitionRow>(
                "PRAGMA foreign_key_list(GameAccounts);"));
        Assert.Equal("PlayerArchives", accountForeignKey.Table);
        Assert.Equal("CASCADE", accountForeignKey.OnDelete);

        ForeignKeyDefinitionRow wishForeignKey = Assert.Single(
            connection.Query<ForeignKeyDefinitionRow>(
                "PRAGMA foreign_key_list(WishRecords);"));
        Assert.Equal("GameAccounts", wishForeignKey.Table);
        Assert.Equal("CASCADE", wishForeignKey.OnDelete);
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

        public void CreatePreReleaseDatabase(int schemaVersion)
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
                CREATE TABLE WishRecords
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
                INSERT INTO WishRecords (GameAccountId, ExternalRecordId)
                VALUES ('old-account', 'old-wish');
                """);
            connection.Execute($"PRAGMA user_version = {schemaVersion};");
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
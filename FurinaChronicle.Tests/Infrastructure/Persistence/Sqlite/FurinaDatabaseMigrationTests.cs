using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class FurinaDatabaseMigrationTests
{
    [Fact]
    public async Task InitializeAsync_NewDatabase_CreatesVersionTwoSchema()
    {
        await using var fixture = MigrationFixture.Create();
        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await database.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(2, connection.ExecuteScalar<int>("PRAGMA user_version;"));
        string[] tables = connection.Query<NameRow>(
                "SELECT name FROM sqlite_master WHERE type = 'table';")
            .Select(row => row.Name)
            .ToArray();
        Assert.Contains("PlayerArchives", tables);
        Assert.Contains("GameAccounts", tables);
        Assert.Contains("WishRecords", tables);
        AssertForeignKeysAreValid(connection);
    }

    [Fact]
    public async Task InitializeAsync_VersionOne_PreservesWishesAndCreatesPlaceholders()
    {
        await using var fixture = MigrationFixture.Create();
        Guid firstAccountId = Guid.NewGuid();
        Guid secondAccountId = Guid.NewGuid();
        fixture.CreateVersionOneDatabase(
            [
                new LegacyWish(firstAccountId, "wish-1", 30),
                new LegacyWish(firstAccountId, "wish-2", 29),
                new LegacyWish(secondAccountId, "wish-3", 28)
            ]);

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            var archives = new SqlitePlayerArchiveRepository(database);
            var accounts = new SqliteGameAccountRepository(database);
            var wishes = new SqliteWishRecordRepository(database);

            PlayerArchive archive = Assert.Single(await archives.GetAllAsync());
            Assert.Equal("\u65e7\u6570\u636e\u8fc1\u79fb\u6863\u6848", archive.Name);

            IReadOnlyList<GameAccount> migratedAccounts =
                await accounts.GetByArchiveIdAsync(archive.Id);
            Assert.Equal(2, migratedAccounts.Count);
            Assert.All(migratedAccounts, account =>
            {
                Assert.True(account.IsPlaceholder);
                Assert.Equal(GameServerRegion.Unknown, account.ServerRegion);
                Assert.Equal(archive.Id, account.PlayerArchiveId);
            });
            Assert.Contains(migratedAccounts, account => account.Id == firstAccountId);
            Assert.Contains(migratedAccounts, account => account.Id == secondAccountId);

            Assert.Equal(2, (await wishes.GetRecentAsync(firstAccountId, 20)).Count);
            Assert.Single(await wishes.GetRecentAsync(secondAccountId, 20));
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(2, connection.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(3, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WishRecords;"));
        AssertForeignKeysAreValid(connection);
    }

    [Fact]
    public async Task InitializeAsync_VersionTwo_ReopeningDoesNotDuplicateMigratedData()
    {
        await using var fixture = MigrationFixture.Create();
        fixture.CreateVersionOneDatabase([new LegacyWish(Guid.NewGuid(), "wish-1", 30)]);

        await using (FurinaDatabase first = fixture.OpenDatabase())
        {
            await first.InitializeAsync();
        }
        await using (FurinaDatabase second = fixture.OpenDatabase())
        {
            await second.InitializeAsync();
        }

        using SQLiteConnection connection = fixture.OpenRawConnection();
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM PlayerArchives;"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM GameAccounts;"));
        Assert.Equal(1, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM WishRecords;"));
        AssertForeignKeysAreValid(connection);
    }

    [Fact]
    public async Task InitializeAsync_NewerVersion_ThrowsNotSupportedException()
    {
        await using var fixture = MigrationFixture.Create();
        using (SQLiteConnection connection = fixture.OpenRawConnection())
        {
            connection.Execute("PRAGMA user_version = 3;");
        }

        await using FurinaDatabase database = fixture.OpenDatabase();
        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => database.InitializeAsync());

        Assert.Contains("\u7248\u672c 3", exception.Message);
    }

    private static void AssertForeignKeysAreValid(SQLiteConnection connection)
    {
        Assert.Empty(connection.Query<ForeignKeyRow>("PRAGMA foreign_key_check;"));
    }

    private sealed class NameRow
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ForeignKeyRow
    {
        [Column("table")]
        public string Table { get; set; } = string.Empty;
    }

    private sealed record LegacyWish(Guid AccountId, string ExternalId, int Minute);

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

        public void CreateVersionOneDatabase(IReadOnlyCollection<LegacyWish> wishes)
        {
            using SQLiteConnection connection = OpenRawConnection();
            connection.Execute(
                """
                CREATE TABLE WishRecords
                (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    GameAccountId TEXT NOT NULL,
                    ExternalRecordId TEXT NOT NULL,
                    ItemName TEXT NOT NULL,
                    RankType INTEGER NOT NULL,
                    TimeUtcTicks INTEGER NOT NULL,
                    TimeOffsetMinutes INTEGER NOT NULL
                );
                """);
            connection.Execute(
                """
                CREATE UNIQUE INDEX UX_WishRecords_Account_ExternalId
                ON WishRecords(GameAccountId, ExternalRecordId);
                """);
            connection.Execute(
                "CREATE INDEX IX_WishRecords_TimeUtcTicks ON WishRecords(TimeUtcTicks);");

            foreach (LegacyWish wish in wishes)
            {
                DateTimeOffset time = new(
                    2026, 7, 16, 18, wish.Minute, 0, TimeSpan.FromHours(8));
                connection.Execute(
                    """
                    INSERT INTO WishRecords
                    (GameAccountId, ExternalRecordId, ItemName, RankType,
                     TimeUtcTicks, TimeOffsetMinutes)
                    VALUES (?, ?, ?, ?, ?, ?);
                    """,
                    wish.AccountId.ToString("D"),
                    wish.ExternalId,
                    "Item",
                    5,
                    time.UtcDateTime.Ticks,
                    (int)time.Offset.TotalMinutes);
            }

            connection.Execute("PRAGMA user_version = 1;");
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

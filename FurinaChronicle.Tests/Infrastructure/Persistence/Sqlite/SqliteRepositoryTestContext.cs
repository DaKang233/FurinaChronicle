using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

internal sealed class SqliteRepositoryTestContext : IAsyncDisposable
{
    private readonly string directory;

    private SqliteRepositoryTestContext(string directory, FurinaDatabase database)
    {
        this.directory = directory;
        Database = database;
        Archives = new SqlitePlayerArchiveRepository(database);
        Accounts = new SqliteGameAccountRepository(database);
        Wishes = new SqliteWishRecordRepository(database);
    }

    public FurinaDatabase Database { get; }

    public SqlitePlayerArchiveRepository Archives { get; }

    public SqliteGameAccountRepository Accounts { get; }

    public SqliteWishRecordRepository Wishes { get; }

    public static SqliteRepositoryTestContext Create()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "test.db3");
        var database = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));

        return new SqliteRepositoryTestContext(directory, database);
    }

    public static PlayerArchive CreateArchive(
        string name = "测试档案",
        Guid? id = null,
        DateTimeOffset? updatedAt = null)
    {
        DateTimeOffset createdAt = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

        return new PlayerArchive(
            id ?? Guid.NewGuid(),
            name,
            createdAt,
            updatedAt ?? createdAt);
    }

    public static GameAccount CreateAccount(
        Guid archiveId,
        string uid = "123456789",
        GameServerRegion region = GameServerRegion.Asia,
        string? displayName = "测试账号",
        Guid? id = null,
        bool isPlaceholder = false,
        DateTimeOffset? updatedAt = null)
    {
        DateTimeOffset createdAt = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);

        return new GameAccount(
            id ?? Guid.NewGuid(),
            archiveId,
            uid,
            region,
            displayName,
            isPlaceholder,
            createdAt,
            updatedAt ?? createdAt);
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync();

        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

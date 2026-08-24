using FurinaChronicle.Core.Gacha;
using SQLite;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;

public sealed class GachaMetadataDatabase : IAsyncDisposable
{
    private const int CurrentSchemaVersion = 1;

    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private bool initialized;

    internal SQLiteAsyncConnection Connection { get; }

    public GachaMetadataDatabase(GachaMetadataOptions options)
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

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await initializationGate.WaitAsync(cancellationToken);
        try
        {
            if (initialized)
            {
                return;
            }

            await Connection.SetBusyTimeoutAsync(TimeSpan.FromSeconds(5));
            await Connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
            int schemaVersion =
                await Connection.ExecuteScalarAsync<int>("PRAGMA user_version;");

            if (schemaVersion > CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Metadata database version {schemaVersion} is newer than {CurrentSchemaVersion}.");
            }

            if (schemaVersion == 0)
            {
                await CreateSchemaAsync(cancellationToken);
            }

            initialized = true;
        }
        finally
        {
            initializationGate.Release();
        }
    }

    internal async Task<GachaMetadataStateRow?> GetStateAsync(
        GachaGame game,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GachaMetadataStateRow> rows =
            await Connection.QueryAsync<GachaMetadataStateRow>(
                """
                SELECT
                    GameId,
                    LastCheckUtcTicks,
                    LastSuccessUtcTicks,
                    ContentSha256
                FROM GachaMetadataState
                WHERE GameId = ?
                LIMIT 1;
                """,
                (int)game);

        return rows.FirstOrDefault();
    }

    internal async Task<bool> HasItemsAsync(
        GachaGame game,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        int count = await Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM GachaMetadataItems WHERE GameId = ?;",
            (int)game);
        return count > 0;
    }

    internal async Task<(
        GachaMetadataItemRow? Item,
        IReadOnlyList<GachaMetadataNameRow> Names)> FindAsync(
        GachaGame game,
        string itemId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        List<GachaMetadataItemRow> itemRows =
            await Connection.QueryAsync<GachaMetadataItemRow>(
                """
                SELECT
                    GameId,
                    ItemId,
                    ItemType,
                    RankType,
                    IconUrl
                FROM GachaMetadataItems
                WHERE GameId = ? AND ItemId = ?
                LIMIT 1;
                """,
                (int)game,
                itemId);

        GachaMetadataItemRow? item = itemRows.FirstOrDefault();
        if (item is null)
        {
            return (null, []);
        }

        List<GachaMetadataNameRow> names =
            await Connection.QueryAsync<GachaMetadataNameRow>(
                """
                SELECT
                    GameId,
                    ItemId,
                    Language,
                    Name
                FROM GachaMetadataNames
                WHERE GameId = ? AND ItemId = ?;
                """,
                (int)game,
                itemId);

        return (item, names);
    }

    internal async Task ReplaceSnapshotAsync(
        GachaGame game,
        IReadOnlyCollection<GachaMetadataItemRow> items,
        IReadOnlyCollection<GachaMetadataNameRow> names,
        string contentSha256,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        long utcTicks = checkedAt.UtcDateTime.Ticks;

        await Connection.RunInTransactionAsync(connection =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            connection.Execute(
                "DELETE FROM GachaMetadataNames WHERE GameId = ?;",
                (int)game);
            connection.Execute(
                "DELETE FROM GachaMetadataItems WHERE GameId = ?;",
                (int)game);

            foreach (GachaMetadataItemRow item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Execute(
                    """
                    INSERT INTO GachaMetadataItems
                        (GameId, ItemId, ItemType, RankType, IconUrl)
                    VALUES (?, ?, ?, ?, ?);
                    """,
                    item.GameId,
                    item.ItemId,
                    item.ItemType,
                    item.RankType,
                    item.IconUrl);
            }

            foreach (GachaMetadataNameRow name in names)
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.Execute(
                    """
                    INSERT INTO GachaMetadataNames
                        (GameId, ItemId, Language, Name)
                    VALUES (?, ?, ?, ?);
                    """,
                    name.GameId,
                    name.ItemId,
                    name.Language,
                    name.Name);
            }

            connection.Execute(
                """
                INSERT OR REPLACE INTO GachaMetadataState
                    (GameId, LastCheckUtcTicks, LastSuccessUtcTicks, ContentSha256)
                VALUES (?, ?, ?, ?);
                """,
                (int)game,
                utcTicks,
                utcTicks,
                contentSha256);
        });
    }

    internal async Task MarkSuccessfulCheckAsync(
        GachaGame game,
        DateTimeOffset checkedAt,
        string contentSha256,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        long utcTicks = checkedAt.UtcDateTime.Ticks;
        await Connection.ExecuteAsync(
            """
            UPDATE GachaMetadataState
            SET
                LastCheckUtcTicks = ?,
                LastSuccessUtcTicks = ?,
                ContentSha256 = ?
            WHERE GameId = ?;
            """,
            utcTicks,
            utcTicks,
            contentSha256,
            (int)game);
    }

    internal async Task MarkCheckedAsync(
        GachaGame game,
        DateTimeOffset checkedAt,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.ExecuteAsync(
            """
            UPDATE GachaMetadataState
            SET LastCheckUtcTicks = ?
            WHERE GameId = ?;
            """,
            checkedAt.UtcDateTime.Ticks,
            (int)game);
    }

    private async Task CreateSchemaAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Connection.RunInTransactionAsync(connection =>
        {
            connection.Execute(
                """
                CREATE TABLE GachaMetadataItems
                (
                    GameId INTEGER NOT NULL,
                    ItemId TEXT NOT NULL,
                    ItemType TEXT NOT NULL,
                    RankType INTEGER,
                    IconUrl TEXT,
                    PRIMARY KEY (GameId, ItemId)
                );
                """);

            connection.Execute(
                """
                CREATE TABLE GachaMetadataNames
                (
                    GameId INTEGER NOT NULL,
                    ItemId TEXT NOT NULL,
                    Language TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    PRIMARY KEY (GameId, ItemId, Language),
                    FOREIGN KEY (GameId, ItemId)
                        REFERENCES GachaMetadataItems(GameId, ItemId)
                        ON DELETE CASCADE
                );
                """);

            connection.Execute(
                """
                CREATE INDEX IX_GachaMetadataNames_Language_Name
                ON GachaMetadataNames(Language, Name);
                """);

            connection.Execute(
                """
                CREATE TABLE GachaMetadataState
                (
                    GameId INTEGER PRIMARY KEY NOT NULL,
                    LastCheckUtcTicks INTEGER NOT NULL,
                    LastSuccessUtcTicks INTEGER NOT NULL,
                    ContentSha256 TEXT
                );
                """);

            connection.Execute($"PRAGMA user_version = {CurrentSchemaVersion};");
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync();
        initializationGate.Dispose();
    }
}

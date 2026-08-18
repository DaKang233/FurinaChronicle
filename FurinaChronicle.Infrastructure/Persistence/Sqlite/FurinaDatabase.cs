using FurinaChronicle.Core.Archives;
using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class FurinaDatabase : IAsyncDisposable
    {
        private const int CurrentSchemaVersion = 2;
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
            SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;
            Connection = new SQLiteAsyncConnection(options.DatabasePath, flags, storeDateTimeAsTicks: true);
        }

        public async Task InitializeAsync(CancellationToken cancellationToken = default)
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
                int schemaVersion = await Connection.ExecuteScalarAsync<int>("PRAGMA user_version;");
                await Connection.ExecuteAsync("PRAGMA foreign_keys = ON;");
                if (schemaVersion > CurrentSchemaVersion)
                {
                    throw new NotSupportedException($"数据库版本 {schemaVersion} 高于程序支持的版本 {CurrentSchemaVersion}。");
                }
                while (schemaVersion < CurrentSchemaVersion)
                {
                    switch (schemaVersion)
                    {
                        case 0:
                            await MigrateFrom0To1Async(cancellationToken);
                            schemaVersion = 1;
                            break;
                        case 1:
                            await MigrateFrom1To2Async(cancellationToken);
                            schemaVersion = 2;
                            break;
                        default:
                            throw new NotSupportedException($"无法从数据库版本 {schemaVersion} 进行升级。");
                    }
                }
                initialized = true;
            }
            finally
            {
                initializeGate.Release();
            }
        }

        private async Task MigrateFrom0To1Async(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Connection.RunInTransactionAsync(connection =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                connection.CreateTable<WishRecordRow>();
                connection.CreateIndex(
                    indexName: "UX_WishRecords_Account_ExternalId",
                    tableName: WishRecordRow.TableName,
                    columnNames:
                        [
                            nameof(WishRecordRow.GameAccountId),
                            nameof(WishRecordRow.ExternalRecordId)
                        ],
                    unique: true);
                connection.CreateIndex(
                    indexName: "IX_WishRecords_TimeUtcTicks",
                    tableName: WishRecordRow.TableName,
                    columnName: nameof(WishRecordRow.TimeUtcTicks));
                connection.Execute("PRAGMA user_version = 1;");
            });
        }

        private async Task MigrateFrom1To2Async(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await Connection.RunInTransactionAsync(
                connection =>
                {
                    connection.CreateTable<PlayerArchiveRow>();
                    connection.CreateTable<GameAccountRow>();

                    connection.CreateIndex(
                        "IX_GameAccounts_PlayerArchiveId",
                        GameAccountRow.TableName,
                        nameof(GameAccountRow.PlayerArchiveId));

                    connection.CreateIndex(
                        "UX_GameAccounts_Region_Uid",
                        GameAccountRow.TableName,
                        [
                            nameof(GameAccountRow.ServerRegion),
                    nameof(GameAccountRow.Uid)
                        ],
                        unique: true);

                    List<LegacyAccountIdRow> legacyAccountIds =
                        connection.Query<LegacyAccountIdRow>(
                            """
                    SELECT DISTINCT GameAccountId
                    FROM WishRecords;
                    """);

                    if (legacyAccountIds.Count > 0)
                    {
                        Guid archiveId = Guid.NewGuid();
                        DateTimeOffset now = DateTimeOffset.UtcNow;

                        connection.Insert(
                            new PlayerArchiveRow
                            {
                                Id = archiveId.ToString("D"),
                                Name = "旧数据迁移档案",
                                CreatedAtUtcTicks =
                                    now.UtcDateTime.Ticks,
                                UpdatedAtUtcTicks =
                                    now.UtcDateTime.Ticks
                            });

                        foreach (LegacyAccountIdRow oldAccount
                            in legacyAccountIds)
                        {
                            Guid accountId =
                                Guid.Parse(oldAccount.GameAccountId);

                            connection.Insert(
                                new GameAccountRow
                                {
                                    Id = accountId.ToString("D"),

                                    PlayerArchiveId =
                                        archiveId.ToString("D"),

                                    Uid =
                                        $"legacy-{accountId:N}",

                                    ServerRegion =
                                        (int)GameServerRegion.Unknown,

                                    DisplayName =
                                        "旧数据账号（待补全）",

                                    IsPlaceholder = true,

                                    CreatedAtUtcTicks =
                                        now.UtcDateTime.Ticks,

                                    UpdatedAtUtcTicks =
                                        now.UtcDateTime.Ticks
                                });
                        }
                    }

                    RebuildWishRecordsWithForeignKey(connection);

                    connection.Execute(
                        "PRAGMA user_version = 2;");
                });
        }

        private static void RebuildWishRecordsWithForeignKey(
    SQLite.SQLiteConnection connection)
        {
            connection.Execute(
                """
        ALTER TABLE WishRecords
        RENAME TO WishRecords_Old;
        """);

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

            TimeOffsetMinutes INTEGER NOT NULL,

            FOREIGN KEY (GameAccountId)
                REFERENCES GameAccounts(Id)
                ON DELETE CASCADE
        );
        """);

            connection.Execute(
                """
        INSERT INTO WishRecords
        (
            Id,
            GameAccountId,
            ExternalRecordId,
            ItemName,
            RankType,
            TimeUtcTicks,
            TimeOffsetMinutes
        )
        SELECT
            Id,
            GameAccountId,
            ExternalRecordId,
            ItemName,
            RankType,
            TimeUtcTicks,
            TimeOffsetMinutes
        FROM WishRecords_Old;
        """);

            connection.Execute(
                """
        DROP TABLE WishRecords_Old;
        """);

            connection.Execute(
                """
        CREATE UNIQUE INDEX
            UX_WishRecords_Account_ExternalId
        ON WishRecords
        (
            GameAccountId,
            ExternalRecordId
        );
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
        }

        private sealed class LegacyAccountIdRow
        {
            public string GameAccountId { get; set; } =
                string.Empty;
        }

        public async ValueTask DisposeAsync()
        {
            await Connection.CloseAsync();
            initializeGate.Dispose();
        }
    }
}

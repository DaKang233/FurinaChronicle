using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Infrastructure.Persistence.Sqlite
{
    public sealed class FurinaDatabase : IAsyncDisposable
    {
        private const int CurrentSchemaVersion = 1;
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

        public async ValueTask DisposeAsync()
        {
            await Connection.CloseAsync();
            initializeGate.Dispose();
        }
    }
}

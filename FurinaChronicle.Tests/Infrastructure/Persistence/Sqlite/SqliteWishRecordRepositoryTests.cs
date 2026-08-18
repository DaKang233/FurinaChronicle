using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteWishRecordRepositoryTests
    {
        /// <summary>
        /// 验证：建库、建表、写入、数据库去重、按时间倒序、关闭连接、重新打开、持久化、时间偏移保存
        /// </summary>
        [Fact]
        public async Task SaveBatchAsync_DataPersistsAfterDatabaseReopens()
        {
            string diretory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(diretory);
            string databasePath = Path.Combine(diretory, "test.db3");
            Guid accountId = Guid.NewGuid();
            WishRecord[] records = [
                new WishRecord(accountId, "record-1", "芙宁娜", 5, new DateTimeOffset(2026,7,16,18,30,0,TimeSpan.FromHours(8))),
                new WishRecord(accountId, "record-2", "夏洛蒂", 4, new DateTimeOffset(2026,7,16,18,29,0,TimeSpan.FromHours(8))),
                new WishRecord(accountId, "record-3", "黎明神剑", 3, new DateTimeOffset(2026,7,16,18,28,0,TimeSpan.FromHours(8))),
                ];

            FurinaDatabase? firstDatabase = null;
            FurinaDatabase? secondDatabase = null;

            try
            {
                firstDatabase = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));
                var firstRepository = new SqliteWishRecordRepository(firstDatabase);
                var firstSave = await firstRepository.SaveBatchAsync(records);
                Assert.Equal(3, firstSave.InsertedCount);
                Assert.Equal(0, firstSave.DuplicateCount);

                var secondSave = await firstRepository.SaveBatchAsync(records);
                Assert.Equal(0, secondSave.InsertedCount);
                Assert.Equal(3, secondSave.DuplicateCount);

                // 模拟应用关闭
                await firstDatabase.DisposeAsync();
                firstDatabase = null;

                // 模拟应用重新启动
                secondDatabase = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));
                var secondRepository = new SqliteWishRecordRepository(secondDatabase);
                IReadOnlyList<WishRecord> loadedRecords = await secondRepository.GetRecentAsync(accountId, 20);
                Assert.Equal("芙宁娜", loadedRecords[0].ItemName);
                Assert.Equal(TimeSpan.FromHours(8), loadedRecords[0].Time.Offset);
            }
            finally
            {
                if (firstDatabase != null)
                {
                    await firstDatabase.DisposeAsync();
                }
                if (secondDatabase != null)
                {
                    await secondDatabase.DisposeAsync();
                }
                if (Directory.Exists(diretory))
                {
                    Directory.Delete(diretory, true);
                }
            }
        }

        [Fact]
        public async Task SaveBatchAsync_SameExternalIdForDifferentAccounts_IsAllowed()
        {
            string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string databasePath = Path.Combine(directory, "test.db3");

            var database = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));
            try
            {
                var repository = new SqliteWishRecordRepository(database);
                const string sameExternalId = "same-id";
                WishRecord[] records = [
                    new WishRecord(Guid.NewGuid(), sameExternalId, "芙宁娜", 5, DateTimeOffset.UtcNow),
                    new WishRecord(Guid.NewGuid(), sameExternalId, "芙宁娜", 5, DateTimeOffset.UtcNow),
                    ];
                var result = await repository.SaveBatchAsync(records);
                Assert.Equal(2, result.InsertedCount);
                Assert.Equal(0, result.DuplicateCount);
            }
            finally { await database.DisposeAsync(); Directory.Delete(directory, true); }
        }
    }
}

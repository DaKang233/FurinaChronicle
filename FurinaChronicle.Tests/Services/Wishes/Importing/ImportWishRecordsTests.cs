using System.Diagnostics;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Wishes.Importing;
using Xunit;

namespace FurinaChronicle.Tests.Services.Wishes.Importing;

public sealed class ImportWishRecordsTests
{
    private static readonly Guid AccountId =
        Guid.Parse("11ec0244-0f33-450a-988a-029b805cbb20");
    private static readonly Guid ArchiveId = Guid.Parse("11ec0244-0f33-450a-988a-029b805cbb21");
    private static readonly PlayerArchive Archive = new PlayerArchive(ArchiveId, "测试存档", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static readonly GameAccount Account = new GameAccount(AccountId, ArchiveId, "123456789", GameServerRegion.Asia, "测试账号", false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    [Fact]
    public async Task ExecuteAsync_FirstAndSecondImport_ReturnExpectedResults()
    {
        // Arrange
        var repository = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        var reader = new JsonWishRecordReader();
        var service = new ImportWishRecords(reader, repository);

        // Act：第一次导入
        await using MemoryStream firstStream = CreateStream(SampleJson);

        WishImportResult firstResult = await service.ExecuteAsync(firstStream, AccountId);

        // Assert：第一次
        Assert.Equal(
            new WishImportResult(
                TotalCount: 5, 
                ImportedCount: 3, 
                DuplicateCount: 1, 
                InvalidCount: 1), 
            firstResult);

        // Act：第二次导入
        // 必须创建新的 Stream。
        await using MemoryStream secondStream = CreateStream(SampleJson);

        WishImportResult secondResult = await service.ExecuteAsync(secondStream, AccountId);

        // Assert：第二次
        Assert.Equal(
            new WishImportResult(
                TotalCount: 5,
                ImportedCount: 0,
                DuplicateCount: 4,
                InvalidCount: 1),
            secondResult);

        IReadOnlyList<WishRecord> storedRecords = await repository.GetRecentAsync(AccountId, 20);

        Assert.Equal(3, storedRecords.Count);
    }

    private static MemoryStream CreateStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private const string SampleJson =
        """
        {
          "list": [
            {
              "id": "200000000000000001",
              "name": "芙宁娜",
              "rank_type": "5",
              "time": "2026-07-16T18:30:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "200000000000000003",
              "name": "黎明神剑",
              "rank_type": "3",
              "time": "2026-07-16T18:28:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "",
              "name": "无效记录",
              "rank_type": "4",
              "time": "2026-07-16T18:27:00+08:00"
            }
          ]
        }
        """;

    [Fact]
    public async Task ExecuteAsync_WithSqliteRepository_SecondImportIsDuplicatedAndDataPersists()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "test.db3");
        var database = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));
        try
        {
            var wishRepository = new SqliteWishRecordRepository(database);
            var archiveRepository = new SqlitePlayerArchiveRepository(database);
            var accountRepository = new SqliteGameAccountRepository(database);
            var reader = new JsonWishRecordReader();
            var service = new ImportWishRecords(reader, wishRepository);
            await archiveRepository.AddAsync(Archive);
            await accountRepository.AddAsync(Account);
            await using MemoryStream firstStream = CreateStream(SampleJson);
            WishImportResult firstResult = await service.ExecuteAsync(firstStream, AccountId);
            Assert.Equal(3, firstResult.ImportedCount);
            Assert.Equal(1, firstResult.DuplicateCount);
            Assert.Equal(1, firstResult.InvalidCount);
            await using MemoryStream secondStream = CreateStream(SampleJson);
            WishImportResult secondResult = await service.ExecuteAsync(secondStream, AccountId);
            Assert.Equal(0, secondResult.ImportedCount);
            Assert.Equal(4, secondResult.DuplicateCount);
            Assert.Equal(1, secondResult.InvalidCount);
            IReadOnlyList<WishRecord> stored = await wishRepository.GetRecentAsync(AccountId, 20);
            Assert.Equal(3, stored.Count);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, true);
        }
    }
}
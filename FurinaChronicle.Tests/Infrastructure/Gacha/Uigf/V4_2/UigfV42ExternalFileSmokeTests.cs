using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Uigf.V4_2;

public sealed class UigfV42ExternalFileSmokeTests
{
    public const string FileEnvironmentVariable =
        "FURINA_UIGF_TEST_FILE";

    [Fact]
    [Trait("Category", "ExternalSmoke")]
    public async Task ExecuteAsync_ConfiguredFile_ImportsPersistsPagesAndDeduplicates()
    {
        string? sourcePath =
            Environment.GetEnvironmentVariable(
                FileEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        Assert.True(
            File.Exists(sourcePath),
            $"Configured UIGF file does not exist: {sourcePath}");

        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleExternalUigfTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        var database = new FurinaDatabase(
            new SqliteDatabaseOptions(
                Path.Combine(directory, "uigf.db3")));

        try
        {
            var archives =
                new SqlitePlayerArchiveRepository(database);
            var accounts =
                new SqliteGameAccountRepository(database);
            var records =
                new SqliteWishRecordRepository(database);
            var service = new ImportUigfGachaRecords(
                new UigfV42GachaReader(),
                new EmptyGachaItemMetadataProvider(),
                archives,
                accounts,
                records);

            Guid archiveId = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await archives.AddAsync(
                new PlayerArchive(
                    archiveId,
                    "External UIGF smoke test",
                    now,
                    now));

            await using (FileStream firstStream =
                File.OpenRead(sourcePath))
            {
                GachaImportResult first =
                    await service.ExecuteAsync(
                        firstStream,
                        archiveId);

                Assert.True(first.TotalCount > 0);
                Assert.Equal(first.TotalCount, first.ImportedCount);
                Assert.Equal(0, first.DuplicateCount);
                Assert.Equal(0, first.InvalidCount);
                Assert.Equal(1, first.CreatedAccountCount);
            }

            GameAccount account = Assert.Single(
                await accounts.GetByArchiveIdAsync(archiveId));
            int storedCount =
                await records.CountAsync(account.Id);
            IReadOnlyList<FurinaChronicle.Core.Wishes.WishRecord> firstPage =
                await records.GetPageAsync(
                    account.Id,
                    offset: 0,
                    count: 50);
            Assert.Equal(
                Math.Min(50, storedCount),
                firstPage.Count);

            await using FileStream secondStream =
                File.OpenRead(sourcePath);
            GachaImportResult second =
                await service.ExecuteAsync(
                    secondStream,
                    archiveId);

            Assert.Equal(0, second.ImportedCount);
            Assert.Equal(
                second.TotalCount,
                second.DuplicateCount);
            Assert.Equal(0, second.CreatedAccountCount);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}

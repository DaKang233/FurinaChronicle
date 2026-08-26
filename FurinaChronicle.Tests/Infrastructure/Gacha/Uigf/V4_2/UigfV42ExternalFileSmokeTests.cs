using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Localization;
using FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha.Exporting;
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
                new UigfCompatibleGachaReader(
                    new UigfV42GachaReader(),
                    new EmbeddedGachaLocalizationSource(),
                    TimeProvider.System),
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
                Assert.True(first.CreatedAccountCount > 0);
            }

            IReadOnlyList<GameAccount> storedAccounts =
                await accounts.GetByArchiveIdAsync(archiveId);
            Assert.NotEmpty(storedAccounts);
            int storedCount = 0;
            foreach (GameAccount account in storedAccounts)
            {
                int accountCount =
                    await records.CountAsync(account.Id);
                storedCount += accountCount;
                Assert.Equal(
                    Math.Min(50, accountCount),
                    (await records.GetPageAsync(
                        account.Id,
                        offset: 0,
                        count: 50)).Count);
            }

            await using (FileStream duplicateStream =
                File.OpenRead(sourcePath))
            {
                GachaImportResult duplicate =
                    await service.ExecuteAsync(
                        duplicateStream,
                        archiveId);

                Assert.Equal(storedCount, duplicate.TotalCount);
                Assert.Equal(0, duplicate.ImportedCount);
                Assert.Equal(storedCount, duplicate.DuplicateCount);
                Assert.Equal(0, duplicate.InvalidCount);
                Assert.Equal(0, duplicate.CreatedAccountCount);
            }

            var loadData = new LoadGachaExportData(
                archives,
                accounts,
                records,
                TimeProvider.System);
            var exportService = new ExportUigfV42GachaRecords(
                loadData,
                new UigfV42GachaWriter(
                    new EmptyGachaItemMetadataProvider()));
            await using var exportedStream = new MemoryStream();
            GachaExportResult exported =
                await exportService.ExecuteAsync(
                    exportedStream,
                    archiveId,
                    storedAccounts.Select(account => account.Id).ToArray(),
                    UigfV42ExportOptions.Compatible);
            Assert.Equal(storedAccounts.Count, exported.AccountCount);
            Assert.Equal(storedCount, exported.RecordCount);

            exportedStream.Position = 0;
            GachaImportResult second =
                await service.ExecuteAsync(
                    exportedStream,
                    archiveId);

            Assert.Equal(0, second.ImportedCount);
            Assert.Equal(storedCount, second.TotalCount);
            Assert.Equal(storedCount, second.DuplicateCount);
            Assert.Equal(0, second.InvalidCount);
            Assert.Equal(0, second.CreatedAccountCount);
        }
        finally
        {
            await database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}

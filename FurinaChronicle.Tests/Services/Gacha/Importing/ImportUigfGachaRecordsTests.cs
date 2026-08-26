using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Tests.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Gacha.Importing;

public sealed class ImportUigfGachaRecordsTests
{
    [Fact]
    public async Task ExecuteAsync_ArchiveImport_CreatesAccountsEnrichesAndIsIdempotent()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);

        var service = CreateService(archives, accounts, records);
        string json = TestUigfJson.CreateTwoAccounts();

        GachaImportResult first = await ExecuteAsync(service, json, archive.Id);
        GachaImportResult second = await ExecuteAsync(service, json, archive.Id);

        Assert.Equal(new GachaImportResult(3, 3, 0, 0, 0, 2), first);
        Assert.Equal(new GachaImportResult(3, 0, 3, 0, 0, 0), second);

        IReadOnlyList<GameAccount> storedAccounts =
            await accounts.GetByArchiveIdAsync(archive.Id);
        Assert.Equal(2, storedAccounts.Count);

        GameAccount asiaAccount =
            Assert.Single(storedAccounts, account => account.Uid == "800000001");
        IReadOnlyList<WishRecord> asiaRecords =
            await records.GetRecentAsync(asiaAccount.Id, 20);
        Assert.Equal(2, asiaRecords.Count);

        WishRecord enriched = Assert.Single(
            asiaRecords,
            record => record.ItemId == "10000089");
        Assert.Equal("Furina", enriched.ItemName);
        Assert.Equal("Avatar", enriched.ItemType);
        Assert.Equal(5, enriched.RankType);
        Assert.Equal("301", enriched.GachaType);
        Assert.Equal("301", enriched.UigfGachaType);
    }

    [Fact]
    public async Task ExecuteAsync_TargetAccount_ImportsMatchingUidAndIgnoresOthers()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount target = ArchiveTestData.Account(
            archive.Id,
            uid: "600000001",
            region: GameServerRegion.America);
        await archives.AddAsync(archive);
        await accounts.AddAsync(target);

        var service = CreateService(archives, accounts, records);
        GachaImportResult result = await ExecuteAsync(
            service,
            TestUigfJson.CreateTwoAccounts(),
            archive.Id,
            target.Id);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.IgnoredCount);
        Assert.Equal(0, result.CreatedAccountCount);
        Assert.Single(await accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Single(await records.GetRecentAsync(target.Id, 20));
    }

    [Fact]
    public async Task ExecuteAsync_ArchiveImport_UnresolvableMetadataRejectsEntireAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);

        var service = CreateService(archives, accounts, records);
        string json = TestUigfJson.CreateTwoAccounts().Replace(
            "\"11401\"",
            "\"999999\"",
            StringComparison.Ordinal);

        GachaImportResult result = await ExecuteAsync(service, json, archive.Id);

        Assert.Equal(new GachaImportResult(3, 1, 0, 2, 0, 1), result);
        GameAccount storedAccount = Assert.Single(
            await accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Equal("600000001", storedAccount.Uid);
        Assert.Single(await records.GetRecentAsync(storedAccount.Id, 20));
    }

    [Fact]
    public async Task ExecuteAsync_TargetAccount_UnresolvableMetadataRejectsWholeImport()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount target = ArchiveTestData.Account(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia);
        await archives.AddAsync(archive);
        await accounts.AddAsync(target);

        var service = CreateService(archives, accounts, records);
        string json = TestUigfJson.Create(
            TestUigfJson.Record("1", "10000089"),
            """{"uigf_gacha_type":"301","gacha_type":"301","item_id":"999999","time":"2026-08-24 12:31:00","name":"Unknown item","item_type":"Avatar","id":"2"}""");

        GachaImportFormatException exception =
            await Assert.ThrowsAsync<GachaImportFormatException>(() =>
                ExecuteAsync(service, json, archive.Id, target.Id));

        Assert.Contains(target.Uid, exception.Message, StringComparison.Ordinal);
        Assert.Empty(await records.GetRecentAsync(target.Id, 20));
        Assert.Single(await accounts.GetByArchiveIdAsync(archive.Id));
    }

    [Fact]
    public async Task ExecuteAsync_ArchiveImport_InvalidRecordRejectsRemainingAccountRecords()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);

        var service = CreateService(archives, accounts, records);
        string json = TestUigfJson.Create(
            TestUigfJson.Record("1", "10000089"),
            """{"uigf_gacha_type":"301","gacha_type":"301","item_id":"10000089","time":"2026-08-24 12:31:00","id":""}""");

        GachaImportResult result = await ExecuteAsync(service, json, archive.Id);

        Assert.Equal(new GachaImportResult(2, 0, 0, 2, 0, 0), result);
        Assert.Empty(await accounts.GetByArchiveIdAsync(archive.Id));
    }
    private static ImportUigfGachaRecords CreateService(
        InMemoryPlayerArchiveRepository archives,
        InMemoryGameAccountRepository accounts,
        InMemoryWishRecordRepository records)
    {
        return new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new TestMetadataProvider(),
            archives,
            accounts,
            records);
    }

    private static async Task<GachaImportResult> ExecuteAsync(
        ImportUigfGachaRecords service,
        string json,
        Guid archiveId,
        Guid? accountId = null)
    {
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await service.ExecuteAsync(stream, archiveId, accountId);
    }

    private sealed class TestMetadataProvider : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaItemMetadata? metadata = itemId switch
            {
                "10000089" => new(game, itemId, "Furina", "Avatar", 5),
                "11401" => new(game, itemId, "Favonius Sword", "Weapon", 4),
                _ => null
            };
            return ValueTask.FromResult(metadata);
        }
    }
}

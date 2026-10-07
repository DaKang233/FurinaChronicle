// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Tests.Services.Gacha;
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
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
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
        IReadOnlyList<GachaRecord> asiaRecords =
            await records.GetRecentAsync(asiaAccount.Id, 20);
        Assert.Equal(2, asiaRecords.Count);

        GachaRecord enriched = Assert.Single(
            asiaRecords,
            record => record.ItemId == "10000089");
        Assert.Equal("Furina", enriched.ItemName);
        Assert.Equal("Avatar", enriched.ItemType);
        Assert.Equal(5, enriched.RankType);
        Assert.Equal("301", enriched.GachaType);
        Assert.Equal("301", enriched.UigfGachaType);
        Assert.All(
            asiaRecords,
            record =>
            {
                Assert.Equal(
                    DataOrigin.StandardImport,
                    record.Provenance.Origin);
                Assert.NotNull(record.Provenance.Timestamps.ImportedAt);
                Assert.Null(record.Provenance.Timestamps.FetchedAt);
                Assert.NotNull(record.Provenance.AcquisitionBatchId);
            });
        Assert.Single(
            asiaRecords
                .Select(record => record.Provenance.AcquisitionBatchId)
                .Distinct());
    }

    [Fact]
    public async Task ExecuteAsync_UigfImport_PersistsProvenanceThroughSqliteRepository()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        var service = new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new TestMetadataProvider(),
            context.Archives,
            context.Accounts,
            new CommitGachaRecords(context.AtomicGacha));

        GachaImportResult result = await ExecuteAsync(
            service,
            TestUigfJson.Create(TestUigfJson.Record("1", "10000089")),
            archive.Id);

        Assert.Equal(1, result.ImportedCount);
        GameAccount account = Assert.Single(
            await context.Accounts.GetByArchiveIdAsync(archive.Id));
        GachaRecord stored = Assert.Single(
            await context.Gacha.GetRecentAsync(account.Id, 10));
        Assert.Equal(DataOrigin.StandardImport, stored.Provenance.Origin);
        Assert.NotNull(stored.Provenance.Timestamps.ImportedAt);
        Assert.Null(stored.Provenance.Timestamps.FetchedAt);
        Assert.NotNull(stored.Provenance.AcquisitionBatchId);
    }

    [Fact]
    public async Task ExecuteAsync_ProposedArchiveCreatesParentsAndFactsAtomically()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("导入新档案");
        var service = new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new TestMetadataProvider(),
            context.Archives,
            context.Accounts,
            new CommitGachaRecords(context.AtomicGacha));
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            TestUigfJson.Create(
                TestUigfJson.Record("1", "10000089"))));

        GachaImportResult result = await service.ExecuteAsync(stream, archive);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(1, result.CreatedAccountCount);
        Assert.Equal(archive, await context.Archives.GetByIdAsync(archive.Id));
        GameAccount account = Assert.Single(
            await context.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Single(await context.Gacha.GetRecentAsync(account.Id, 10));
    }

    [Fact]
    public async Task ExecuteAsync_ProposedArchiveWithNoValidFactsLeavesNoParents()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("不应创建的档案");
        var service = new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new TestMetadataProvider(),
            context.Archives,
            context.Accounts,
            new CommitGachaRecords(context.AtomicGacha));
        string invalid = TestUigfJson.Create(
            """{"uigf_gacha_type":"301","gacha_type":"301","item_id":"10000089","time":"2026-08-24 12:31:00","id":""}""");
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(invalid));

        GachaImportResult result = await service.ExecuteAsync(stream, archive);

        Assert.Equal(0, result.ImportedCount);
        Assert.Null(await context.Archives.GetByIdAsync(archive.Id));
        Assert.Empty(await context.Accounts.GetByArchiveIdAsync(archive.Id));
    }

    [Fact]
    public async Task ExecuteAsync_TargetAccount_ImportsMatchingUidAndIgnoresOthers()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
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
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
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
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
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
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
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

    [Fact]
    public async Task ExecuteAsync_TargetAccount_InvalidTimezoneRejectsImport()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
        var archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = CreateService(archives, accounts, records);
        var target = ArchiveTestData.Account(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia);
        await accounts.AddAsync(target);

        string json = TestUigfJson
            .Create(TestUigfJson.Record("1", "10000089"))
            .Replace(
                "\"timezone\": 8",
                "\"timezone\": 15",
                StringComparison.Ordinal);

        await Assert.ThrowsAsync<GachaImportFormatException>(() =>
            ExecuteAsync(service, json, archive.Id, target.Id));

        Assert.Empty(await records.GetRecentAsync(target.Id, 20));
    }

    [Fact]
    public async Task ExecuteAsync_TargetAccount_UnsupportedLanguageRejectsImport()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
        var archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = CreateService(archives, accounts, records);
        var target = ArchiveTestData.Account(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia);
        await accounts.AddAsync(target);

        string json = TestUigfJson
            .Create(TestUigfJson.Record("1", "10000089"))
            .Replace(
                "\"lang\": \"zh-cn\"",
                "\"lang\": \"xx-yy\"",
                StringComparison.Ordinal);

        await Assert.ThrowsAsync<GachaImportFormatException>(() =>
            ExecuteAsync(service, json, archive.Id, target.Id));

        Assert.Empty(await records.GetRecentAsync(target.Id, 20));
    }

    [Fact]
    public async Task ExecuteAsync_TargetAccount_MissingListRejectsImport()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        var records = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
        var archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = CreateService(archives, accounts, records);
        var target = ArchiveTestData.Account(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia);
        await accounts.AddAsync(target);

        string json = TestUigfJson.Create().Replace(
            "\"list\": []",
            "\"not_list\": []",
            StringComparison.Ordinal);

        await Assert.ThrowsAsync<GachaImportFormatException>(() =>
            ExecuteAsync(service, json, archive.Id, target.Id));

        Assert.Empty(await records.GetRecentAsync(target.Id, 20));
    }

    private static ImportUigfGachaRecords CreateService(
        InMemoryPlayerArchiveRepository archives,
        InMemoryGameAccountRepository accounts,
        InMemoryGachaRecordRepository records)
    {
        return new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new TestMetadataProvider(),
            archives,
            accounts,
            new RepositoryGachaCommitter(records, accounts));
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

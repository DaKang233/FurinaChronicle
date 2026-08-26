using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Exporting;

public sealed class GachaExportTests
{
    [Fact]
    public async Task UigfWriter_CompatiblePreset_WritesReferenceFields()
    {
        GachaExportDocument document = CreateDocument();
        var writer = new UigfV42GachaWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            document,
            UigfV42ExportOptions.Compatible);

        stream.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        JsonElement root = json.RootElement;
        JsonElement info = root.GetProperty("info");
        Assert.Equal("v4.2", info.GetProperty("version").GetString());
        Assert.Equal("zh-cn", info.GetProperty("lang").GetString());
        JsonElement exportTimestamp = info.GetProperty("export_timestamp");
        Assert.Equal(JsonValueKind.Number, exportTimestamp.ValueKind);
        Assert.Equal(1787589041, exportTimestamp.GetInt64());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("hk4e_ugc").ValueKind);

        JsonElement account = root.GetProperty("hk4e")[0];
        Assert.Equal("800000001", account.GetProperty("uid").GetString());
        Assert.Equal(8, account.GetProperty("timezone").GetInt32());
        Assert.False(account.TryGetProperty("lang", out _));

        JsonElement record = account.GetProperty("list")[0];
        string[] expectedFields =
        [
            "gacha_type", "item_id", "count", "time", "name",
            "item_type", "rank_type", "id", "uigf_gacha_type"
        ];
        Assert.Equal(
            expectedFields,
            record.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public async Task UigfWriter_MinimalPreset_OmitsEveryOptionalField()
    {
        var writer = new UigfV42GachaWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            CreateDocument(),
            UigfV42ExportOptions.Minimal);

        stream.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        JsonElement root = json.RootElement;
        Assert.False(root.GetProperty("info").TryGetProperty("lang", out _));
        Assert.False(root.TryGetProperty("hk4e_ugc", out _));

        JsonElement record =
            root.GetProperty("hk4e")[0].GetProperty("list")[0];
        Assert.False(record.TryGetProperty("count", out _));
        Assert.False(record.TryGetProperty("name", out _));
        Assert.False(record.TryGetProperty("item_type", out _));
        Assert.False(record.TryGetProperty("rank_type", out _));
        Assert.True(record.TryGetProperty("item_id", out _));
        Assert.True(record.TryGetProperty("id", out _));
    }

    [Fact]
    public async Task ExportThenImport_ExistingDatabase_ReportsAllDuplicates()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        GachaExportDocument source = CreateDocument();
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount account = source.Accounts[0].Account with
        {
            PlayerArchiveId = archive.Id
        };
        WishRecord[] existingRecords = source.Accounts[0].Records
            .Select(record => record with
            {
                GameAccountId = account.Id
            })
            .ToArray();
        var records = new InMemoryWishRecordRepository(existingRecords);
        await archives.AddAsync(archive);
        await accounts.AddAsync(account);

        var loader = new LoadGachaExportData(
            archives,
            accounts,
            records,
            TimeProvider.System);
        var exportService = new ExportUigfV42GachaRecords(
            loader,
            new UigfV42GachaWriter(
                new EmptyGachaItemMetadataProvider()));
        await using var stream = new MemoryStream();
        GachaExportResult exported = await exportService.ExecuteAsync(
            stream,
            archive.Id,
            [account.Id],
            UigfV42ExportOptions.Compatible);

        stream.Position = 0;
        var importService = new ImportUigfGachaRecords(
            new UigfV42GachaReader(),
            new EmptyGachaItemMetadataProvider(),
            archives,
            accounts,
            records);
        GachaImportResult imported = await importService.ExecuteAsync(
            stream,
            archive.Id);

        Assert.Equal(existingRecords.Length, exported.RecordCount);
        Assert.Equal(0, imported.ImportedCount);
        Assert.Equal(existingRecords.Length, imported.DuplicateCount);
        Assert.Equal(0, imported.InvalidCount);
        Assert.Equal(0, imported.CreatedAccountCount);
    }

    [Fact]
    public async Task TableWriter_Csv_KeepsAccountsContiguousAndCalculatesPity()
    {
        var writer = new GachaTableExportWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            CreateDocument(),
            new GachaTableExportOptions(
                GachaTableFormat.Csv,
                GachaExportLanguages.SimplifiedChinese));

        string text = Encoding.UTF8.GetString(stream.ToArray());
        string[] lines = text
            .TrimStart('\uFEFF')
            .Split(
                ["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(5, lines.Length);
        Assert.Contains("保底内计数", lines[0]);
        Assert.StartsWith("800000001,", lines[1]);
        Assert.Contains(",+08:00,", lines[1]);
        Assert.EndsWith(",1,1", lines[1]);
        Assert.StartsWith("800000001,", lines[2]);
        Assert.EndsWith(",2,2", lines[2]);
        Assert.StartsWith("800000001,", lines[3]);
        Assert.EndsWith(",3,1", lines[3]);
        Assert.StartsWith("600000001,", lines[4]);
        Assert.EndsWith(",1,1", lines[4]);
    }

    [Fact]
    public async Task TableWriter_Csv_NeutralizesSpreadsheetFormulaPrefixes()
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount account = source.Accounts[0];
        WishRecord template = account.Records[0];
        string[] dangerousNames = ["=1+1", "+1+1", "-1+1", "@SUM", "  =1+1"];
        WishRecord[] records = dangerousNames
            .Select((name, index) => template with
            {
                ExternalRecordId = $"danger-{index}",
                ItemName = name
            })
            .ToArray();
        var document = source with
        {
            Accounts = [account with { Records = records }]
        };
        var writer = new GachaTableExportWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            document,
            new GachaTableExportOptions(
                GachaTableFormat.Csv,
                GachaExportLanguages.SimplifiedChinese));

        string text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains(",'=1+1,", text);
        Assert.Contains(",'+1+1,", text);
        Assert.Contains(",'-1+1,", text);
        Assert.Contains(",'@SUM,", text);
        Assert.Contains(",'  =1+1,", text);
    }

    [Fact]
    public async Task TableWriter_Xlsx_WritesValidWorkbookParts()
    {
        var writer = new GachaTableExportWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            CreateDocument(),
            new GachaTableExportOptions(
                GachaTableFormat.Xlsx,
                GachaExportLanguages.English));

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        ZipArchiveEntry worksheet =
            Assert.IsType<ZipArchiveEntry>(
                archive.GetEntry("xl/worksheets/sheet1.xml"));
        using var reader = new StreamReader(worksheet.Open());
        string xml = await reader.ReadToEndAsync();

        Assert.Contains("Timezone Offset", xml);
        Assert.Contains("Character Event Wish", xml);
        Assert.Contains("800000001", xml);
        Assert.Contains("600000001", xml);
    }

    [Fact]
    public async Task LoadData_AccountFromAnotherArchive_Throws()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive firstArchive = ArchiveTestData.Archive();
        PlayerArchive secondArchive = ArchiveTestData.Archive();
        GameAccount foreignAccount =
            ArchiveTestData.Account(secondArchive.Id, uid: "600000001");
        await archives.AddAsync(firstArchive);
        await archives.AddAsync(secondArchive);
        await accounts.AddAsync(foreignAccount);
        var loader = new LoadGachaExportData(
            archives,
            accounts,
            new InMemoryWishRecordRepository(Array.Empty<WishRecord>()),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.ExecuteAsync(
                firstArchive.Id,
                [foreignAccount.Id]));
    }

    private static GachaExportDocument CreateDocument()
    {
        Guid archiveId = Guid.NewGuid();
        GameAccount asia = ArchiveTestData.Account(
            archiveId,
            uid: "800000001",
            region: GameServerRegion.Asia);
        GameAccount america = ArchiveTestData.Account(
            archiveId,
            uid: "600000001",
            region: GameServerRegion.America);

        WishRecord[] asiaRecords =
        [
            Record(
                asia.Id,
                "1",
                "15301",
                "鸦羽弓",
                "武器",
                3,
                new DateTimeOffset(
                    2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(8))),
            Record(
                asia.Id,
                "2",
                "10000089",
                "芙宁娜",
                "角色",
                5,
                new DateTimeOffset(
                    2026, 1, 1, 10, 1, 0, TimeSpan.FromHours(8))),
            Record(
                asia.Id,
                "3",
                "15301",
                "鸦羽弓",
                "武器",
                3,
                new DateTimeOffset(
                    2026, 1, 1, 10, 2, 0, TimeSpan.FromHours(8)))
        ];
        WishRecord[] americaRecords =
        [
            Record(
                america.Id,
                "4",
                "11401",
                "西风剑",
                "武器",
                4,
                new DateTimeOffset(
                    2026, 1, 1, 10, 0, 0, TimeSpan.FromHours(-5)))
        ];

        return new GachaExportDocument(
            DateTimeOffset.FromUnixTimeSeconds(1787589041),
            [
                new GachaExportAccount(asia, 8, asiaRecords),
                new GachaExportAccount(america, -5, americaRecords)
            ]);
    }

    private static WishRecord Record(
        Guid accountId,
        string externalId,
        string itemId,
        string name,
        string itemType,
        int rankType,
        DateTimeOffset time)
    {
        return new WishRecord(
            accountId,
            externalId,
            name,
            rankType,
            time)
        {
            ItemId = itemId,
            ItemType = itemType,
            GachaType = "301",
            UigfGachaType = "301",
            Count = 1
        };
    }
}

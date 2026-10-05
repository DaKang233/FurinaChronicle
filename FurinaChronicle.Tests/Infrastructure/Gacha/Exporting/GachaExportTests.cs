// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Abstractions;
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
    public async Task UigfWriter_MissingItemId_ResolvesItFromItemName()
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount sourceAccount = source.Accounts[0];
        GachaRecord sourceRecord = sourceAccount.Records[0];
        GachaExportDocument document = source with
        {
            Accounts =
            [
                sourceAccount with
                {
                    Records = [sourceRecord with { ItemId = null }]
                }
            ]
        };
        var metadata = new GachaItemMetadata(
            GachaGame.GenshinImpact,
            sourceRecord.ItemId!,
            sourceRecord.ItemName!,
            sourceRecord.ItemType!,
            sourceRecord.RankType);
        var writer = new UigfV42GachaWriter(
            new FixedMetadataProvider(metadata));
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            document,
            UigfV42ExportOptions.Minimal);

        stream.Position = 0;
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        JsonElement record =
            json.RootElement.GetProperty("hk4e")[0].GetProperty("list")[0];
        Assert.Equal(
            metadata.ItemId,
            record.GetProperty("item_id").GetString());
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
        GachaRecord[] existingRecords = source.Accounts[0].Records
            .Select(record => record with
            {
                GameAccountId = account.Id
            })
            .ToArray();
        var records = new InMemoryGachaRecordRepository(existingRecords);
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
        Assert.Contains("所选范围保底内计数", lines[0]);
        Assert.Contains("数据来源", lines[0]);
        Assert.Contains("完整性声明", lines[0]);
        Assert.StartsWith("800000001,", lines[1]);
        Assert.Contains(",'+08:00,", lines[1]);
        Assert.Contains(",1,1,", lines[1]);
        Assert.StartsWith("800000001,", lines[2]);
        Assert.Contains(",2,2,", lines[2]);
        Assert.StartsWith("800000001,", lines[3]);
        Assert.Contains(",3,1,", lines[3]);
        Assert.StartsWith("600000001,", lines[4]);
        Assert.Contains(",1,1,", lines[4]);
        Assert.Contains(",OfficialApi,", lines[1]);
        Assert.EndsWith(",none", lines[1]);
    }

    [Fact]
    public async Task TableWriter_Csv_NeutralizesSpreadsheetFormulaPrefixes()
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount account = source.Accounts[0];
        GachaRecord template = account.Records[0];
        string[] dangerousNames = ["=1+1", "+1+1", "-1+1", "@SUM", "  =1+1"];
        GachaRecord[] records = dangerousNames
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

    [Theory]
    [InlineData(GachaExportLanguages.SimplifiedChinese, "角色")]
    [InlineData(GachaExportLanguages.English, "Character")]
    public async Task TableWriter_CanonicalMetadata_LocalizesForeignItemType(
        string language,
        string expectedType)
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount account = source.Accounts[0];
        GachaRecord record = account.Records[1] with
        {
            ItemType = "キャラクター"
        };
        var document = source with
        {
            Accounts = [account with { Records = [record] }]
        };
        var metadata = new GachaItemMetadata(
            GachaGame.GenshinImpact,
            "10000089",
            "Furina",
            "Avatar",
            5);
        var writer = new GachaTableExportWriter(
            new FixedMetadataProvider(metadata));
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            document,
            new GachaTableExportOptions(
                GachaTableFormat.Csv,
                language));

        string text = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains($",{expectedType},", text);
        Assert.Contains("キャラクター", text);
    }

    [Fact]
    public async Task TableWriter_MissingRankAndMetadata_StillProducesReadableExport()
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount account = source.Accounts[0];
        GachaRecord record = account.Records[0] with
        {
            ItemId = "999999",
            RankType = null
        };
        var document = source with
        {
            Accounts = [account with { Records = [record] }]
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
        Assert.Contains("鸦羽弓", text);
        Assert.Contains(",999999,", text);
        Assert.Contains(",none", text);
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
        Assert.Contains("Character Event Gacha", xml);
        Assert.Contains("800000001", xml);
        Assert.Contains("600000001", xml);
    }

    [Fact]
    public async Task TableWriter_Xlsx_ReplacesInvalidXmlCharactersAndPreservesEmoji()
    {
        GachaExportDocument source = CreateDocument();
        GachaExportAccount account = source.Accounts[0];
        GachaRecord record = account.Records[0] with
        {
            ItemId = "999999",
            ItemName = "Furina\u0001😀"
        };
        var document = source with
        {
            Accounts = [account with { Records = [record] }]
        };
        var writer = new GachaTableExportWriter(
            new EmptyGachaItemMetadataProvider());
        await using var stream = new MemoryStream();

        await writer.WriteAsync(
            stream,
            document,
            new GachaTableExportOptions(
                GachaTableFormat.Xlsx,
                GachaExportLanguages.English));

        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        ZipArchiveEntry worksheet =
            Assert.IsType<ZipArchiveEntry>(
                archive.GetEntry("xl/worksheets/sheet1.xml"));
        using var reader = new StreamReader(worksheet.Open());
        string xml = await reader.ReadToEndAsync();

        Assert.Contains("Furina�😀", xml);
        Assert.DoesNotContain(
            "\u0001",
            xml,
            StringComparison.Ordinal);
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
            new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>()),
            TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loader.ExecuteAsync(
                firstArchive.Id,
                [foreignAccount.Id]));
    }

    private sealed class FixedMetadataProvider(
        GachaItemMetadata metadata) : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaItemMetadata? result =
                game == metadata.Game &&
                string.Equals(itemId, metadata.ItemId, StringComparison.Ordinal)
                    ? metadata
                    : null;
            return ValueTask.FromResult(result);
        }

        public ValueTask<GachaItemMetadata?> FindByNameAsync(
            GachaGame game,
            string itemName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool matchesName =
                string.Equals(itemName, metadata.Name, StringComparison.Ordinal) ||
                metadata.LocalizedNames?.Values.Contains(
                    itemName,
                    StringComparer.Ordinal) == true;
            GachaItemMetadata? result =
                game == metadata.Game && matchesName
                    ? metadata
                    : null;
            return ValueTask.FromResult(result);
        }
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

        GachaRecord[] asiaRecords =
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
        GachaRecord[] americaRecords =
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
            ])
        {
            SourceArchive = new PlayerArchive(
                archiveId,
                "Export Archive",
                DateTimeOffset.FromUnixTimeSeconds(1),
                DateTimeOffset.FromUnixTimeSeconds(2)),
            CompletenessAssertion = "none",
        };
    }

    private static GachaRecord Record(
        Guid accountId,
        string externalId,
        string itemId,
        string name,
        string itemType,
        int rankType,
        DateTimeOffset time)
    {
        return new GachaRecord(
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
            Count = 1,
            Provenance = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    FetchedAt: new DateTimeOffset(
                        2026,
                        1,
                        2,
                        3,
                        4,
                        5,
                        TimeSpan.FromHours(8))))
        };
    }
}

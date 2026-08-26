using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.Infrastructure.Gacha.Exporting;

public sealed class GachaTableExportWriter(
    IGachaItemMetadataProvider metadataProvider)
    : IGachaTableExportWriter
{
    public async Task WriteAsync(
        Stream destination,
        GachaExportDocument document,
        GachaTableExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "The destination stream is not writable.",
                nameof(destination));
        }

        options.Validate();
        var resolver = new GachaExportValueResolver(metadataProvider);
        IReadOnlyList<string> headers = GetHeaders(options.Language);

        if (options.Format == GachaTableFormat.Csv)
        {
            await WriteCsvAsync(
                destination,
                document,
                options.Language,
                resolver,
                headers,
                cancellationToken);
            return;
        }

        await WriteXlsxAsync(
            destination,
            document,
            options.Language,
            resolver,
            headers,
            cancellationToken);
    }

    private static async Task WriteCsvAsync(
        Stream destination,
        GachaExportDocument document,
        string language,
        GachaExportValueResolver resolver,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            bufferSize: 16 * 1024,
            leaveOpen: true);

        await writer.WriteLineAsync(
            string.Join(",", headers.Select(value => EscapeCsv(value))));

        await ForEachRowAsync(
            document,
            language,
            resolver,
            async row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(
                    string.Join(
                        ",",
                        row.Select((value, index) =>
                            EscapeCsv(value, index is 4 or 5))));
            },
            cancellationToken);
        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteXlsxAsync(
        Stream destination,
        GachaExportDocument document,
        string language,
        GachaExportValueResolver resolver,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(
            destination,
            ZipArchiveMode.Create,
            leaveOpen: true);

        WriteContentTypes(archive);
        WriteRootRelationships(archive);
        WriteWorkbook(archive);
        WriteWorkbookRelationships(archive);

        ZipArchiveEntry worksheetEntry =
            archive.CreateEntry(
                "xl/worksheets/sheet1.xml",
                CompressionLevel.Optimal);
        await using Stream worksheetStream = worksheetEntry.Open();
        using XmlWriter worksheet = XmlWriter.Create(
            worksheetStream,
            new XmlWriterSettings
            {
                Async = true,
                Encoding = new UTF8Encoding(false),
                CloseOutput = false
            });

        await worksheet.WriteStartDocumentAsync();
        await worksheet.WriteStartElementAsync(
            null,
            "worksheet",
            "http://schemas.openxmlformats.org/spreadsheetml/2006/main");
        await worksheet.WriteStartElementAsync(null, "sheetData", null);

        int rowNumber = 1;
        await WriteWorksheetRowAsync(
            worksheet,
            rowNumber++,
            headers);

        await ForEachRowAsync(
            document,
            language,
            resolver,
            async row =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteWorksheetRowAsync(
                    worksheet,
                    rowNumber++,
                    row);
            },
            cancellationToken);

        await worksheet.WriteEndElementAsync();
        await worksheet.WriteEndElementAsync();
        await worksheet.WriteEndDocumentAsync();
        await worksheet.FlushAsync();
    }

    private static async Task ForEachRowAsync(
        GachaExportDocument document,
        string language,
        GachaExportValueResolver resolver,
        Func<IReadOnlyList<string>, Task> writeRow,
        CancellationToken cancellationToken)
    {
        foreach (GachaExportAccount account in document.Accounts)
        {
            int index = 0;
            Dictionary<string, int> pityByPool =
                new(StringComparer.Ordinal);

            foreach (WishRecord record in account.Records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                index++;

                string poolKey =
                    record.UigfGachaType ??
                    record.GachaType ??
                    string.Empty;
                int pityCount = pityByPool.GetValueOrDefault(poolKey) + 1;
                pityByPool[poolKey] = pityCount;

                string itemName = await resolver.GetItemNameAsync(
                    record,
                    language,
                    cancellationToken);
                string itemType = await resolver.GetItemTypeAsync(
                    record,
                    language,
                    cancellationToken);
                string poolName =
                    GachaExportValueResolver.GetPoolName(record, language);
                int rankType =
                    await resolver.GetRankTypeAsync(
                        record,
                        cancellationToken);

                await writeRow(
                [
                    account.Account.Uid,
                    record.ExternalRecordId,
                    record.Time.ToString(
                        "yyyy-MM-dd HH:mm:ss",
                        CultureInfo.InvariantCulture),
                    FormatOffset(record.Time.Offset),
                    itemName,
                    itemType,
                    poolName,
                    index.ToString(CultureInfo.InvariantCulture),
                    pityCount.ToString(CultureInfo.InvariantCulture)
                ]);

                if (rankType == 5)
                {
                    pityByPool[poolKey] = 0;
                }
            }
        }
    }

    private static IReadOnlyList<string> GetHeaders(string language)
    {
        return language switch
        {
            GachaExportLanguages.TraditionalChinese =>
            [
                "UID", "ExternalID", "時間", "時區偏移",
                "物品名稱", "物品類型", "卡池", "序號", "保底內計數"
            ],
            GachaExportLanguages.English =>
            [
                "UID", "ExternalID", "Time", "Timezone Offset",
                "Item Name", "Item Type", "Wish Type", "Index", "Pity Count"
            ],
            _ =>
            [
                "UID", "ExternalID", "时间", "时区偏移",
                "物品名称", "物品类型", "卡池", "序号", "保底内计数"
            ]
        };
    }

    private static string FormatOffset(TimeSpan offset)
    {
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        offset = offset.Duration();
        return $"{sign}{offset.Hours:00}:{offset.Minutes:00}";
    }

    private static string EscapeCsv(
        string value,
        bool neutralizeFormula = false)
    {
        string safeValue =
            neutralizeFormula && HasSpreadsheetFormulaPrefix(value)
            ? $"'{value}"
            : value;

        if (!safeValue.Contains(',') &&
            !safeValue.Contains('"') &&
            !safeValue.Contains('\r') &&
            !safeValue.Contains('\n'))
        {
            return safeValue;
        }

        return $"\"{safeValue.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static bool HasSpreadsheetFormulaPrefix(string value)
    {
        ReadOnlySpan<char> trimmed = value.AsSpan().TrimStart();
        return !trimmed.IsEmpty &&
            trimmed[0] is '=' or '+' or '-' or '@';
    }

    private static async Task WriteWorksheetRowAsync(
        XmlWriter writer,
        int rowNumber,
        IReadOnlyList<string> values)
    {
        await writer.WriteStartElementAsync(null, "row", null);
        await writer.WriteAttributeStringAsync(
            null,
            "r",
            null,
            rowNumber.ToString(CultureInfo.InvariantCulture));

        for (int column = 0; column < values.Count; column++)
        {
            await writer.WriteStartElementAsync(null, "c", null);
            await writer.WriteAttributeStringAsync(
                null,
                "r",
                null,
                $"{GetColumnName(column + 1)}{rowNumber}");
            await writer.WriteAttributeStringAsync(null, "t", null, "inlineStr");
            await writer.WriteStartElementAsync(null, "is", null);
            await writer.WriteStartElementAsync(null, "t", null);
            await writer.WriteAttributeStringAsync(
                "xml",
                "space",
                "http://www.w3.org/XML/1998/namespace",
                "preserve");
            await writer.WriteStringAsync(values[column]);
            await writer.WriteEndElementAsync();
            await writer.WriteEndElementAsync();
            await writer.WriteEndElementAsync();
        }

        await writer.WriteEndElementAsync();
    }

    private static string GetColumnName(int column)
    {
        var result = new StringBuilder();
        while (column > 0)
        {
            column--;
            result.Insert(0, (char)('A' + column % 26));
            column /= 26;
        }
        return result.ToString();
    }

    private static void WriteContentTypes(ZipArchive archive)
    {
        WriteXmlEntry(
            archive,
            "[Content_Types].xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
            </Types>
            """);
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        WriteXmlEntry(
            archive,
            "_rels/.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
    }

    private static void WriteWorkbook(ZipArchive archive)
    {
        WriteXmlEntry(
            archive,
            "xl/workbook.xml",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets><sheet name="Gacha Records" sheetId="1" r:id="rId1"/></sheets>
            </workbook>
            """);
    }

    private static void WriteWorkbookRelationships(ZipArchive archive)
    {
        WriteXmlEntry(
            archive,
            "xl/_rels/workbook.xml.rels",
            """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
            </Relationships>
            """);
    }

    private static void WriteXmlEntry(
        ZipArchive archive,
        string name,
        string content)
    {
        ZipArchiveEntry entry =
            archive.CreateEntry(name, CompressionLevel.Optimal);
        using Stream stream = entry.Open();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(false));
        writer.Write(content);
    }
}

using System.Globalization;
using System.Text.Json;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;

public sealed class UigfV42GachaWriter(
    IGachaItemMetadataProvider metadataProvider)
    : IUigfV42ExportWriter
{
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    public async Task WriteAsync(
        Stream destination,
        GachaExportDocument document,
        UigfV42ExportOptions options,
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
        await using var writer = new Utf8JsonWriter(
            destination,
            new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();
        WriteInfo(writer, document, options);

        writer.WritePropertyName("hk4e");
        writer.WriteStartArray();
        foreach (GachaExportAccount account in document.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAccountAsync(
                writer,
                resolver,
                account,
                options,
                cancellationToken);
        }
        writer.WriteEndArray();

        if (options.IncludeEmptyHk4eUgc)
        {
            writer.WritePropertyName("hk4e_ugc");
            writer.WriteStartArray();
            writer.WriteEndArray();
        }

        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
    }

    private static void WriteInfo(
        Utf8JsonWriter writer,
        GachaExportDocument document,
        UigfV42ExportOptions options)
    {
        writer.WritePropertyName("info");
        writer.WriteStartObject();
        writer.WriteNumber(
            "export_timestamp",
            document.ExportedAt.ToUnixTimeSeconds());
        writer.WriteString("export_app", "FurinaChronicle");
        writer.WriteString("export_app_version", "1.0.0");
        writer.WriteString("version", "v4.2");
        if (options.IncludeInfoLanguage)
        {
            writer.WriteString("lang", options.Language);
        }
        writer.WriteEndObject();
    }

    private static async Task WriteAccountAsync(
        Utf8JsonWriter writer,
        GachaExportValueResolver resolver,
        GachaExportAccount account,
        UigfV42ExportOptions options,
        CancellationToken cancellationToken)
    {
        writer.WriteStartObject();
        writer.WriteString("uid", account.Account.Uid);
        writer.WriteNumber("timezone", account.Timezone);
        if (options.IncludeAccountLanguage)
        {
            writer.WriteString("lang", options.Language);
        }

        writer.WritePropertyName("list");
        writer.WriteStartArray();
        foreach (WishRecord record in account.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteRecordAsync(
                writer,
                resolver,
                account.Timezone,
                record,
                options,
                cancellationToken);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static async Task WriteRecordAsync(
        Utf8JsonWriter writer,
        GachaExportValueResolver resolver,
        int timezone,
        WishRecord record,
        UigfV42ExportOptions options,
        CancellationToken cancellationToken)
    {
        string gachaType = Required(record.GachaType, record, "gacha_type");
        string uigfGachaType = Required(
            record.UigfGachaType,
            record,
            "uigf_gacha_type");
        string itemId = Required(record.ItemId, record, "item_id");

        writer.WriteStartObject();
        writer.WriteString("gacha_type", gachaType);
        writer.WriteString("item_id", itemId);
        if (options.IncludeCount)
        {
            writer.WriteString(
                "count",
                record.Count.ToString(CultureInfo.InvariantCulture));
        }
        writer.WriteString(
            "time",
            record.Time
                .ToOffset(TimeSpan.FromHours(timezone))
                .ToString(TimeFormat, CultureInfo.InvariantCulture));
        if (options.IncludeName)
        {
            writer.WriteString(
                "name",
                await resolver.GetItemNameAsync(
                    record,
                    options.Language,
                    cancellationToken));
        }
        if (options.IncludeItemType)
        {
            writer.WriteString(
                "item_type",
                await resolver.GetItemTypeAsync(
                    record,
                    options.Language,
                    cancellationToken));
        }
        if (options.IncludeRankType)
        {
            writer.WriteString(
                "rank_type",
                (await resolver.GetRankTypeAsync(record, cancellationToken))
                    .ToString(CultureInfo.InvariantCulture));
        }
        writer.WriteString("id", record.ExternalRecordId);
        writer.WriteString("uigf_gacha_type", uigfGachaType);
        writer.WriteEndObject();
    }

    private static string Required(
        string? value,
        WishRecord record,
        string field)
    {
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException(
                $"记录 {record.ExternalRecordId} 缺少 UIGF 必需字段 {field}。");
    }
}

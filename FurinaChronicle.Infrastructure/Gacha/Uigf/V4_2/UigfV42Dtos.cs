using System.Text.Json;
using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;

internal sealed class UigfV42DocumentDto
{
    [JsonPropertyName("info")]
    public UigfV42InfoDto? Info { get; init; }

    [JsonPropertyName("hk4e")]
    public List<UigfV42Hk4eAccountDto?>? Hk4e { get; init; }
}

internal sealed class UigfV42InfoDto
{
    [JsonPropertyName("export_timestamp")]
    public JsonElement ExportTimestamp { get; init; }

    [JsonPropertyName("export_app")]
    public string? ExportApp { get; init; }

    [JsonPropertyName("export_app_version")]
    public string? ExportAppVersion { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }
}

internal sealed class UigfV42Hk4eAccountDto
{
    [JsonPropertyName("uid")]
    public JsonElement Uid { get; init; }

    [JsonPropertyName("timezone")]
    public int? Timezone { get; init; }

    [JsonPropertyName("lang")]
    public string? Language { get; init; }

    [JsonPropertyName("list")]
    public List<UigfV42Hk4eRecordDto?>? List { get; init; }
}

internal sealed class UigfV42Hk4eRecordDto
{
    [JsonPropertyName("uigf_gacha_type")]
    public string? UigfGachaType { get; init; }

    [JsonPropertyName("gacha_type")]
    public string? GachaType { get; init; }

    [JsonPropertyName("item_id")]
    public string? ItemId { get; init; }

    [JsonPropertyName("count")]
    public string? Count { get; init; }

    [JsonPropertyName("time")]
    public string? Time { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("item_type")]
    public string? ItemType { get; init; }

    [JsonPropertyName("rank_type")]
    public string? RankType { get; init; }

    [JsonPropertyName("id")]
    public string? Id { get; init; }
}

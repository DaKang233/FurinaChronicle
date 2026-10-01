// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed class EmbeddedGachaEventCatalog : IGachaEventCatalog
{
    private const string ResourceSuffix =
        "Gacha.Metadata.Assets.genshin-gacha-events.v1.json";
    private readonly Lazy<Task<IReadOnlyList<GachaEventPeriod>>> periods;

    public EmbeddedGachaEventCatalog()
    {
        periods = new Lazy<Task<IReadOnlyList<GachaEventPeriod>>>(
            LoadAsync,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
        GachaGame game,
        CancellationToken cancellationToken = default)
    {
        if (game != GachaGame.GenshinImpact)
        {
            return [];
        }

        return await periods.Value.WaitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<GachaEventPeriod>> LoadAsync()
    {
        Assembly assembly = typeof(EmbeddedGachaEventCatalog).Assembly;
        string resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(
                ResourceSuffix,
                StringComparison.Ordinal));
        await using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                "The embedded gacha event dataset is missing.");
        EmbeddedDataset? dataset = await JsonSerializer.DeserializeAsync<EmbeddedDataset>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            });
        if (dataset is null)
        {
            throw new InvalidDataException(
                "The embedded gacha event dataset is empty.");
        }

        ValidateHeader(dataset);
        var result = new List<GachaEventPeriod>(dataset.Periods.Count);
        var periodIds = new HashSet<string>(StringComparer.Ordinal);
        var bannerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (EmbeddedPeriod sourcePeriod in dataset.Periods)
        {
            if (!periodIds.Add(sourcePeriod.Id))
            {
                throw new InvalidDataException(
                    $"Duplicate gacha event period id: {sourcePeriod.Id}");
            }
            if (!Enum.TryParse(
                    sourcePeriod.PoolGroup,
                    ignoreCase: false,
                    out GachaPoolGroup poolGroup) ||
                poolGroup is GachaPoolGroup.Unknown or
                    GachaPoolGroup.Standard or
                    GachaPoolGroup.Novice)
            {
                throw new InvalidDataException(
                    $"Unsupported gacha pool group: {sourcePeriod.PoolGroup}");
            }

            DateTimeOffset startsAt = ParseTime(sourcePeriod.StartsAt);
            DateTimeOffset endsAt = ParseTime(sourcePeriod.EndsAt);
            if (endsAt < startsAt)
            {
                throw new InvalidDataException(
                    $"Invalid gacha event interval: {sourcePeriod.Id}");
            }

            GachaEventBanner[] banners = sourcePeriod.Banners.Select(banner =>
            {
                if (!bannerIds.Add(banner.Id))
                {
                    throw new InvalidDataException(
                        $"Duplicate gacha banner id: {banner.Id}");
                }
                if (string.IsNullOrWhiteSpace(banner.Name))
                {
                    throw new InvalidDataException(
                        $"Gacha banner {banner.Id} has no name.");
                }
                return new GachaEventBanner(
                    banner.Id,
                    banner.Name.Trim(),
                    banner.GachaType,
                    NormalizeUrl(banner.ImageUrl),
                    NormalizeUrl(banner.BackupImageUrl),
                    NormalizeItemIds(banner.UpFiveStarItemIds),
                    NormalizeItemIds(banner.UpFourStarItemIds));
            }).ToArray();
            if (banners.Length == 0)
            {
                throw new InvalidDataException(
                    $"Gacha event period {sourcePeriod.Id} has no banners.");
            }

            result.Add(new GachaEventPeriod(
                sourcePeriod.Id,
                GachaGame.GenshinImpact,
                sourcePeriod.Version.Trim(),
                sourcePeriod.PhaseOrder,
                poolGroup,
                startsAt,
                endsAt,
                new HashSet<GameServerRegion>
                {
                    GameServerRegion.ChinaOfficial,
                    GameServerRegion.ChinaBilibili
                },
                banners,
                dataset.Source.Name.Trim(),
                dataset.Source.Revision.Trim()));
        }

        if (dataset.PeriodCount != result.Count)
        {
            throw new InvalidDataException(
                "The embedded gacha event period count does not match its manifest.");
        }
        if (dataset.SourceRecordCount != result.Sum(item => item.Banners.Count))
        {
            throw new InvalidDataException(
                "The embedded gacha banner count does not match its manifest.");
        }

        return result
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.PoolGroup)
            .ToArray();
    }

    private static void ValidateHeader(EmbeddedDataset dataset)
    {
        if (dataset.Format != "furina-gacha-events" ||
            dataset.FormatVersion != 1 ||
            dataset.Game != "genshin" ||
            dataset.RegionScope != "china" ||
            string.IsNullOrWhiteSpace(dataset.Language) ||
            string.IsNullOrWhiteSpace(dataset.Source.Name) ||
            string.IsNullOrWhiteSpace(dataset.Source.Revision))
        {
            throw new InvalidDataException(
                "The embedded gacha event dataset header is invalid or unsupported.");
        }
    }

    private static DateTimeOffset ParseTime(string value)
    {
        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset result))
        {
            throw new InvalidDataException(
                $"Invalid gacha event timestamp: {value}");
        }
        return result;
    }

    private static string? NormalizeUrl(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> NormalizeItemIds(
        IReadOnlyList<string>? values) =>
        values is null
            ? []
            : values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

    private sealed class EmbeddedDataset
    {
        [JsonPropertyName("format")]
        public string Format { get; init; } = string.Empty;

        [JsonPropertyName("format_version")]
        public int FormatVersion { get; init; }

        [JsonPropertyName("game")]
        public string Game { get; init; } = string.Empty;

        [JsonPropertyName("region_scope")]
        public string RegionScope { get; init; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; init; } = string.Empty;

        [JsonPropertyName("source")]
        public EmbeddedSource Source { get; init; } = new();

        [JsonPropertyName("source_record_count")]
        public int SourceRecordCount { get; init; }

        [JsonPropertyName("period_count")]
        public int PeriodCount { get; init; }

        [JsonPropertyName("periods")]
        public List<EmbeddedPeriod> Periods { get; init; } = [];
    }

    private sealed class EmbeddedSource
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("revision")]
        public string Revision { get; init; } = string.Empty;
    }

    private sealed class EmbeddedPeriod
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("phase_order")]
        public int PhaseOrder { get; init; }

        [JsonPropertyName("pool_group")]
        public string PoolGroup { get; init; } = string.Empty;

        [JsonPropertyName("starts_at")]
        public string StartsAt { get; init; } = string.Empty;

        [JsonPropertyName("ends_at")]
        public string EndsAt { get; init; } = string.Empty;

        [JsonPropertyName("banners")]
        public List<EmbeddedBanner> Banners { get; init; } = [];
    }

    private sealed class EmbeddedBanner
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("gacha_type")]
        public int GachaType { get; init; }

        [JsonPropertyName("image_url")]
        public string? ImageUrl { get; init; }

        [JsonPropertyName("backup_image_url")]
        public string? BackupImageUrl { get; init; }

        [JsonPropertyName("up_five_star_item_ids")]
        public List<string> UpFiveStarItemIds { get; init; } = [];

        [JsonPropertyName("up_four_star_item_ids")]
        public List<string> UpFourStarItemIds { get; init; } = [];
    }
}

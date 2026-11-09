// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed partial class YsHelperGachaEventCatalog :
    IGachaEventCatalog,
    IDisposable
{
    private static readonly IReadOnlySet<GameServerRegion> ChinaRegions =
        new HashSet<GameServerRegion>
        {
            GameServerRegion.ChinaOfficial,
            GameServerRegion.ChinaBilibili
        };

    private readonly EmbeddedGachaEventCatalog fallbackCatalog;
    private readonly IGachaItemMetadataProvider itemMetadataProvider;
    private readonly YsHelperGachaEventCatalogOptions options;
    private readonly TimeProvider timeProvider;
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly SemaphoreSlim loadGate = new(1, 1);
    private IReadOnlyList<GachaEventPeriod>? cachedPeriods;
    private DateTimeOffset nextRefreshAt;

    public YsHelperGachaEventCatalog(
        EmbeddedGachaEventCatalog fallbackCatalog,
        IGachaItemMetadataProvider itemMetadataProvider,
        YsHelperGachaEventCatalogOptions options,
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        this.fallbackCatalog = fallbackCatalog;
        this.itemMetadataProvider = itemMetadataProvider;
        this.options = options;
        this.timeProvider = timeProvider;
        this.httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        ownsHttpClient = httpClient is null;
    }

    public async ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
        GachaGame game,
        CancellationToken cancellationToken = default)
    {
        if (game != GachaGame.GenshinImpact)
        {
            return [];
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        if (cachedPeriods is not null && now < nextRefreshAt)
        {
            return cachedPeriods;
        }

        await loadGate.WaitAsync(cancellationToken);
        try
        {
            now = timeProvider.GetUtcNow();
            if (cachedPeriods is not null && now < nextRefreshAt)
            {
                return cachedPeriods;
            }

            IReadOnlyList<GachaEventPeriod> embedded =
                await fallbackCatalog.GetAllAsync(game, cancellationToken);
            cachedPeriods = await LoadBestAvailableAsync(
                embedded,
                now,
                cancellationToken);
            nextRefreshAt = now.Add(options.RefreshInterval);
            return cachedPeriods;
        }
        finally
        {
            loadGate.Release();
        }
    }

    private async Task<IReadOnlyList<GachaEventPeriod>> LoadBestAvailableAsync(
        IReadOnlyList<GachaEventPeriod> embedded,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        byte[]? staleCache = await TryReadCacheAsync(cancellationToken);
        bool cacheIsFresh = staleCache is not null &&
            File.GetLastWriteTimeUtc(options.CacheFilePath) +
                options.RefreshInterval > now.UtcDateTime;
        if (cacheIsFresh)
        {
            IReadOnlyList<GachaEventPeriod>? parsed = await TryParseAsync(
                staleCache!,
                embedded,
                cancellationToken);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        try
        {
            byte[] response = await DownloadAsync(cancellationToken);
            IReadOnlyList<GachaEventPeriod>? parsed = await TryParseAsync(
                response,
                embedded,
                cancellationToken);
            if (parsed is not null)
            {
                await WriteCacheAtomicallyAsync(response, cancellationToken);
                return parsed;
            }
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            // A stale valid cache is preferable to making history unavailable.
        }

        if (staleCache is not null)
        {
            IReadOnlyList<GachaEventPeriod>? parsed = await TryParseAsync(
                staleCache,
                embedded,
                cancellationToken);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return embedded;
    }

    private async Task<byte[]> DownloadAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.GetAsync(
            options.Endpoint,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length &&
            length > options.MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "The gacha event metadata response is too large.");
        }

        byte[] bytes = await response.Content.ReadAsByteArrayAsync(
            cancellationToken);
        if (bytes.Length == 0 || bytes.Length > options.MaximumResponseBytes)
        {
            throw new InvalidDataException(
                "The gacha event metadata response is empty or too large.");
        }
        return bytes;
    }

    private async Task<IReadOnlyList<GachaEventPeriod>?> TryParseAsync(
        byte[] bytes,
        IReadOnlyList<GachaEventPeriod> embedded,
        CancellationToken cancellationToken)
    {
        try
        {
            YsHelperResponse? response = JsonSerializer.Deserialize<YsHelperResponse>(
                bytes);
            if (response is null || response.Code != 200 ||
                (response.Characters.Count == 0 && response.Weapons.Count == 0))
            {
                return null;
            }

            string revision = Convert.ToHexStringLower(SHA256.HashData(bytes));
            return await BuildPeriodsAsync(
                response,
                embedded,
                revision,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is JsonException or
            FormatException or
            InvalidDataException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<GachaEventPeriod>> BuildPeriodsAsync(
        YsHelperResponse response,
        IReadOnlyList<GachaEventPeriod> embedded,
        string revision,
        CancellationToken cancellationToken)
    {
        var sources = new List<SourcePool>();
        foreach (IGrouping<(string Version, string Time), YsHelperEntry> group in
            response.Characters
                .Where(entry => !IsChronicled(entry.Version))
                .GroupBy(entry => (entry.Version, entry.Time)))
        {
            int index = 0;
            foreach (YsHelperEntry entry in group.Take(2))
            {
                sources.Add(new SourcePool(
                    entry,
                    GachaPoolGroup.CharacterEvent,
                    index++ == 0 ? 301 : 400));
            }
        }
        sources.AddRange(response.Weapons
            .Where(entry => !IsChronicled(entry.Version))
            .Select(entry => new SourcePool(
                entry,
                GachaPoolGroup.WeaponEvent,
                302)));

        IEnumerable<(YsHelperEntry Entry, bool IsWeapon)> chronicled =
            response.Characters
                .Where(entry => IsChronicled(entry.Version))
                .Select(entry => (entry, false))
                .Concat(response.Weapons
                    .Where(entry => IsChronicled(entry.Version))
                    .Select(entry => (entry, true)));
        foreach (IGrouping<(string Version, string Time),
                     (YsHelperEntry Entry, bool IsWeapon)> group in
            chronicled.GroupBy(value =>
                (value.Entry.Version, value.Entry.Time)))
        {
            YsHelperEntry first = group.First().Entry;
            sources.Add(new SourcePool(
                new YsHelperEntry
                {
                    Avatar = group.Select(value => value.Entry.Avatar)
                        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)),
                    Version = first.Version,
                    Time = first.Time,
                    FiveStarItems = group
                        .SelectMany(value => value.Entry.FiveStarItems)
                        .Distinct(StringComparer.Ordinal)
                        .ToList(),
                    FourStarItems = group
                        .SelectMany(value => value.Entry.FourStarItems)
                        .Distinct(StringComparer.Ordinal)
                        .ToList()
                },
                GachaPoolGroup.Chronicled,
                500));
        }

        var metadataByName = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        var periods = new List<GachaEventPeriod>(sources.Count);
        foreach (SourcePool source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaEventFeaturedItem[] featuredItems = await BuildFeaturedItemsAsync(
                source.Entry,
                response.AvatarList,
                metadataByName,
                cancellationToken);
            string version = NormalizeVersion(source.Entry.Version);
            (DateTimeOffset startsAt, DateTimeOffset endsAt) = ParseTimeRange(
                source.Entry.Time,
                source.Entry.Version);
            GachaEventPeriod? embeddedMatch = FindEmbeddedMatch(
                embedded,
                source.PoolGroup,
                version,
                startsAt,
                endsAt,
                featuredItems);
            GachaEventBanner? embeddedBanner = embeddedMatch?.Banners.Single();
            if (embeddedMatch is not null)
            {
                startsAt = embeddedMatch.StartsAt;
                endsAt = embeddedMatch.EndsAt;
            }

            string title = embeddedBanner?.Name ?? BuildFallbackName(
                source.PoolGroup,
                featuredItems);
            int gachaType = embeddedBanner?.GachaType ?? source.GachaType;
            string id = embeddedMatch?.Id ?? BuildStableId(
                source.PoolGroup,
                gachaType,
                version,
                startsAt,
                featuredItems);
            var banner = new GachaEventBanner(
                id,
                title,
                gachaType,
                NormalizeUrl(source.Entry.Avatar),
                embeddedBanner?.ImageUrl ?? embeddedBanner?.BackupImageUrl,
                featuredItems
                    .Where(item => item.RankType == 5 && item.ItemId is not null)
                    .Select(item => item.ItemId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                featuredItems
                    .Where(item => item.RankType == 4 && item.ItemId is not null)
                    .Select(item => item.ItemId!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray())
            {
                FeaturedItems = featuredItems
            };
            periods.Add(new GachaEventPeriod(
                id,
                GachaGame.GenshinImpact,
                version,
                GetPhaseOrder(source.Entry.Version),
                source.PoolGroup,
                startsAt,
                endsAt,
                ChinaRegions,
                [banner],
                "api.yshelper.com",
                revision));
        }

        return periods
            .OrderBy(period => period.StartsAt)
            .ThenBy(period => period.PoolGroup)
            .ThenBy(period => period.Banners[0].GachaType)
            .ToArray();
    }

    private async Task<GachaEventFeaturedItem[]> BuildFeaturedItemsAsync(
        YsHelperEntry entry,
        IReadOnlyDictionary<string, string> avatarList,
        IDictionary<string, GachaItemMetadata?> metadataByName,
        CancellationToken cancellationToken)
    {
        var result = new List<GachaEventFeaturedItem>();
        foreach ((string name, int rank) in entry.FiveStarItems
            .Select(name => (name, 5))
            .Concat(entry.FourStarItems.Select(name => (name, 4))))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            string normalizedName = name.Trim();
            if (!metadataByName.TryGetValue(normalizedName, out GachaItemMetadata? item))
            {
                try
                {
                    item = await itemMetadataProvider.FindByNameAsync(
                        GachaGame.GenshinImpact,
                        normalizedName,
                        cancellationToken);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException)
                {
                    item = null;
                }
                metadataByName[normalizedName] = item;
            }

            avatarList.TryGetValue(normalizedName, out string? sourceImage);
            result.Add(new GachaEventFeaturedItem(
                item?.ItemId,
                normalizedName,
                rank,
                NormalizeUrl(sourceImage) ?? item?.IconUrl));
        }
        return result
            .DistinctBy(item => (item.ItemId, item.Name, item.RankType))
            .ToArray();
    }

    private static GachaEventPeriod? FindEmbeddedMatch(
        IReadOnlyList<GachaEventPeriod> embedded,
        GachaPoolGroup poolGroup,
        string version,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        IReadOnlyList<GachaEventFeaturedItem> featuredItems)
    {
        GachaEventPeriod[] candidates = embedded
            .Where(period =>
                period.PoolGroup == poolGroup &&
                period.Version == version &&
                DateOnly.FromDateTime(period.StartsAt.Date) ==
                    DateOnly.FromDateTime(startsAt.Date) &&
                DateOnly.FromDateTime(period.EndsAt.Date) ==
                    DateOnly.FromDateTime(endsAt.Date))
            .ToArray();
        if (candidates.Length <= 1)
        {
            return candidates.SingleOrDefault();
        }

        HashSet<string> fiveStarIds = featuredItems
            .Where(item => item.RankType == 5 && item.ItemId is not null)
            .Select(item => item.ItemId!)
            .ToHashSet(StringComparer.Ordinal);
        return candidates.SingleOrDefault(period => period.Banners
            .Single()
            .UpFiveStarItemIds
            .Any(fiveStarIds.Contains));
    }

    private static (DateTimeOffset StartsAt, DateTimeOffset EndsAt)
        ParseTimeRange(string value, string phase)
    {
        string[] parts = value.Split('-', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !DateOnly.TryParseExact(
                parts[0],
                "yyyy/MM/dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly startDate) ||
            !DateOnly.TryParseExact(
                parts[1],
                "yyyy/MM/dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly endDate))
        {
            throw new FormatException("Invalid gacha event time range.");
        }

        TimeSpan offset = TimeSpan.FromHours(8);
        bool firstPhase = phase.Contains("上半", StringComparison.Ordinal);
        TimeOnly startTime = firstPhase
            ? new TimeOnly(7, 0)
            : new TimeOnly(18, 0);
        TimeOnly endTime = firstPhase
            ? new TimeOnly(17, 59, 59)
            : new TimeOnly(14, 59, 59);
        return (
            new DateTimeOffset(startDate.ToDateTime(startTime), offset),
            new DateTimeOffset(endDate.ToDateTime(endTime), offset));
    }

    private static string NormalizeVersion(string value)
    {
        Match match = VersionRegex().Match(value ?? string.Empty);
        if (!match.Success)
        {
            throw new FormatException("Gacha event version is invalid.");
        }
        return match.Value;
    }

    private static int GetPhaseOrder(string value) =>
        value.Contains("上半", StringComparison.Ordinal) ? 1 :
        value.Contains("下半", StringComparison.Ordinal) ||
            value.Contains('中', StringComparison.Ordinal) ? 2 :
        value.Contains("混池", StringComparison.Ordinal) ? 3 : 1;

    private static bool IsChronicled(string value) =>
        value.Contains("混池", StringComparison.Ordinal);

    private static string BuildFallbackName(
        GachaPoolGroup group,
        IReadOnlyList<GachaEventFeaturedItem> featuredItems)
    {
        string[] fiveStars = featuredItems
            .Where(item => item.RankType == 5)
            .Select(item => item.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
        if (fiveStars.Length > 0)
        {
            return string.Join(" / ", fiveStars);
        }
        return group switch
        {
            GachaPoolGroup.CharacterEvent => "角色活动祈愿",
            GachaPoolGroup.WeaponEvent => "武器活动祈愿",
            GachaPoolGroup.Chronicled => "集录祈愿",
            _ => "限定祈愿"
        };
    }

    private static string BuildStableId(
        GachaPoolGroup group,
        int gachaType,
        string version,
        DateTimeOffset startsAt,
        IReadOnlyList<GachaEventFeaturedItem> items)
    {
        string identity = string.Join(
            '|',
            group,
            gachaType,
            version,
            startsAt.ToString("O", CultureInfo.InvariantCulture),
            string.Join(',', items.Select(item => item.Name)));
        string hash = Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        return $"yshelper:{gachaType}:{version}:{hash}";
    }

    private async Task<byte[]?> TryReadCacheAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(options.CacheFilePath))
            {
                return null;
            }
            var info = new FileInfo(options.CacheFilePath);
            if (info.Length == 0 || info.Length > options.MaximumResponseBytes)
            {
                return null;
            }
            return await File.ReadAllBytesAsync(
                options.CacheFilePath,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException)
        {
            return null;
        }
    }

    private async Task WriteCacheAtomicallyAsync(
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(options.CacheFilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        string temporaryPath = options.CacheFilePath + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(
                temporaryPath,
                bytes,
                cancellationToken);
            File.Move(temporaryPath, options.CacheFilePath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
        }
    }

    private static string? NormalizeUrl(string? value) =>
        Uri.TryCreate(value?.Trim(), UriKind.Absolute, out Uri? uri) &&
        uri.Scheme is "http" or "https"
            ? uri.AbsoluteUri
            : null;

    public void Dispose()
    {
        loadGate.Dispose();
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

    [GeneratedRegex(@"\d+\.\d+", RegexOptions.CultureInvariant)]
    private static partial Regex VersionRegex();

    private sealed record SourcePool(
        YsHelperEntry Entry,
        GachaPoolGroup PoolGroup,
        int GachaType);

    private sealed class YsHelperResponse
    {
        [JsonPropertyName("code")]
        public int Code { get; init; }

        [JsonPropertyName("result")]
        public List<YsHelperEntry> Characters { get; init; } = [];

        [JsonPropertyName("weapon")]
        public List<YsHelperEntry> Weapons { get; init; } = [];

        [JsonPropertyName("avatar_list")]
        public Dictionary<string, string> AvatarList { get; init; } =
            new(StringComparer.Ordinal);
    }

    private sealed class YsHelperEntry
    {
        [JsonPropertyName("avatar")]
        public string? Avatar { get; init; }

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("star5_role")]
        public List<string> FiveStarItems { get; init; } = [];

        [JsonPropertyName("star4_role")]
        public List<string> FourStarItems { get; init; } = [];

        [JsonPropertyName("time")]
        public string Time { get; init; } = string.Empty;
    }
}

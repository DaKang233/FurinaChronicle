// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Services.Gacha.Analytics;

public sealed class BuildGachaAnalytics
{
    private readonly IGachaRecordRepository repository;
    private readonly IGachaItemMetadataProvider metadataProvider;
    private readonly IGachaEventCatalog eventCatalog;

    private static readonly HashSet<string> StandardCharacterIds =
    [
        "10000003", // Jean
        "10000016", // Diluc
        "10000035", // Qiqi
        "10000041", // Mona
        "10000042", // Keqing
        "10000069", // Tighnari
        "10000079", // Dehya
        "10000109"  // Yumemizuki Mizuki
    ];

    private static readonly HashSet<string> StandardWeaponIds =
    [
        "11501", // Aquila Favonia
        "11502", // Skyward Blade
        "12501", // Skyward Pride
        "12502", // Wolf's Gravestone
        "13502", // Skyward Spine
        "13505", // Primordial Jade Winged-Spear
        "14501", // Skyward Atlas
        "14502", // Lost Prayer to the Sacred Winds
        "15501", // Skyward Harp
        "15502"  // Amos' Bow
    ];

    public BuildGachaAnalytics(
        IGachaRecordRepository repository,
        IGachaItemMetadataProvider metadataProvider)
        : this(repository, metadataProvider, EmptyGachaEventCatalog.Instance)
    {
    }

    public BuildGachaAnalytics(
        IGachaRecordRepository repository,
        IGachaItemMetadataProvider metadataProvider,
        IGachaEventCatalog eventCatalog)
    {
        this.repository = repository;
        this.metadataProvider = metadataProvider;
        this.eventCatalog = eventCatalog;
    }

    public Task<GachaAnalyticsReport> ExecuteAsync(
        GachaRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            query,
            GachaAnalyticsComponents.All,
            cancellationToken);
    }

    public async Task<GachaAnalyticsReport> ExecuteAsync(
        GachaRecordQuery query,
        GachaAnalyticsComponents components,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteAsync(
            query,
            components,
            accountRegions: null,
            cancellationToken);
    }

    public async Task<GachaAnalyticsReport> ExecuteAsync(
        GachaRecordQuery query,
        GachaAnalyticsComponents components,
        IReadOnlyDictionary<Guid, GameServerRegion>? accountRegions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        GachaRecordQuery analyticsQuery = query with
        {
            Offset = 0,
            Limit = null,
            SortOrder = GachaRecordSortOrder.OldestFirst
        };
        IReadOnlyList<GachaRecord> records =
            await repository.QueryAsync(analyticsQuery, cancellationToken);
        bool needsMetadata = components.HasFlag(GachaAnalyticsComponents.Pools) ||
            components.HasFlag(GachaAnalyticsComponents.History) ||
            components.HasFlag(GachaAnalyticsComponents.Items);
        Dictionary<string, GachaItemMetadata?> metadata =
            new(StringComparer.Ordinal);
        if (needsMetadata)
        {
            (
                IReadOnlyList<GachaRecord> resolvedRecords,
                Dictionary<string, GachaItemMetadata?> resolvedMetadata) =
                await ResolveMetadataAsync(records, cancellationToken);
            records = resolvedRecords;
            metadata = resolvedMetadata;
        }

        IReadOnlyList<GachaPoolStatistics> pools =
            components.HasFlag(GachaAnalyticsComponents.Pools)
                ? records
                    .GroupBy(GachaPoolGroupResolver.Resolve)
                    .Select(group => BuildPoolStatistics(group.Key, group, metadata))
                    .OrderBy(statistics => GetPoolOrder(statistics.PoolGroup))
                    .ToArray()
                : [];
        IReadOnlyList<GachaHistoryPeriod> history = [];
        if (components.HasFlag(GachaAnalyticsComponents.History))
        {
            IReadOnlyList<GachaEventPeriod> eventPeriods =
                await eventCatalog.GetAllAsync(
                    GachaGame.GenshinImpact,
                    cancellationToken);
            history = BuildHistory(
                records,
                metadata,
                eventPeriods,
                accountRegions);
        }
        IReadOnlyList<GachaCalendarDay> calendar =
            components.HasFlag(GachaAnalyticsComponents.Calendar)
                ? BuildCalendar(records)
                : [];
        IReadOnlyList<GachaItemStatistics> items =
            components.HasFlag(GachaAnalyticsComponents.Items)
                ? BuildItems(records, metadata)
                : [];

        return new GachaAnalyticsReport(
            pools,
            history,
            calendar,
            items,
            records.Sum(GetPullCount));
    }

    private async Task<(
        IReadOnlyList<GachaRecord> Records,
        Dictionary<string, GachaItemMetadata?> Metadata)> ResolveMetadataAsync(
        IReadOnlyList<GachaRecord> records,
        CancellationToken cancellationToken)
    {
        var metadataById = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        var metadataByName = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        var resolvedRecords = new List<GachaRecord>(records.Count);

        foreach (GachaRecord record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaItemMetadata? item = null;
            if (!string.IsNullOrWhiteSpace(record.ItemId))
            {
                string itemId = record.ItemId.Trim();
                if (!metadataById.TryGetValue(itemId, out item))
                {
                    item = await metadataProvider.FindByIdAsync(
                        GachaGame.GenshinImpact,
                        itemId,
                        cancellationToken);
                    metadataById.Add(itemId, item);
                }
            }
            else if (!string.IsNullOrWhiteSpace(record.ItemName))
            {
                string itemName = record.ItemName.Trim();
                if (!metadataByName.TryGetValue(itemName, out item))
                {
                    item = await metadataProvider.FindByNameAsync(
                        GachaGame.GenshinImpact,
                        itemName,
                        cancellationToken);
                    metadataByName.Add(itemName, item);
                }
                if (item is not null)
                {
                    metadataById.TryAdd(item.ItemId, item);
                }
            }

            resolvedRecords.Add(item is null
                ? record
                : record with
                {
                    ItemId = item.ItemId,
                    ItemName = string.IsNullOrWhiteSpace(record.ItemName)
                        ? item.Name
                        : record.ItemName.Trim(),
                    ItemType = string.IsNullOrWhiteSpace(record.ItemType)
                        ? item.ItemType
                        : record.ItemType.Trim(),
                    RankType = record.RankType ?? item.RankType
                });
        }

        return (resolvedRecords, metadataById);
    }

    private static GachaPoolStatistics BuildPoolStatistics(
        GachaPoolGroup pool,
        IEnumerable<GachaRecord> source,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        GachaRecord[] records = source
            .OrderBy(record => record.Time)
            .ThenBy(record => record.ExternalRecordId, StringComparer.Ordinal)
            .ToArray();
        int total = records.Sum(GetPullCount);
        int fiveCount = records
            .Where(record => record.RankType == 5)
            .Sum(GetPullCount);
        int fourCount = records
            .Where(record => record.RankType == 4)
            .Sum(GetPullCount);
        int threeCount = records
            .Where(record => record.RankType == 3)
            .Sum(GetPullCount);
        var fiveStarPities = new List<int>();
        var limitedFiveStarPities = new List<int>();
        var fiveStarHistory = new List<FiveStarGacha>();
        var limitedFiveStarHistory = new List<LimitedFiveStarGacha>();
        int sinceFive = 0;
        int sinceFour = 0;

        foreach (IGrouping<Guid, GachaRecord> accountRecords in records
            .GroupBy(record => record.GameAccountId))
        {
            int accountSinceFive = 0;
            int accountSinceFour = 0;
            int accountSinceLimitedFive = 0;
            bool hasFiveStarBoundary = false;
            bool hasLimitedFiveStarBoundary = false;
            foreach (GachaRecord record in accountRecords
                .OrderBy(record => record.Time)
                .ThenBy(record => record.ExternalRecordId, StringComparer.Ordinal))
            {
                int count = GetPullCount(record);
                accountSinceFive += count;
                accountSinceFour += count;
                accountSinceLimitedFive += count;
                if (record.RankType == 5)
                {
                    if (hasFiveStarBoundary &&
                        IsValidFiveStarInterval(pool, accountSinceFive))
                    {
                        fiveStarPities.Add(accountSinceFive);
                    }
                    fiveStarHistory.Add(new FiveStarGacha(
                        record.GameAccountId,
                        record.ExternalRecordId,
                        GetItemName(record, metadata),
                        record.ItemId,
                        GetIconUrl(record, metadata),
                        record.Time,
                        accountSinceFive));
                    hasFiveStarBoundary = true;
                    accountSinceFive = 0;

                    if (IsLimitedFiveStar(record, pool))
                    {
                        int? completedLimitedInterval = null;
                        if (hasLimitedFiveStarBoundary &&
                            IsValidLimitedFiveStarInterval(
                                pool,
                                accountSinceLimitedFive))
                        {
                            completedLimitedInterval = accountSinceLimitedFive;
                            limitedFiveStarPities.Add(completedLimitedInterval.Value);
                        }
                        limitedFiveStarHistory.Add(new LimitedFiveStarGacha(
                            record.GameAccountId,
                            record.ExternalRecordId,
                            GetItemName(record, metadata),
                            record.ItemId,
                            GetIconUrl(record, metadata),
                            record.Time,
                            completedLimitedInterval));
                        hasLimitedFiveStarBoundary = true;
                        accountSinceLimitedFive = 0;
                    }
                }
                if (record.RankType == 4)
                {
                    accountSinceFour = 0;
                }
            }
            sinceFive += accountSinceFive;
            sinceFour += accountSinceFour;
        }

        return new GachaPoolStatistics(
            pool,
            GachaPoolGroupResolver.GetDisplayName(pool),
            total,
            records.FirstOrDefault()?.Time,
            records.LastOrDefault()?.Time,
            fiveCount,
            fourCount,
            threeCount,
            Percentage(fiveCount, total),
            Percentage(fourCount, total),
            Percentage(threeCount, total),
            fiveStarPities.Count == 0 ? null : fiveStarPities.Average(),
            limitedFiveStarPities.Count == 0
                ? null
                : limitedFiveStarPities.Average(),
            fiveStarPities.Count == 0 ? null : fiveStarPities.Min(),
            fiveStarPities.Count == 0 ? null : fiveStarPities.Max(),
            sinceFive,
            sinceFour,
            fiveStarHistory
                .OrderByDescending(item => item.Time)
                .ThenByDescending(
                    item => item.ExternalRecordId,
                    StringComparer.Ordinal)
                .ToArray(),
            limitedFiveStarHistory
                .OrderByDescending(item => item.Time)
                .ThenByDescending(
                    item => item.ExternalRecordId,
                    StringComparer.Ordinal)
                .ToArray(),
            records
                .GroupBy(GetItemKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    GachaRecord first = group.First();
                    return new GachaPoolItemCount(
                        GetItemName(first, metadata),
                        first.ItemId,
                        GetIconUrl(first, metadata),
                        first.RankType,
                        group.Sum(GetPullCount));
                })
                .OrderByDescending(item => item.RankType)
                .ThenByDescending(item => item.Count)
                .ThenBy(item => item.ItemName, StringComparer.Ordinal)
                .ToArray());
    }

    private static IReadOnlyList<GachaHistoryPeriod> BuildHistory(
        IReadOnlyList<GachaRecord> records,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata,
        IReadOnlyList<GachaEventPeriod> eventPeriods,
        IReadOnlyDictionary<Guid, GameServerRegion>? accountRegions)
    {
        GachaHistoryRecordMatch[] matches = records
            .Select(record => MatchEventPeriod(
                record,
                eventPeriods,
                accountRegions))
            .ToArray();

        IEnumerable<GachaHistoryPeriod> matched = matches
            .Where(match => match.EventPeriod is not null)
            .GroupBy(match => match.EventPeriod!.Id, StringComparer.Ordinal)
            .Select(group =>
            {
                GachaHistoryRecordMatch first = group.First();
                GachaEventPeriod period = first.EventPeriod!;
                GachaRecord[] periodRecords = group
                    .Select(match => match.Record)
                    .ToArray();
                return new GachaHistoryPeriod(
                    period,
                    group.Min(match => match.MatchQuality),
                    period.StartsAt,
                    period.EndsAt,
                    period.PoolGroup,
                    GachaPoolGroupResolver.GetDisplayName(period.PoolGroup),
                    periodRecords.Sum(GetPullCount),
                    periodRecords
                        .Select(record => record.GameAccountId)
                        .Distinct()
                        .ToArray(),
                    BuildHistoryItems(periodRecords, metadata));
            });

        IEnumerable<GachaHistoryPeriod> unmatched = matches
            .Where(match => match.EventPeriod is null)
            .GroupBy(match => new
            {
                Date = DateOnly.FromDateTime(match.Record.Time.Date),
                Pool = GachaPoolGroupResolver.Resolve(match.Record)
            })
            .Select(group =>
            {
                GachaRecord[] periodRecords = group
                    .Select(match => match.Record)
                    .ToArray();
                return new GachaHistoryPeriod(
                    null,
                    GachaEventMatchQuality.Unmatched,
                    periodRecords.Min(record => record.Time),
                    periodRecords.Max(record => record.Time),
                    group.Key.Pool,
                    GachaPoolGroupResolver.GetDisplayName(group.Key.Pool),
                    periodRecords.Sum(GetPullCount),
                    periodRecords
                        .Select(record => record.GameAccountId)
                        .Distinct()
                        .ToArray(),
                    BuildHistoryItems(periodRecords, metadata));
            });

        return matched
            .Concat(unmatched)
            .OrderByDescending(period => period.StartTime)
            .ThenBy(period => GetPoolOrder(period.PoolGroup))
            .ToArray();
    }

    private static IReadOnlyList<GachaHistoryItem> BuildHistoryItems(
        IReadOnlyList<GachaRecord> records,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        return records
            .GroupBy(GetItemKey)
            .Select(items =>
            {
                GachaRecord first = items.First();
                return new GachaHistoryItem(
                    GetItemName(first, metadata),
                    first.ItemId,
                    GetIconUrl(first, metadata),
                    first.RankType,
                    items.Sum(GetPullCount));
            })
            .OrderByDescending(item => item.RankType)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.ItemName, StringComparer.Ordinal)
            .ToArray();
    }

    private static GachaHistoryRecordMatch MatchEventPeriod(
        GachaRecord record,
        IReadOnlyList<GachaEventPeriod> eventPeriods,
        IReadOnlyDictionary<Guid, GameServerRegion>? accountRegions)
    {
        GameServerRegion region = accountRegions is not null &&
            accountRegions.TryGetValue(record.GameAccountId, out GameServerRegion value)
                ? value
                : GameServerRegion.Unknown;
        GachaPoolGroup poolGroup = GachaPoolGroupResolver.Resolve(record);
        GachaEventPeriod? eventPeriod = eventPeriods
            .Where(period =>
                period.PoolGroup == poolGroup &&
                period.Contains(record.Time) &&
                period.Supports(region))
            .OrderBy(period => period.StartsAt)
            .FirstOrDefault();

        return new GachaHistoryRecordMatch(
            record,
            eventPeriod,
            eventPeriod is null
                ? GachaEventMatchQuality.Unmatched
                : region == GameServerRegion.Unknown
                    ? GachaEventMatchQuality.RegionUnverified
                    : GachaEventMatchQuality.Verified);
    }

    private static bool IsLimitedFiveStar(
        GachaRecord record,
        GachaPoolGroup pool)
    {
        if (record.RankType != 5 ||
            string.IsNullOrWhiteSpace(record.ItemId))
        {
            return false;
        }

        string itemId = record.ItemId.Trim();
        return pool switch
        {
            GachaPoolGroup.CharacterEvent =>
                !StandardCharacterIds.Contains(itemId),
            GachaPoolGroup.WeaponEvent =>
                !StandardWeaponIds.Contains(itemId),
            _ => false
        };
    }

    private static bool IsValidFiveStarInterval(
        GachaPoolGroup pool,
        int pulls)
    {
        int maximum = pool == GachaPoolGroup.WeaponEvent ? 80 : 90;
        return pulls is > 0 && pulls <= maximum;
    }

    private static bool IsValidLimitedFiveStarInterval(
        GachaPoolGroup pool,
        int pulls)
    {
        int maximum = pool == GachaPoolGroup.WeaponEvent ? 160 : 180;
        return pulls is > 0 && pulls <= maximum;
    }

    private static IReadOnlyList<GachaCalendarDay> BuildCalendar(
        IReadOnlyList<GachaRecord> records)
    {
        var grouped = records
            .GroupBy(record => DateOnly.FromDateTime(record.Time.Date))
            .Select(group => new
            {
                Date = group.Key,
                Total = group.Sum(GetPullCount),
                Five = group
                    .Where(record => record.RankType == 5)
                    .Sum(GetPullCount),
                Four = group
                    .Where(record => record.RankType == 4)
                    .Sum(GetPullCount)
            })
            .ToArray();
        int maximum = grouped.Length == 0 ? 0 : grouped.Max(day => day.Total);

        return grouped
            .Select(day => new GachaCalendarDay(
                day.Date,
                day.Total,
                day.Five,
                day.Four,
                GetIntensity(day.Total, maximum)))
            .OrderBy(day => day.Date)
            .ToArray();
    }

    private static IReadOnlyList<GachaItemStatistics> BuildItems(
        IReadOnlyList<GachaRecord> records,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        return records
            .GroupBy(GetItemKey, StringComparer.Ordinal)
            .Select(group =>
            {
                GachaRecord first = group.First();
                return new GachaItemStatistics(
                    group.Key,
                    GetItemName(first, metadata),
                    first.ItemId,
                    first.ItemType,
                    GetIconUrl(first, metadata),
                    first.RankType,
                    group.Sum(GetPullCount),
                    group
                        .OrderByDescending(record => record.Time)
                        .Select(record => record.Time)
                        .ToArray());
            })
            .OrderByDescending(item => item.RankType)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.ItemName, StringComparer.Ordinal)
            .ToArray();
    }

    private static int GetPullCount(GachaRecord record)
    {
        return Math.Max(1, record.Count);
    }

    private static string GetItemKey(GachaRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ItemId))
        {
            return $"id:{record.ItemId.Trim()}";
        }
        return $"name:{record.ItemName?.Trim() ?? "未知物品"}|{record.RankType}";
    }

    private static string GetItemName(
        GachaRecord record,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        if (!string.IsNullOrWhiteSpace(record.ItemName))
        {
            return record.ItemName.Trim();
        }
        if (record.ItemId is string itemId &&
            metadata.TryGetValue(
                itemId.Trim(),
                out GachaItemMetadata? item) &&
            item is not null)
        {
            return item.Name;
        }
        return $"物品 {record.ItemId ?? "未知"}";
    }

    private static string? GetIconUrl(
        GachaRecord record,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        return record.ItemId is string itemId &&
            metadata.TryGetValue(
                itemId.Trim(),
                out GachaItemMetadata? item)
            ? item?.IconUrl
            : null;
    }

    private static double Percentage(int count, int total)
    {
        return total == 0 ? 0D : count * 100D / total;
    }

    private static int GetIntensity(int count, int maximum)
    {
        if (count <= 0 || maximum <= 0)
        {
            return 0;
        }
        return Math.Clamp(
            (int)Math.Ceiling(count * 5D / maximum),
            1,
            5);
    }

    private static int GetPoolOrder(GachaPoolGroup group)
    {
        return group switch
        {
            GachaPoolGroup.CharacterEvent => 0,
            GachaPoolGroup.WeaponEvent => 1,
            GachaPoolGroup.Chronicled => 2,
            GachaPoolGroup.Standard => 3,
            GachaPoolGroup.Novice => 4,
            _ => 5
        };
    }

    private sealed record GachaHistoryRecordMatch(
        GachaRecord Record,
        GachaEventPeriod? EventPeriod,
        GachaEventMatchQuality MatchQuality);

    private sealed class EmptyGachaEventCatalog : IGachaEventCatalog
    {
        public static EmptyGachaEventCatalog Instance { get; } = new();

        public ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
            GachaGame game,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<IReadOnlyList<GachaEventPeriod>>([]);
        }
    }
}

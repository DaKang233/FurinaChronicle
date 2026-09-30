// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Services.Wishes.Analytics;

public sealed class BuildWishAnalytics(
    IWishRecordRepository repository,
    IGachaItemMetadataProvider metadataProvider)
{
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

    public Task<WishAnalyticsReport> ExecuteAsync(
        WishRecordQuery query,
        CancellationToken cancellationToken = default)
    {
        return ExecuteAsync(
            query,
            WishAnalyticsComponents.All,
            cancellationToken);
    }

    public async Task<WishAnalyticsReport> ExecuteAsync(
        WishRecordQuery query,
        WishAnalyticsComponents components,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        query.Validate();
        WishRecordQuery analyticsQuery = query with
        {
            Offset = 0,
            Limit = null,
            SortOrder = WishRecordSortOrder.OldestFirst
        };
        IReadOnlyList<WishRecord> records =
            await repository.QueryAsync(analyticsQuery, cancellationToken);
        bool needsMetadata = components.HasFlag(WishAnalyticsComponents.Pools) ||
            components.HasFlag(WishAnalyticsComponents.History) ||
            components.HasFlag(WishAnalyticsComponents.Items);
        Dictionary<string, GachaItemMetadata?> metadata =
            new(StringComparer.Ordinal);
        if (needsMetadata)
        {
            (
                IReadOnlyList<WishRecord> resolvedRecords,
                Dictionary<string, GachaItemMetadata?> resolvedMetadata) =
                await ResolveMetadataAsync(records, cancellationToken);
            records = resolvedRecords;
            metadata = resolvedMetadata;
        }

        IReadOnlyList<WishPoolStatistics> pools =
            components.HasFlag(WishAnalyticsComponents.Pools)
                ? records
                    .GroupBy(WishPoolGroupResolver.Resolve)
                    .Select(group => BuildPoolStatistics(group.Key, group, metadata))
                    .OrderBy(statistics => GetPoolOrder(statistics.PoolGroup))
                    .ToArray()
                : [];
        IReadOnlyList<WishHistoryPeriod> history =
            components.HasFlag(WishAnalyticsComponents.History)
                ? BuildHistory(records, metadata)
                : [];
        IReadOnlyList<WishCalendarDay> calendar =
            components.HasFlag(WishAnalyticsComponents.Calendar)
                ? BuildCalendar(records)
                : [];
        IReadOnlyList<WishItemStatistics> items =
            components.HasFlag(WishAnalyticsComponents.Items)
                ? BuildItems(records, metadata)
                : [];

        return new WishAnalyticsReport(
            pools,
            history,
            calendar,
            items,
            records.Sum(GetPullCount));
    }

    private async Task<(
        IReadOnlyList<WishRecord> Records,
        Dictionary<string, GachaItemMetadata?> Metadata)> ResolveMetadataAsync(
        IReadOnlyList<WishRecord> records,
        CancellationToken cancellationToken)
    {
        var metadataById = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        var metadataByName = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        var resolvedRecords = new List<WishRecord>(records.Count);

        foreach (WishRecord record in records)
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

    private static WishPoolStatistics BuildPoolStatistics(
        WishPoolGroup pool,
        IEnumerable<WishRecord> source,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        WishRecord[] records = source
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
        var fiveStarHistory = new List<FiveStarWish>();
        int sinceFive = 0;
        int sinceFour = 0;

        foreach (IGrouping<Guid, WishRecord> accountRecords in records
            .GroupBy(record => record.GameAccountId))
        {
            int accountSinceFive = 0;
            int accountSinceFour = 0;
            int accountSinceLimitedFive = 0;
            bool hasFiveStarBoundary = false;
            bool hasLimitedFiveStarBoundary = false;
            foreach (WishRecord record in accountRecords
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
                    fiveStarHistory.Add(new FiveStarWish(
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
                        if (hasLimitedFiveStarBoundary &&
                            IsValidLimitedFiveStarInterval(
                                pool,
                                accountSinceLimitedFive))
                        {
                            limitedFiveStarPities.Add(
                                accountSinceLimitedFive);
                        }
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

        return new WishPoolStatistics(
            pool,
            WishPoolGroupResolver.GetDisplayName(pool),
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
            records
                .GroupBy(GetItemKey, StringComparer.Ordinal)
                .Select(group =>
                {
                    WishRecord first = group.First();
                    return new WishPoolItemCount(
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

    private static IReadOnlyList<WishHistoryPeriod> BuildHistory(
        IReadOnlyList<WishRecord> records,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        return records
            .GroupBy(record => new
            {
                Date = DateOnly.FromDateTime(record.Time.Date),
                Pool = WishPoolGroupResolver.Resolve(record)
            })
            .Select(group => new WishHistoryPeriod(
                group.Key.Date,
                group.Key.Pool,
                WishPoolGroupResolver.GetDisplayName(group.Key.Pool),
                group.Sum(GetPullCount),
                group.Select(record => record.GameAccountId).Distinct().ToArray(),
                group
                    .GroupBy(GetItemKey)
                    .Select(items => new WishHistoryItem(
                        GetItemName(items.First(), metadata),
                        items.First().RankType,
                        items.Sum(GetPullCount)))
                    .OrderByDescending(item => item.RankType)
                    .ThenByDescending(item => item.Count)
                    .ThenBy(item => item.ItemName, StringComparer.Ordinal)
                    .ToArray()))
            .OrderByDescending(period => period.Date)
            .ThenBy(period => GetPoolOrder(period.PoolGroup))
            .ToArray();
    }

    private static bool IsLimitedFiveStar(
        WishRecord record,
        WishPoolGroup pool)
    {
        if (record.RankType != 5 ||
            string.IsNullOrWhiteSpace(record.ItemId))
        {
            return false;
        }

        string itemId = record.ItemId.Trim();
        return pool switch
        {
            WishPoolGroup.CharacterEvent =>
                !StandardCharacterIds.Contains(itemId),
            WishPoolGroup.WeaponEvent =>
                !StandardWeaponIds.Contains(itemId),
            _ => false
        };
    }

    private static bool IsValidFiveStarInterval(
        WishPoolGroup pool,
        int pulls)
    {
        int maximum = pool == WishPoolGroup.WeaponEvent ? 80 : 90;
        return pulls is > 0 && pulls <= maximum;
    }

    private static bool IsValidLimitedFiveStarInterval(
        WishPoolGroup pool,
        int pulls)
    {
        int maximum = pool == WishPoolGroup.WeaponEvent ? 160 : 180;
        return pulls is > 0 && pulls <= maximum;
    }

    private static IReadOnlyList<WishCalendarDay> BuildCalendar(
        IReadOnlyList<WishRecord> records)
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
            .Select(day => new WishCalendarDay(
                day.Date,
                day.Total,
                day.Five,
                day.Four,
                GetIntensity(day.Total, maximum)))
            .OrderBy(day => day.Date)
            .ToArray();
    }

    private static IReadOnlyList<WishItemStatistics> BuildItems(
        IReadOnlyList<WishRecord> records,
        IReadOnlyDictionary<string, GachaItemMetadata?> metadata)
    {
        return records
            .GroupBy(GetItemKey, StringComparer.Ordinal)
            .Select(group =>
            {
                WishRecord first = group.First();
                return new WishItemStatistics(
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

    private static int GetPullCount(WishRecord record)
    {
        return Math.Max(1, record.Count);
    }

    private static string GetItemKey(WishRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.ItemId))
        {
            return $"id:{record.ItemId.Trim()}";
        }
        return $"name:{record.ItemName?.Trim() ?? "未知物品"}|{record.RankType}";
    }

    private static string GetItemName(
        WishRecord record,
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
        WishRecord record,
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

    private static int GetPoolOrder(WishPoolGroup group)
    {
        return group switch
        {
            WishPoolGroup.CharacterEvent => 0,
            WishPoolGroup.WeaponEvent => 1,
            WishPoolGroup.Chronicled => 2,
            WishPoolGroup.Standard => 3,
            WishPoolGroup.Novice => 4,
            _ => 5
        };
    }
}

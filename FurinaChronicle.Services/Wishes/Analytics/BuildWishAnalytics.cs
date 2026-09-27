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
    public async Task<WishAnalyticsReport> ExecuteAsync(
        WishRecordQuery query,
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
        Dictionary<string, GachaItemMetadata?> metadata =
            await LoadMetadataAsync(records, cancellationToken);

        IReadOnlyList<WishPoolStatistics> pools = records
            .GroupBy(WishPoolGroupResolver.Resolve)
            .Select(group => BuildPoolStatistics(group.Key, group, metadata))
            .OrderBy(statistics => GetPoolOrder(statistics.PoolGroup))
            .ToArray();
        IReadOnlyList<WishHistoryPeriod> history = records
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
        IReadOnlyList<WishCalendarDay> calendar =
            BuildCalendar(records);
        IReadOnlyList<WishItemStatistics> items =
            BuildItems(records, metadata);

        return new WishAnalyticsReport(
            pools,
            history,
            calendar,
            items,
            records.Sum(GetPullCount));
    }

    private async Task<Dictionary<string, GachaItemMetadata?>> LoadMetadataAsync(
        IReadOnlyList<WishRecord> records,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, GachaItemMetadata?>(
            StringComparer.Ordinal);
        foreach (string itemId in records
            .Select(record => record.ItemId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result[itemId] = await metadataProvider.FindByIdAsync(
                GachaGame.GenshinImpact,
                itemId,
                cancellationToken);
        }
        return result;
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
        var fiveStarHistory = new List<FiveStarWish>();
        int sinceFive = 0;
        int sinceFour = 0;

        foreach (IGrouping<Guid, WishRecord> accountRecords in records
            .GroupBy(record => record.GameAccountId))
        {
            int accountSinceFive = 0;
            int accountSinceFour = 0;
            foreach (WishRecord record in accountRecords
                .OrderBy(record => record.Time)
                .ThenBy(record => record.ExternalRecordId, StringComparer.Ordinal))
            {
                int count = GetPullCount(record);
                accountSinceFive += count;
                accountSinceFour += count;
                if (record.RankType == 5)
                {
                    fiveStarPities.Add(accountSinceFive);
                    fiveStarHistory.Add(new FiveStarWish(
                        record.GameAccountId,
                        GetItemName(record, metadata),
                        record.ItemId,
                        GetIconUrl(record, metadata),
                        record.Time,
                        accountSinceFive));
                    accountSinceFive = 0;
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
            AverageUpFiveStarPulls: null,
            fiveStarPities.Count == 0 ? null : fiveStarPities.Min(),
            fiveStarPities.Count == 0 ? null : fiveStarPities.Max(),
            sinceFive,
            sinceFour,
            fiveStarHistory
                .OrderByDescending(item => item.Time)
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
            metadata.TryGetValue(itemId, out GachaItemMetadata? item) &&
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
            metadata.TryGetValue(itemId, out GachaItemMetadata? item)
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

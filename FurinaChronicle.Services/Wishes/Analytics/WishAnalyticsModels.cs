using FurinaChronicle.Core.Wishes;

namespace FurinaChronicle.Services.Wishes.Analytics;

[Flags]
public enum WishAnalyticsComponents
{
    None = 0,
    Pools = 1 << 0,
    History = 1 << 1,
    Calendar = 1 << 2,
    Items = 1 << 3,
    All = Pools | History | Calendar | Items
}

public sealed record WishAnalyticsReport(
    IReadOnlyList<WishPoolStatistics> Pools,
    IReadOnlyList<WishHistoryPeriod> History,
    IReadOnlyList<WishCalendarDay> Calendar,
    IReadOnlyList<WishItemStatistics> Items,
    int TotalPulls);

public sealed record WishPoolStatistics(
    WishPoolGroup PoolGroup,
    string PoolName,
    int TotalPulls,
    DateTimeOffset? StartTime,
    DateTimeOffset? EndTime,
    int FiveStarCount,
    int FourStarCount,
    int ThreeStarCount,
    double FiveStarPercentage,
    double FourStarPercentage,
    double ThreeStarPercentage,
    double? AverageFiveStarPulls,
    double? AverageUpFiveStarPulls,
    int? MinimumFiveStarPulls,
    int? MaximumFiveStarPulls,
    int PullsSinceLastFiveStar,
    int PullsSinceLastFourStar,
    IReadOnlyList<FiveStarWish> FiveStarHistory,
    IReadOnlyList<WishPoolItemCount> ItemCounts);

public sealed record FiveStarWish(
    Guid GameAccountId,
    string ExternalRecordId,
    string ItemName,
    string? ItemId,
    string? IconUrl,
    DateTimeOffset Time,
    int Pulls);

public sealed record WishPoolItemCount(
    string ItemName,
    string? ItemId,
    string? IconUrl,
    int? RankType,
    int Count);

public sealed record WishHistoryPeriod(
    DateOnly Date,
    WishPoolGroup PoolGroup,
    string PoolName,
    int TotalPulls,
    IReadOnlyList<Guid> GameAccountIds,
    IReadOnlyList<WishHistoryItem> Items);

public sealed record WishHistoryItem(
    string ItemName,
    int? RankType,
    int Count);

public sealed record WishCalendarDay(
    DateOnly Date,
    int TotalPulls,
    int FiveStarCount,
    int FourStarCount,
    int IntensityLevel);

public sealed record WishItemStatistics(
    string Key,
    string ItemName,
    string? ItemId,
    string? ItemType,
    string? IconUrl,
    int? RankType,
    int Count,
    IReadOnlyList<DateTimeOffset> AcquisitionTimes);

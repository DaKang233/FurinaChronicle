// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;

namespace FurinaChronicle.Services.Gacha.Analytics;

[Flags]
public enum GachaAnalyticsComponents
{
    None = 0,
    Pools = 1 << 0,
    History = 1 << 1,
    Calendar = 1 << 2,
    Items = 1 << 3,
    All = Pools | History | Calendar | Items
}

public sealed record GachaAnalyticsReport(
    IReadOnlyList<GachaPoolStatistics> Pools,
    IReadOnlyList<GachaHistoryPeriod> History,
    IReadOnlyList<GachaCalendarDay> Calendar,
    IReadOnlyList<GachaItemStatistics> Items,
    int TotalPulls);

public sealed record GachaPoolStatistics(
    GachaPoolGroup PoolGroup,
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
    IReadOnlyList<FiveStarGacha> FiveStarHistory,
    IReadOnlyList<LimitedFiveStarGacha> LimitedFiveStarHistory,
    IReadOnlyList<GachaPoolItemCount> ItemCounts);

public sealed record FiveStarGacha(
    Guid GameAccountId,
    string ExternalRecordId,
    string ItemName,
    string? ItemId,
    string? IconUrl,
    DateTimeOffset Time,
    int Pulls);

public sealed record LimitedFiveStarGacha(
    Guid GameAccountId,
    string ExternalRecordId,
    string ItemName,
    string? ItemId,
    string? IconUrl,
    DateTimeOffset Time,
    int? PullsSincePreviousLimitedFiveStar);

public sealed record GachaPoolItemCount(
    string ItemName,
    string? ItemId,
    string? IconUrl,
    int? RankType,
    int Count);

public enum GachaEventMatchQuality
{
    Unmatched,
    RegionUnverified,
    Verified
}

public sealed record GachaHistoryPeriod(
    GachaEventPeriod? EventPeriod,
    GachaEventMatchQuality MatchQuality,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    GachaPoolGroup PoolGroup,
    string PoolName,
    int TotalPulls,
    IReadOnlyList<Guid> GameAccountIds,
    IReadOnlyList<GachaHistoryItem> Items);

public sealed record GachaHistoryItem(
    string ItemName,
    string? ItemId,
    string? IconUrl,
    int? RankType,
    int Count);

public sealed record GachaCalendarDay(
    DateOnly Date,
    int TotalPulls,
    int FiveStarCount,
    int FourStarCount,
    int IntensityLevel);

public sealed record GachaItemStatistics(
    string Key,
    string ItemName,
    string? ItemId,
    string? ItemType,
    string? IconUrl,
    int? RankType,
    int Count,
    IReadOnlyList<DateTimeOffset> AcquisitionTimes);

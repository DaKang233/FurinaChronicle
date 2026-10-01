// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.App.ViewModels;

public sealed partial class GachaAccountFilterItem(GameAccount account)
    : ObservableObject
{
    public GameAccount Account { get; } = account;

    public string DisplayName => Account.DisplayName ?? Account.Uid;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = true;
}

public sealed record PoolFilterOption(
    GachaPoolGroup? Value,
    string Name);

public sealed record RankFilterOption(
    int? Value,
    string Name);

public sealed record GachaRecordAnalysisDisplayItem(
    string Account,
    string Name,
    string Rank,
    string Time,
    string Pool,
    string ItemType)
{
    public static GachaRecordAnalysisDisplayItem FromDomain(
        GachaRecord record,
        IReadOnlyDictionary<Guid, GameAccount> accounts)
    {
        GachaRecordDisplayItem item = GachaRecordDisplayItem.FromDomain(record);
        string account = accounts.TryGetValue(record.GameAccountId, out GameAccount? value)
            ? value.DisplayName ?? value.Uid
            : record.GameAccountId.ToString("D");
        return new(
            account,
            item.Name,
            item.Rank,
            item.Time,
            item.Pool,
            item.ItemType);
    }
}

public sealed record FiveStarGachaDisplayItem(
    string ItemName,
    ImageSource? IconUrl,
    string Pulls,
    string Time,
    string Account);

public sealed record GachaPoolItemCountDisplayItem(
    string ItemName,
    ImageSource? IconUrl,
    int? RankType,
    string Rank,
    string Count);

public sealed partial class GachaPoolStatisticsDisplayItem(
    string PoolName,
    string Total,
    string Period,
    string AverageFiveStar,
    string AverageUp,
    string Extremes,
    string RankDistribution,
    string CurrentPity,
    IReadOnlyList<FiveStarGachaDisplayItem> FiveStarHistory,
    IReadOnlyList<GachaPoolItemCountDisplayItem> ItemCounts)
    : ObservableObject
{
    public string PoolName { get; } = PoolName;

    public string Total { get; } = Total;

    public string Period { get; } = Period;

    public string AverageFiveStar { get; } = AverageFiveStar;

    public string AverageUp { get; } = AverageUp;

    public string Extremes { get; } = Extremes;

    public string RankDistribution { get; } = RankDistribution;

    public string CurrentPity { get; } = CurrentPity;

    public IReadOnlyList<FiveStarGachaDisplayItem> FiveStarHistory { get; } =
        FiveStarHistory;

    public IReadOnlyList<GachaPoolItemCountDisplayItem> ItemCounts { get; } =
        ItemCounts;

    public IReadOnlyList<GachaPoolItemCountDisplayItem> FiveStarItemCounts { get; } =
        ItemCounts.Where(item => item.RankType == 5).ToArray();

    public IReadOnlyList<GachaPoolItemCountDisplayItem> VisibleItemCounts =>
        IsStatisticsExpanded ? ItemCounts : FiveStarItemCounts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatisticsToggleText))]
    [NotifyPropertyChangedFor(nameof(VisibleItemCounts))]
    public partial bool IsStatisticsExpanded { get; set; } = true;

    public string StatisticsToggleText =>
        IsStatisticsExpanded ? "收起统计" : "展开统计";

    [RelayCommand]
    private void ToggleStatistics() =>
        IsStatisticsExpanded = !IsStatisticsExpanded;
}

public sealed record GachaHistoryBannerDisplayItem(
    string Name,
    string Type,
    ImageSource? BannerImage,
    string FeaturedItems);

public sealed record GachaHistoryDisplayItem(
    string Title,
    string Period,
    string Pool,
    string Total,
    string Accounts,
    string Items,
    string MetadataNote,
    IReadOnlyList<GachaHistoryBannerDisplayItem> Banners);

public sealed record GachaCalendarDisplayItem(
    string Date,
    string Total,
    string HighRanks,
    Color Color);

public sealed partial class GachaItemStatisticsDisplayItem(
    GachaItemStatistics source,
    string? cachedIconPath)
    : ObservableObject
{
    public string ItemName => source.ItemName;

    public ImageSource? IconUrl { get; } = string.IsNullOrWhiteSpace(cachedIconPath)
        ? null
        : ImageSource.FromFile(cachedIconPath);

    public int? RankType => source.RankType;

    public string Rank => source.RankType is int rank ? $"{rank} 星" : "未知";

    public string Count => $"× {source.Count}";

    public bool CanShowTimes => source.RankType is 4 or 5;

    public bool CanExpandTimes => source.RankType is 4 or 5 && source.Count > 10;

    public bool IsTimesVisible => source.RankType == 5 ||
        source.RankType == 4 && (source.Count > 10 ? IsExpanded : true);

    public string Times => string.Join(
        Environment.NewLine,
        source.AcquisitionTimes.Select(
            time => time.ToString("yyyy-MM-dd HH:mm:ss")));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimesVisible))]
    public partial bool IsExpanded { get; set; }
}

public sealed record GachaItemGridDisplayRow(
    string? GroupName,
    IReadOnlyList<GachaItemStatisticsDisplayItem> Items)
{
    public bool IsHeader => GroupName is not null;

    public bool IsItemRow => GroupName is null;
}

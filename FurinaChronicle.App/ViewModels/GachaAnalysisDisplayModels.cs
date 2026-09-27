using CommunityToolkit.Mvvm.ComponentModel;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Wishes.Analytics;

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
    WishPoolGroup? Value,
    string Name);

public sealed record RankFilterOption(
    int? Value,
    string Name);

public sealed record WishRecordAnalysisDisplayItem(
    string Account,
    string Name,
    string Rank,
    string Time,
    string Pool,
    string ItemType)
{
    public static WishRecordAnalysisDisplayItem FromDomain(
        WishRecord record,
        IReadOnlyDictionary<Guid, GameAccount> accounts)
    {
        WishRecordDisplayItem item = WishRecordDisplayItem.FromDomain(record);
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

public sealed record FiveStarWishDisplayItem(
    string ItemName,
    ImageSource? IconUrl,
    string Pulls,
    string Time,
    string Account);

public sealed record WishPoolItemCountDisplayItem(
    string ItemName,
    ImageSource? IconUrl,
    string Rank,
    string Count);

public sealed record WishPoolStatisticsDisplayItem(
    string PoolName,
    string Total,
    string Period,
    string AverageFiveStar,
    string AverageUp,
    string Extremes,
    string RankDistribution,
    string CurrentPity,
    IReadOnlyList<FiveStarWishDisplayItem> FiveStarHistory,
    IReadOnlyList<WishPoolItemCountDisplayItem> ItemCounts);

public sealed record WishHistoryDisplayItem(
    string Date,
    string Pool,
    string Total,
    string Accounts,
    string Items);

public sealed record WishCalendarDisplayItem(
    string Date,
    string Total,
    string HighRanks,
    Color Color);

public sealed partial class WishItemStatisticsDisplayItem(
    WishItemStatistics source,
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

    public bool CanExpandTimes => source.RankType == 4;

    public bool IsTimesVisible => source.RankType == 5 ||
        source.RankType == 4 && IsExpanded;

    public string Times => string.Join(
        Environment.NewLine,
        source.AcquisitionTimes.Select(
            time => time.ToString("yyyy-MM-dd HH:mm:ss")));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTimesVisible))]
    public partial bool IsExpanded { get; set; }
}

public sealed record WishItemRankGroupDisplayItem(
    string Name,
    IReadOnlyList<WishItemStatisticsDisplayItem> Items);

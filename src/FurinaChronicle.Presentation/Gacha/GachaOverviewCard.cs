// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Presentation.Gacha;

// Pure presentation state. The analytics service owns ordering, pity intervals and UP classification.
public sealed class GachaOverviewCard : INotifyPropertyChanged
{
    private const int AccountBatchSize = 48;
    private const int ArchiveBatchSize = 12;
    private readonly IReadOnlyList<GachaReadOnlyRow> normalHistory;
    private readonly IReadOnlyList<GachaReadOnlyRow> limitedHistory;
    private readonly int batchSize;
    private int visibleCount;
    private bool truePulls;
    private bool listMode;

    public GachaOverviewCard(
        GachaPoolStatistics statistics,
        bool archiveMode,
        Func<string?, string?> cachedIconPath)
    {
        ToggleTruePullsCommand = new GachaActionCommand(ToggleTruePulls);
        ToggleLayoutCommand = new GachaActionCommand(ToggleLayout);
        LoadMoreCommand = new GachaActionCommand(LoadMore);
        Title = statistics.PoolName;
        IsArchiveMode = archiveMode;
        CanShowTruePulls = !archiveMode && statistics.PoolGroup == GachaPoolGroup.CharacterEvent;
        Statistics = $"总抽数 {statistics.TotalPulls}\n" +
            $"五星平均 {Average(statistics.AverageFiveStarPulls)} · 限定五星平均 {Average(statistics.AverageUpFiveStarPulls)}\n" +
            $"五星间隔 {statistics.MinimumFiveStarPulls?.ToString() ?? "未知"} ～ {statistics.MaximumFiveStarPulls?.ToString() ?? "未知"}\n" +
            $"五星 {statistics.FiveStarCount}（{statistics.FiveStarPercentage:0.0}%） · " +
            $"四星 {statistics.FourStarCount}（{statistics.FourStarPercentage:0.0}%） · " +
            $"三星 {statistics.ThreeStarCount}（{statistics.ThreeStarPercentage:0.0}%）\n" +
            (archiveMode ? string.Empty :
                $"距上个五星 {statistics.PullsSinceLastFiveStar?.ToString() ?? "未知"} 抽 · " +
                $"四星 {statistics.PullsSinceLastFourStar?.ToString() ?? "未知"} 抽\n") +
            $"{Date(statistics.StartTime)} ～ {Date(statistics.EndTime)}";
        batchSize = archiveMode ? ArchiveBatchSize : AccountBatchSize;
        visibleCount = batchSize;
        int ordinaryScale = statistics.PoolGroup == GachaPoolGroup.WeaponEvent ? 80 : 90;
        normalHistory = archiveMode
            ? statistics.ItemCounts.Where(item => item.RankType == 5).Select(item =>
                Row(item.ItemName, item.ItemId, item.IconUrl, "", item.Count.ToString(), cachedIconPath)).ToArray()
            : statistics.FiveStarHistory.Select(item =>
                Row(item.ItemName, item.ItemId, item.IconUrl, Pulls(item.Pulls), null, cachedIconPath)
                    with { DetailText = item.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"), IntervalPulls = item.Pulls, IntervalMaximum = ordinaryScale }).ToArray();
        limitedHistory = !CanShowTruePulls ? [] : statistics.LimitedFiveStarHistory.Select(item =>
            Row(item.ItemName, item.ItemId, item.IconUrl, Pulls(item.PullsSincePreviousLimitedFiveStar), null, cachedIconPath)
                with { DetailText = item.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"), IntervalPulls = item.PullsSincePreviousLimitedFiveStar, IntervalMaximum = 180 }).ToArray();
    }

    public string Title { get; }
    public string Statistics { get; }
    public bool IsArchiveMode { get; }
    public bool CanShowTruePulls { get; }
    public bool CanChangeLayout => !IsArchiveMode;
    public bool IsListMode => listMode;
    public bool IsGridMode => !listMode;
    public string LayoutToggleText => listMode ? "五星图标" : "五星列表";
    public string TruePullsToggleText => truePulls ? "显示普通抽数" : "显示真实抽数";
    public IReadOnlyList<GachaReadOnlyRow> VisibleItems => History.Take(visibleCount).ToArray();
    public bool CanLoadMore => visibleCount < History.Count;
    public string LoadMoreText => $"加载更多（{Math.Min(visibleCount, History.Count)} / {History.Count}）";
    public string HistoryLabel => IsArchiveMode ? "五星物品数量" : truePulls ? "限定五星历史" : "五星历史";

    private IReadOnlyList<GachaReadOnlyRow> History => truePulls ? limitedHistory : normalHistory;

    public System.Windows.Input.ICommand ToggleTruePullsCommand { get; }
    public System.Windows.Input.ICommand ToggleLayoutCommand { get; }
    public System.Windows.Input.ICommand LoadMoreCommand { get; }
    private double viewportHeight = double.NaN;
    public double ViewportHeight => viewportHeight;
    public void SetViewportHeight(double height)
    {
        if (!double.IsFinite(height) || height <= 0 || height == viewportHeight) return;
        viewportHeight = height;
        PropertyChanged?.Invoke(this, new(nameof(ViewportHeight)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleTruePulls()
    {
        if (!CanShowTruePulls) return;
        truePulls = !truePulls;
        visibleCount = batchSize;
        Refresh();
    }

    public void ToggleLayout()
    {
        if (!CanChangeLayout) return;
        listMode = !listMode;
        Refresh();
    }

    public void LoadMore()
    {
        visibleCount = Math.Min(History.Count, visibleCount + batchSize);
        Refresh();
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

    private static GachaReadOnlyRow Row(
        string name, string? id, string? url, string pulls, string? count, Func<string?, string?> cachedPath) =>
        new("", name, "5 星", "", "", "", new(id, cachedPath(id), url))
        { Rarity = 5, Subtitle = pulls, CountText = count };

    private static string Pulls(int? count) => count is int known ? $"{known} 抽" : "间隔未知";
    private static string Average(double? value) => value?.ToString("0.0") ?? "未知";
    private static string Date(DateTimeOffset? value) => value?.ToString("yyyy-MM-dd") ?? "未知";
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Presentation.Gacha;

// Presentation of acquisition history only. Counts and item identity remain analytics results.
public sealed class GachaItemCard : INotifyPropertyChanged
{
    private const int TimeBatchSize = 50;
    private readonly IReadOnlyList<DateTimeOffset> acquisitionTimes;
    private bool timesExpanded;
    private int visibleTimes = TimeBatchSize;

    public GachaItemCard(GachaItemStatistics item, bool archiveMode, Func<string?, string?> cachedIconPath)
    {
        LoadMoreTimesCommand = new GachaActionCommand(LoadMoreTimes);
        IsArchiveMode = archiveMode;
        Rank = item.RankType;
        acquisitionTimes = item.AcquisitionTimes.OrderByDescending(time => time).ToArray();
        timesExpanded = !archiveMode && Rank == 5;
        Icon = new("", item.ItemName, item.RankType is int rank ? $"{rank} 星" : "星级未知",
            "", "", "", new(item.ItemId, cachedIconPath(item.ItemId), item.IconUrl))
        {
            Rarity = item.RankType,
            CountText = item.Count.ToString(),
            Subtitle = "",
            ItemType = item.ItemType,
        };
    }

    public bool IsArchiveMode { get; }
    public int? Rank { get; }
    public GachaReadOnlyRow Icon { get; }
    public bool CanExpandTimes => !IsArchiveMode && Rank == 4 && acquisitionTimes.Count > 0;
    public bool IsTimesVisible => timesExpanded && acquisitionTimes.Count > 0;
    public bool CanLoadMoreTimes => IsTimesVisible && visibleTimes < acquisitionTimes.Count;
    public string ToggleTimesText => timesExpanded ? "折叠四星获取时间" : "展开四星获取时间";
    public string TimesText => !IsTimesVisible ? "" :
        string.Join("\n", acquisitionTimes.Take(visibleTimes).Select(time => time.ToString("yyyy-MM-dd HH:mm:ss zzz")));
    public string TimeCountText => IsTimesVisible
        ? $"仅来自抽卡记录 · 已显示 {Math.Min(visibleTimes, acquisitionTimes.Count)} / {acquisitionTimes.Count} 次获取" : "";

    public System.Windows.Input.ICommand LoadMoreTimesCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ToggleTimes()
    {
        if (!CanExpandTimes) return;
        timesExpanded = !timesExpanded;
        visibleTimes = TimeBatchSize;
        Refresh();
    }

    public void OpenDetails()
    {
        timesExpanded = true;
        visibleTimes = TimeBatchSize;
        Refresh();
    }

    public void LoadMoreTimes()
    {
        if (!CanLoadMoreTimes) return;
        visibleTimes = Math.Min(acquisitionTimes.Count, visibleTimes + TimeBatchSize);
        Refresh();
    }

    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed record GachaItemGroup(string Title, IReadOnlyList<GachaReadOnlyRow> Items)
{
    public static IReadOnlyList<GachaItemGroup> Build(IReadOnlyList<GachaReadOnlyRow> rows) =>
        rows.GroupBy(row => row.Rarity).OrderByDescending(group => group.Key ?? -1)
            .Select(group => new GachaItemGroup(group.Key switch
            {
                5 => "五星", 4 => "四星", 3 => "三星", null => "星级未知", _ => $"{group.Key} 星",
            }, group.ToArray())).ToArray();
}

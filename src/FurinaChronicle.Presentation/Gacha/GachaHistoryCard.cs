// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Presentation.Gacha;

public sealed class GachaHistoryCard : INotifyPropertyChanged
{
    private const int BatchSize = 48;
    private readonly IReadOnlyList<GachaReadOnlyRow> items;
    private int visibleCount = BatchSize;

    public GachaHistoryCard(
        GachaEventPeriod period,
        GachaHistoryPeriod? history,
        Func<string?, string?> cachedIconPath)
    {
        Id = period.Id;
        Version = $"v{period.Version}";
        Title = string.Join(" / ", period.Banners.Select(banner => banner.Name));
        Period = $"{period.StartsAt:yyyy-MM-dd HH:mm zzz} ～ {period.EndsAt:yyyy-MM-dd HH:mm zzz}";
        Total = $"{history?.TotalPulls ?? 0} 抽";
        Metadata = $"元数据：{period.Source} · {period.SourceRevision}" +
            (history?.MatchQuality == GachaEventMatchQuality.RegionUnverified ? " · 区服未核实" : string.Empty);
        Banners = period.Banners.Select(banner =>
            new GachaHistoryBannerVisual(banner.Name, banner.Id, banner.ImageUrl, banner.BackupImageUrl)).ToArray();
        IReadOnlyList<GachaHistoryItem> obtained = history?.Items ?? [];
        items = obtained.Select(item =>
            Item(item.ItemName, item.ItemId, item.IconUrl, item.RankType, item.Count, cachedIconPath)).ToArray();
        FeaturedItems = period.Banners.SelectMany(banner => Featured(banner))
            .DistinctBy(item => item.ItemId ?? item.Name, StringComparer.Ordinal)
            .Select(item =>
            {
                GachaHistoryItem[] matches = obtained.Where(record =>
                    item.ItemId is not null && record.ItemId is not null
                        ? item.ItemId == record.ItemId
                        : !string.IsNullOrWhiteSpace(item.Name) && item.Name == record.ItemName).ToArray();
                string name = string.IsNullOrWhiteSpace(item.Name)
                    ? matches.FirstOrDefault()?.ItemName ?? item.ItemId ?? "未知物品"
                    : item.Name;
                return Item(name, item.ItemId, item.ImageUrl ?? matches.FirstOrDefault()?.IconUrl,
                    item.RankType, matches.Sum(record => record.Count), cachedIconPath);
            }).ToArray();
        ItemsText = items.Count == 0 ? "本期暂无抽卡记录。" :
            string.Join(Environment.NewLine, obtained.Select(item => $"{item.ItemName} × {item.Count}"));
    }

    public string Id { get; }
    public string Version { get; }
    public string Title { get; }
    public string Period { get; }
    public string Total { get; }
    public string Metadata { get; }
    public string ItemsText { get; }
    public IReadOnlyList<GachaHistoryBannerVisual> Banners { get; }
    public IReadOnlyList<GachaReadOnlyRow> FeaturedItems { get; }
    public IReadOnlyList<GachaReadOnlyRow> VisibleItems => items.Take(visibleCount).ToArray();
    public bool CanLoadMore => visibleCount < items.Count;
    public string LoadMoreText => $"加载更多物品（{Math.Min(visibleCount, items.Count)} / {items.Count}）";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadMore()
    {
        visibleCount = Math.Min(items.Count, visibleCount + BatchSize);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    private static IEnumerable<GachaEventFeaturedItem> Featured(GachaEventBanner banner) =>
        banner.FeaturedItems.Count > 0 ? banner.FeaturedItems :
        banner.UpFiveStarItemIds.Select(id => new GachaEventFeaturedItem(id, "", 5, null))
            .Concat(banner.UpFourStarItemIds.Select(id => new GachaEventFeaturedItem(id, "", 4, null)));

    private static GachaReadOnlyRow Item(
        string name, string? id, string? url, int? rank, int count, Func<string?, string?> cachedPath) =>
        new("", name, rank is int rarity ? $"{rarity} 星" : "", "", "", "", new(id, cachedPath(id), url))
        { Rarity = rank, CountText = count.ToString(), Subtitle = string.Empty };
}

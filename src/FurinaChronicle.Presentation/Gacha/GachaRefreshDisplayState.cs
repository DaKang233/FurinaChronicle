// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.ComponentModel;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// Transient read model. It never saves facts, credentials, URLs or account identifiers.
public sealed class GachaRefreshDisplayState : INotifyPropertyChanged
{
    private readonly ObservableCollection<GachaRefreshDisplayItem> items = [];
    private readonly Dictionary<string, GachaRefreshDisplayItem> byItem = new(StringComparer.Ordinal);
    private readonly HashSet<string> seenRecords = new(StringComparer.Ordinal);
    private readonly Func<string?, string?> cachedIconPath;
    private int anonymousRecord;
    private int fetchedRecords;
    private long itemCount;

    public GachaRefreshDisplayState(string title, Func<string?, string?>? cachedIconPath = null)
    {
        Title = title;
        this.cachedIconPath = cachedIconPath ?? (_ => null);
        Items = new(items);
    }

    public string Title { get; private set; }
    public string CurrentPool { get; private set; } = "正在准备";
    public string Detail { get; private set; } = "正在验证账号和刷新来源…";
    public bool IsRunning { get; private set; } = true;
    public bool Succeeded { get; private set; }
    public int FetchedRecords => fetchedRecords;
    public long ItemCount => itemCount;
    public string Totals => $"已获取 {fetchedRecords} 条记录 · {itemCount} 个物品 · {items.Count} 种";
    public ReadOnlyObservableCollection<GachaRefreshDisplayItem> Items { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    // Called on the host's UI dispatcher. Received pages contain only the current request's items;
    // fetching is provisional until the atomic refresh returns successfully.
    public void Apply(GachaRefreshProgress progress)
    {
        if (!IsRunning || progress.Page < 0 || progress.FetchedCount < 0) return;
        fetchedRecords = Math.Max(fetchedRecords, progress.FetchedCount);
        if (progress.Stage == GachaRefreshProgressStage.Saving)
        {
            CurrentPool = "正在保存";
            Detail = $"已获取 {fetchedRecords} 条，正在保存抽卡记录…";
        }
        else
        {
            CurrentPool = "正在获取" + PoolName(progress.GachaType);
            Detail = progress.Stage == GachaRefreshProgressStage.RequestingPage
                ? $"正在请求第 {progress.Page} 页"
                : $"已完成第 {progress.Page} 页";
            if (progress.Stage == GachaRefreshProgressStage.PageReceived)
                foreach (var record in progress.Records) Add(record);
        }
        Changed();
    }

    public void Complete(bool succeeded, GachaRefreshResult? result)
    {
        if (!IsRunning) return;
        IsRunning = false;
        Succeeded = succeeded;
        Title = succeeded ? "抽卡记录刷新完成" : "抽卡记录刷新未完成";
        CurrentPool = succeeded ? "已完成" : "请关闭后查看通知中的说明";
        Detail = succeeded && result is not null
            ? $"获取 {result.FetchedCount} 条 · 新增 {result.InsertedCount} 条 · 校正 {result.UpdatedCount} 条 · " +
                $"保留重复 {result.DuplicateCount} 条 · 永久删除抑制 {result.SuppressedCount} 条"
            : "本次获取到的物品仅为过程预览；未确认这些记录已经保存。";
        Changed();
    }

    private void Add(GachaRemoteRecord record)
    {
        string recordKey = string.IsNullOrWhiteSpace(record.ExternalRecordId)
            ? $"anonymous:{++anonymousRecord}" : record.ExternalRecordId;
        if (!seenRecords.Add(recordKey)) return;
        string? itemId = string.IsNullOrWhiteSpace(record.ItemId) ? null : record.ItemId.Trim();
        // Unresolved items remain separate records; a display name cannot establish identity.
        string key = itemId is null ? $"record:{recordKey}" : $"id:{itemId}";
        int count = Math.Max(1, record.Count);
        itemCount += count;
        if (byItem.TryGetValue(key, out var existing))
        {
            existing.Add(count);
            return;
        }
        var item = new GachaRefreshDisplayItem(record.ItemName, record.RankType,
            new(itemId, cachedIconPath(itemId)), count);
        byItem.Add(key, item);
        items.Add(item);
    }

    private static string PoolName(string type) => type switch
    {
        "301" or "400" => "角色活动祈愿", "302" => "武器活动祈愿", "500" => "集录祈愿",
        "100" => "新手祈愿", "200" => "常驻祈愿", _ => "其他卡池",
    };

    private void Changed() => PropertyChanged?.Invoke(this, new(string.Empty));
}

public sealed class GachaRefreshDisplayItem : INotifyPropertyChanged
{
    public GachaRefreshDisplayItem(string? name, int? rarity, GachaImageReference image, int count)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "未知物品" : name.Trim();
        Rarity = rarity;
        Image = image;
        Count = Math.Max(1, count);
    }
    public string Name { get; }
    public int? Rarity { get; }
    public GachaImageReference Image { get; }
    public long Count { get; private set; }
    public bool HasCount => true;
    public string Subtitle => string.Empty;
    public string CountText => Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Add(int count)
    {
        Count += count;
        PropertyChanged?.Invoke(this, new(nameof(CountText)));
    }
}

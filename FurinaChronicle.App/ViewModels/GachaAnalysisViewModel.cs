using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Wishes;
using FurinaChronicle.Services.Wishes.Analytics;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public enum GachaAnalysisSection
{
    Overview,
    Details,
    History,
    Calendar,
    Items
}

public partial class GachaAnalysisViewModel(
    BuildWishAnalytics buildWishAnalytics,
    GetWishRecordPage getWishRecordPage,
    IGachaItemIconCache iconCache)
    : ObservableObject
{
    private PlayerArchive? archive;
    private GameAccount? selectedAccount;
    private IReadOnlyList<GameAccount> accounts = [];

    public ObservableCollection<GachaAccountFilterItem> AccountFilters { get; } = [];
    public ObservableCollection<WishPoolStatisticsDisplayItem> PoolCards { get; } = [];
    public ObservableCollection<WishRecordAnalysisDisplayItem> DetailRecords { get; } = [];
    public ObservableCollection<WishHistoryDisplayItem> HistoryItems { get; } = [];
    public ObservableCollection<WishCalendarDisplayItem> CalendarItems { get; } = [];
    public ObservableCollection<WishItemStatisticsDisplayItem> ItemStatistics { get; } = [];
    public ObservableCollection<WishItemRankGroupDisplayItem> ItemGroups { get; } = [];

    public IReadOnlyList<PoolFilterOption> PoolFilters { get; } =
    [
        new(null, "全部卡池"),
        .. Enum.GetValues<WishPoolGroup>()
            .Select(group => new PoolFilterOption(
                group,
                WishPoolGroupResolver.GetDisplayName(group)))
    ];

    public IReadOnlyList<RankFilterOption> RankFilters { get; } =
    [
        new(null, "全部星级"),
        new(5, "五星"),
        new(4, "四星"),
        new(3, "三星")
    ];

    public IReadOnlyList<int> DetailPageSizeOptions { get; } =
        [25, 50, 100, 200];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverviewVisible))]
    [NotifyPropertyChangedFor(nameof(IsDetailsVisible))]
    [NotifyPropertyChangedFor(nameof(IsHistoryVisible))]
    [NotifyPropertyChangedFor(nameof(IsCalendarVisible))]
    [NotifyPropertyChangedFor(nameof(IsItemsVisible))]
    public partial GachaAnalysisSection CurrentSection { get; set; } =
        GachaAnalysisSection.Overview;

    public bool IsOverviewVisible => CurrentSection == GachaAnalysisSection.Overview;
    public bool IsDetailsVisible => CurrentSection == GachaAnalysisSection.Details;
    public bool IsHistoryVisible => CurrentSection == GachaAnalysisSection.History;
    public bool IsCalendarVisible => CurrentSection == GachaAnalysisSection.Calendar;
    public bool IsItemsVisible => CurrentSection == GachaAnalysisSection.Items;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAccountMode))]
    [NotifyPropertyChangedFor(nameof(CanUseItemListMode))]
    public partial bool IsArchiveMode { get; set; }

    public bool IsAccountMode => !IsArchiveMode;

    public bool CanUseItemListMode => !IsArchiveMode;

    [ObservableProperty]
    public partial string ScopeDescription { get; set; } = "尚未选择档案或账号";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "暂无抽卡记录。";

    [ObservableProperty]
    public partial PoolFilterOption SelectedPoolFilter { get; set; } =
        new(null, "全部卡池");

    [ObservableProperty]
    public partial RankFilterOption SelectedRankFilter { get; set; } =
        new(null, "全部星级");

    [ObservableProperty]
    public partial bool HasStartDate { get; set; }

    [ObservableProperty]
    public partial bool HasEndDate { get; set; }

    [ObservableProperty]
    public partial DateTime StartDate { get; set; } = DateTime.Today.AddMonths(-6);

    [ObservableProperty]
    public partial DateTime EndDate { get; set; } = DateTime.Today;

    [ObservableProperty]
    public partial bool IsNewestFirst { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailPageSummary))]
    public partial int DetailPageSize { get; set; } = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToPreviousDetailPage))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextDetailPage))]
    [NotifyPropertyChangedFor(nameof(DetailPageSummary))]
    public partial int DetailPage { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoToPreviousDetailPage))]
    [NotifyPropertyChangedFor(nameof(CanGoToNextDetailPage))]
    [NotifyPropertyChangedFor(nameof(DetailPageSummary))]
    public partial int DetailTotalPages { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DetailPageSummary))]
    public partial int DetailTotalCount { get; set; }

    public string DetailPageSummary =>
        $"第 {DetailPage} / {DetailTotalPages} 页，共 {DetailTotalCount} 条，每页 {DetailPageSize} 条";

    public bool CanGoToPreviousDetailPage =>
        IsNotBusy && DetailPage > 1;

    public bool CanGoToNextDetailPage =>
        IsNotBusy && DetailPage < DetailTotalPages;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFiveStarListMode))]
    public partial bool IsFiveStarGridMode { get; set; } = true;

    public bool IsFiveStarListMode => !IsFiveStarGridMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsItemListMode))]
    public partial bool IsItemGridMode { get; set; } = true;

    public bool IsItemListMode => !IsItemGridMode;

    [RelayCommand]
    private void UseFiveStarGrid() => IsFiveStarGridMode = true;

    [RelayCommand]
    private void UseFiveStarList() => IsFiveStarGridMode = false;

    public async Task SetContextAsync(
        PlayerArchive? selectedArchive,
        IEnumerable<GameAccount> archiveAccounts,
        GameAccount? currentAccount)
    {
        ArgumentNullException.ThrowIfNull(archiveAccounts);
        Guid? previousArchiveId = archive?.Id;
        archive = selectedArchive;
        accounts = archiveAccounts.ToArray();
        selectedAccount = currentAccount;
        bool archiveChanged = previousArchiveId != archive?.Id;

        if (archiveChanged ||
            AccountFilters.Select(item => item.Account.Id)
                .Order()
                .SequenceEqual(accounts.Select(item => item.Id).Order()) is false)
        {
            AccountFilters.Clear();
            foreach (GameAccount account in accounts)
            {
                AccountFilters.Add(new GachaAccountFilterItem(account));
            }
        }

        await RefreshAsync();
    }

    public async Task SetArchiveModeAsync(bool archiveMode)
    {
        if (IsBusy || IsArchiveMode == archiveMode)
        {
            return;
        }

        ClearResults();
        IsArchiveMode = archiveMode;
        if (archiveMode)
        {
            foreach (GachaAccountFilterItem item in AccountFilters)
            {
                item.IsSelected = true;
            }
            IsItemGridMode = true;
        }
        DetailPage = 1;
        await RefreshAsync();
    }

    [RelayCommand]
    private Task ShowOverviewAsync() =>
        ShowSectionAsync(GachaAnalysisSection.Overview);

    [RelayCommand]
    private Task ShowDetailsAsync() =>
        ShowSectionAsync(GachaAnalysisSection.Details);

    [RelayCommand]
    private Task ShowHistoryAsync() =>
        ShowSectionAsync(GachaAnalysisSection.History);

    [RelayCommand]
    private Task ShowCalendarAsync() =>
        ShowSectionAsync(GachaAnalysisSection.Calendar);

    [RelayCommand]
    private Task ShowItemsAsync() =>
        ShowSectionAsync(GachaAnalysisSection.Items);

    [RelayCommand]
    private async Task ApplyScopeAsync()
    {
        DetailPage = 1;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ApplyDetailFiltersAsync()
    {
        DetailPage = 1;
        await LoadDetailAsync();
    }

    [RelayCommand]
    private async Task PreviousDetailPageAsync()
    {
        if (CanGoToPreviousDetailPage)
        {
            DetailPage--;
            await LoadDetailAsync();
        }
    }

    [RelayCommand]
    private async Task NextDetailPageAsync()
    {
        if (CanGoToNextDetailPage)
        {
            DetailPage++;
            await LoadDetailAsync();
        }
    }

    [RelayCommand]
    private void UseItemGrid() => IsItemGridMode = true;

    [RelayCommand]
    private void UseItemList()
    {
        if (CanUseItemListMode)
        {
            IsItemGridMode = false;
        }
    }

    [RelayCommand]
    private async Task ClearIconCacheAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        bool cleared = false;
        try
        {
            await iconCache.ClearAsync();
            cleared = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (cleared)
        {
            ClearResults();
            await RefreshAsync();
            if (ErrorMessage is null)
            {
                Summary = $"图标缓存已清除并重新加载。{Summary}";
            }
        }
    }

    private async Task ShowSectionAsync(GachaAnalysisSection section)
    {
        if (CurrentSection == section)
        {
            return;
        }

        CurrentSection = section;
        DetailPage = 1;
        ClearResults();
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<Guid> ids = GetScopeAccountIds();
            UpdateScopeDescription(ids);
            if (ids.Count == 0)
            {
                ClearResults();
                return;
            }

            if (CurrentSection == GachaAnalysisSection.Details)
            {
                await LoadDetailCoreAsync(ids);
                return;
            }

            WishAnalyticsReport report = await buildWishAnalytics.ExecuteAsync(
                new WishRecordQuery(ids),
                GetCurrentComponent(),
                CancellationToken.None);
            await PopulateCurrentSectionAsync(report);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanGoToPreviousDetailPage));
            OnPropertyChanged(nameof(CanGoToNextDetailPage));
        }
    }

    private async Task LoadDetailAsync()
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            IReadOnlyList<Guid> ids = GetScopeAccountIds();
            if (ids.Count == 0)
            {
                DetailRecords.Clear();
                DetailTotalCount = 0;
                DetailTotalPages = 1;
                DetailPage = 1;
                return;
            }
            await LoadDetailCoreAsync(ids);
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanGoToPreviousDetailPage));
            OnPropertyChanged(nameof(CanGoToNextDetailPage));
        }
    }

    private async Task LoadDetailCoreAsync(IReadOnlyList<Guid> ids)
    {
        WishRecordQuery query = BuildDetailQuery(ids);
        WishRecordPage page = await getWishRecordPage.ExecuteAsync(
            query,
            DetailPage,
            DetailPageSize);
        DetailRecords.Clear();
        Dictionary<Guid, GameAccount> accountMap =
            accounts.ToDictionary(account => account.Id);
        foreach (WishRecord record in page.Records)
        {
            DetailRecords.Add(
                WishRecordAnalysisDisplayItem.FromDomain(record, accountMap));
        }
        DetailPage = page.PageNumber;
        DetailTotalPages = page.TotalPages;
        DetailTotalCount = page.TotalCount;
        Summary = page.TotalCount == 0
            ? "当前筛选没有匹配的抽卡记录。"
            : $"当前筛选共 {page.TotalCount} 条抽卡记录。";
    }

    private WishRecordQuery BuildDetailQuery(IReadOnlyList<Guid> ids)
    {
        IReadOnlySet<int>? ranks = SelectedRankFilter.Value is int rank
            ? new HashSet<int> { rank }
            : null;
        IReadOnlySet<WishPoolGroup>? pools =
            SelectedPoolFilter.Value is WishPoolGroup pool
                ? new HashSet<WishPoolGroup> { pool }
                : null;
        return new WishRecordQuery(
            ids,
            ranks,
            pools,
            HasStartDate ? ToStartOfDay(StartDate) : null,
            HasEndDate ? ToEndOfDay(EndDate) : null,
            SortOrder: IsNewestFirst
                ? WishRecordSortOrder.NewestFirst
                : WishRecordSortOrder.OldestFirst);
    }

    private IReadOnlyList<Guid> GetScopeAccountIds()
    {
        if (archive is null)
        {
            return [];
        }
        if (IsArchiveMode)
        {
            return AccountFilters
                .Where(item => item.IsSelected)
                .Select(item => item.Account.Id)
                .ToArray();
        }
        return selectedAccount is null ? [] : [selectedAccount.Id];
    }

    private void UpdateScopeDescription(IReadOnlyList<Guid> ids)
    {
        ScopeDescription = archive is null
            ? "尚未选择档案"
            : IsArchiveMode
                ? $"档案“{archive.Name}”：已选择 {ids.Count} / {accounts.Count} 个账号"
                : selectedAccount is null
                    ? $"档案“{archive.Name}”：尚未选择账号"
                    : $"账号：{selectedAccount.DisplayName ?? selectedAccount.Uid}";
    }

    private static WishAnalyticsComponents GetCurrentComponent(
        GachaAnalysisSection section)
    {
        return section switch
        {
            GachaAnalysisSection.Overview => WishAnalyticsComponents.Pools,
            GachaAnalysisSection.History => WishAnalyticsComponents.History,
            GachaAnalysisSection.Calendar => WishAnalyticsComponents.Calendar,
            GachaAnalysisSection.Items => WishAnalyticsComponents.Items,
            _ => WishAnalyticsComponents.None
        };
    }

    private WishAnalyticsComponents GetCurrentComponent() =>
        GetCurrentComponent(CurrentSection);

    private async Task PopulateCurrentSectionAsync(WishAnalyticsReport report)
    {
        switch (CurrentSection)
        {
            case GachaAnalysisSection.Overview:
                await PopulateOverviewAsync(report.Pools);
                break;
            case GachaAnalysisSection.History:
                PopulateHistory(report.History);
                break;
            case GachaAnalysisSection.Calendar:
                PopulateCalendar(report.Calendar);
                break;
            case GachaAnalysisSection.Items:
                await PopulateItemsAsync(report.Items);
                break;
        }

        Summary = report.TotalPulls == 0
            ? "当前范围暂无抽卡记录。"
            : $"当前范围共 {report.TotalPulls} 抽。";
    }

    private async Task PopulateOverviewAsync(
        IReadOnlyList<WishPoolStatistics> pools)
    {
        WishPoolStatisticsDisplayItem[] displayItems =
            await Task.WhenAll(pools.Select(ToDisplayItemAsync));
        PoolCards.Clear();
        foreach (WishPoolStatisticsDisplayItem item in displayItems)
        {
            PoolCards.Add(item);
        }
    }

    private void PopulateHistory(
        IReadOnlyList<WishHistoryPeriod> history)
    {
        HistoryItems.Clear();
        foreach (WishHistoryPeriod period in history)
        {
            HistoryItems.Add(new WishHistoryDisplayItem(
                period.Date.ToString("yyyy-MM-dd"),
                period.PoolName,
                $"{period.TotalPulls} 抽",
                FormatAccounts(period.GameAccountIds),
                FormatHistoryItems(period.Items)));
        }
    }

    private void PopulateCalendar(
        IReadOnlyList<WishCalendarDay> calendar)
    {
        CalendarItems.Clear();
        foreach (WishCalendarDay day in calendar)
        {
            CalendarItems.Add(new WishCalendarDisplayItem(
                day.Date.ToString("MM-dd"),
                $"{day.TotalPulls} 抽",
                $"五星 {day.FiveStarCount} · 四星 {day.FourStarCount}",
                GetIntensityColor(day.IntensityLevel)));
        }
    }

    private async Task PopulateItemsAsync(
        IReadOnlyList<WishItemStatistics> items)
    {
        WishItemStatisticsDisplayItem[] displayItems =
            await Task.WhenAll(items.Select(async item =>
                new WishItemStatisticsDisplayItem(
                    item,
                    await GetCachedIconPathAsync(
                        item.ItemId,
                        item.IconUrl))));
        ItemStatistics.Clear();
        foreach (WishItemStatisticsDisplayItem item in displayItems)
        {
            ItemStatistics.Add(item);
        }
        ItemGroups.Clear();
        foreach (IGrouping<int?, WishItemStatisticsDisplayItem> group in
            ItemStatistics.GroupBy(item => item.RankType))
        {
            ItemGroups.Add(new WishItemRankGroupDisplayItem(
                group.Key is int rank ? $"{rank} 星" : "未知星级",
                group.ToArray()));
        }
    }

    private static string FormatHistoryItems(IReadOnlyList<WishHistoryItem> items)
    {
        List<string> summaries = items
            .Where(item => item.RankType is >= 4)
            .Select(item => $"{item.ItemName} ×{item.Count}")
            .ToList();
        int threeStarCount = items
            .Where(item => item.RankType == 3)
            .Sum(item => item.Count);
        if (threeStarCount > 0)
        {
            summaries.Add($"三星物品 ×{threeStarCount}");
        }

        return summaries.Count == 0
            ? "无物品明细"
            : string.Join("、", summaries);
    }

    private async Task<WishPoolStatisticsDisplayItem> ToDisplayItemAsync(
        WishPoolStatistics pool)
    {
        Dictionary<Guid, GameAccount> accountMap =
            accounts.ToDictionary(account => account.Id);
        var fiveStars = new List<FiveStarWishDisplayItem>();
        if (!IsArchiveMode)
        {
            fiveStars.AddRange(await Task.WhenAll(
                pool.FiveStarHistory.Select(async item =>
                    new FiveStarWishDisplayItem(
                    item.ItemName,
                    CreateFileImageSource(
                        await GetCachedIconPathAsync(
                            item.ItemId,
                            item.IconUrl)),
                    $"{item.Pulls} 抽",
                    item.Time.ToString("yyyy-MM-dd HH:mm"),
                    accountMap.TryGetValue(
                        item.GameAccountId,
                        out GameAccount? account)
                        ? account.DisplayName ?? account.Uid
                        : string.Empty))));
        }

        var itemCounts = new List<WishPoolItemCountDisplayItem>();
        if (IsArchiveMode)
        {
            itemCounts.AddRange(await Task.WhenAll(
                pool.ItemCounts.Select(async item =>
                    new WishPoolItemCountDisplayItem(
                    item.ItemName,
                    CreateFileImageSource(
                        await GetCachedIconPathAsync(
                            item.ItemId,
                            item.IconUrl)),
                    item.RankType is int rank ? $"{rank} 星" : "未知",
                    $"× {item.Count}"))));
        }

        return new WishPoolStatisticsDisplayItem(
            pool.PoolName,
            $"总抽数 {pool.TotalPulls}",
            pool.StartTime is null
                ? "暂无记录"
                : $"{pool.StartTime:yyyy-MM-dd} 至 {pool.EndTime:yyyy-MM-dd}",
            pool.AverageFiveStarPulls is double average
                ? $"任意五星期望 {average:F2} 抽"
                : "任意五星期望 —",
            pool.AverageUpFiveStarPulls is double upAverage
                ? $"限定五星期望 {upAverage:F2} 抽"
                : pool.PoolGroup is WishPoolGroup.CharacterEvent or
                    WishPoolGroup.WeaponEvent
                    ? "限定五星期望 —"
                    : "限定五星期望：不适用于该卡池",
            pool.MinimumFiveStarPulls is int minimum
                ? $"五星极值 {minimum} / {pool.MaximumFiveStarPulls} 抽"
                : "五星极值 —",
            $"五星 {pool.FiveStarCount}（{pool.FiveStarPercentage:F2}%） · " +
                $"四星 {pool.FourStarCount}（{pool.FourStarPercentage:F2}%） · " +
                $"三星 {pool.ThreeStarCount}（{pool.ThreeStarPercentage:F2}%）",
            $"距五星 {pool.PullsSinceLastFiveStar} 抽 · 距四星 {pool.PullsSinceLastFourStar} 抽",
            fiveStars,
            itemCounts);
    }

    private Task<string?> GetCachedIconPathAsync(
        string? itemId,
        string? sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Task.FromResult<string?>(null);
        }

        return iconCache.GetOrRefreshAsync(
            GachaGame.GenshinImpact,
            itemId,
            sourceUrl);
    }

    private static ImageSource? CreateFileImageSource(string? path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? null
            : ImageSource.FromFile(path);
    }

    private string FormatAccounts(IReadOnlyList<Guid> accountIds)
    {
        Dictionary<Guid, GameAccount> map =
            accounts.ToDictionary(account => account.Id);
        return string.Join(
            "、",
            accountIds.Select(id =>
                map.TryGetValue(id, out GameAccount? account)
                    ? account.DisplayName ?? account.Uid
                    : id.ToString("D")));
    }

    private void ClearResults()
    {
        PoolCards.Clear();
        DetailRecords.Clear();
        HistoryItems.Clear();
        CalendarItems.Clear();
        ItemStatistics.Clear();
        ItemGroups.Clear();
        DetailPage = 1;
        DetailTotalPages = 1;
        DetailTotalCount = 0;
        Summary = "当前范围暂无抽卡记录。";
    }

    private static DateTimeOffset ToStartOfDay(DateTime value)
    {
        DateTime local = value.Date;
        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static DateTimeOffset ToEndOfDay(DateTime value)
    {
        DateTime local = value.Date.AddDays(1).AddTicks(-1);
        return new DateTimeOffset(
            local,
            TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static Color GetIntensityColor(int level)
    {
        return level switch
        {
            5 => Color.FromArgb("#216E39"),
            4 => Color.FromArgb("#30A14E"),
            3 => Color.FromArgb("#40C463"),
            2 => Color.FromArgb("#9BE9A8"),
            1 => Color.FromArgb("#C6E48B"),
            _ => Color.FromArgb("#EBEDF0")
        };
    }
}

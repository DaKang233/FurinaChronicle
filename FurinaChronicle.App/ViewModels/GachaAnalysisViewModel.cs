// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;
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
    BuildGachaAnalytics buildGachaAnalytics,
    GetGachaRecordPage getGachaRecordPage,
    IGachaItemIconCache iconCache,
    IGachaEventCatalog eventCatalog,
    IGachaBannerImageCache bannerImageCache)
    : ObservableObject
{
    private const int ArchiveOverviewInitialFiveStarItemCount = 12;

    private PlayerArchive? archive;
    private GameAccount? selectedAccount;
    private IReadOnlyList<GameAccount> accounts = [];

    public ObservableCollection<GachaAccountFilterItem> AccountFilters { get; } = [];
    [ObservableProperty]
    public partial IReadOnlyList<GachaPoolStatisticsDisplayItem> PoolCards { get; set; } = [];
    public ObservableCollection<GachaRecordAnalysisDisplayItem> DetailRecords { get; } = [];
    public ObservableCollection<GachaHistoryDisplayItem> HistoryItems { get; } = [];
    public ObservableCollection<GachaCalendarDisplayItem> CalendarItems { get; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GachaItemStatisticsDisplayItem> ItemStatistics { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyList<GachaItemGridDisplayRow> ItemGridRows { get; set; } = [];

    public IReadOnlyList<PoolFilterOption> PoolFilters { get; } =
    [
        new(null, "全部卡池"),
        .. Enum.GetValues<GachaPoolGroup>()
            .Select(group => new PoolFilterOption(
                group,
                GachaPoolGroupResolver.GetDisplayName(group)))
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
    public partial bool IsArchiveMode { get; set; }

    public bool IsAccountMode => !IsArchiveMode;

    [ObservableProperty]
    public partial string ScopeDescription { get; set; } = "尚未选择档案或账号";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "暂无抽卡记录。";

    [ObservableProperty]
    public partial string? BannerCacheStatus { get; set; }

    [ObservableProperty]
    public partial PoolFilterOption? SelectedPoolFilter { get; set; } =
        new(null, "全部卡池");

    [ObservableProperty]
    public partial RankFilterOption? SelectedRankFilter { get; set; } =
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

        ErrorMessage = null;
        try
        {
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
        catch (Exception exception)
        {
            // This method is called by an async void UI event. Do not let a
            // binding/handler exception escape into WinUI's dispatcher.
            ErrorMessage = exception.Message;
        }
    }

    [RelayCommand]
    private Task ShowAccountScopeAsync() => SetArchiveModeAsync(false);

    [RelayCommand]
    private Task ShowArchiveScopeAsync() => SetArchiveModeAsync(true);

    public void PrepareForSection(GachaAnalysisSection section)
    {
        if (section != GachaAnalysisSection.Details)
        {
            return;
        }

        SelectedPoolFilter ??= PoolFilters[0];
        SelectedRankFilter ??= RankFilters[0];
        if (!DetailPageSizeOptions.Contains(DetailPageSize))
        {
            DetailPageSize = 50;
        }
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
    private void UseItemList() => IsItemGridMode = false;

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

    [RelayCommand]
    private async Task PreloadGachaBannerImagesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        BannerCacheStatus = "正在预下载卡池图片……";
        try
        {
            IReadOnlyList<GachaEventPeriod> periods =
                await eventCatalog.GetAllAsync(GachaGame.GenshinImpact);
            GachaEventBanner[] banners = periods
                .SelectMany(period => period.Banners)
                .DistinctBy(banner => banner.Id, StringComparer.Ordinal)
                .ToArray();
            int succeeded = 0;
            await Parallel.ForEachAsync(
                banners,
                new ParallelOptions { MaxDegreeOfParallelism = 4 },
                async (banner, cancellationToken) =>
                {
                    string? path = await bannerImageCache.GetOrRefreshAsync(
                        GachaGame.GenshinImpact,
                        banner.Id,
                        banner.ImageUrl,
                        banner.BackupImageUrl,
                        cancellationToken);
                    if (path is not null)
                    {
                        Interlocked.Increment(ref succeeded);
                    }
                });
            BannerCacheStatus =
                $"卡池图片预下载完成：{succeeded} / {banners.Length}。";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            BannerCacheStatus = "卡池图片预下载未完成。";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearGachaBannerImagesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await bannerImageCache.ClearAsync();
            BannerCacheStatus = "卡池图片缓存已清除。";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (ErrorMessage is null &&
            CurrentSection == GachaAnalysisSection.History)
        {
            for (int index = 0; index < HistoryItems.Count; index++)
            {
                GachaHistoryDisplayItem item = HistoryItems[index];
                HistoryItems[index] = item with
                {
                    Banners = item.Banners
                        .Select(banner => banner with { BannerImage = null })
                        .ToArray()
                };
            }
        }
    }

    private async Task ShowSectionAsync(GachaAnalysisSection section)
    {
        if (CurrentSection == section)
        {
            return;
        }

        PrepareForSection(section);
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

            GachaAnalyticsReport report = await buildGachaAnalytics.ExecuteAsync(
                new GachaRecordQuery(ids),
                GetCurrentComponent(),
                accounts.ToDictionary(
                    account => account.Id,
                    account => account.ServerRegion),
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
        GachaRecordQuery query = BuildDetailQuery(ids);
        GachaRecordPage page = await getGachaRecordPage.ExecuteAsync(
            query,
            DetailPage,
            DetailPageSize);
        DetailRecords.Clear();
        Dictionary<Guid, GameAccount> accountMap =
            accounts.ToDictionary(account => account.Id);
        foreach (GachaRecord record in page.Records)
        {
            DetailRecords.Add(
                GachaRecordAnalysisDisplayItem.FromDomain(record, accountMap));
        }
        DetailPage = page.PageNumber;
        DetailTotalPages = page.TotalPages;
        DetailTotalCount = page.TotalCount;
        Summary = page.TotalCount == 0
            ? "当前筛选没有匹配的抽卡记录。"
            : $"当前筛选共 {page.TotalCount} 条抽卡记录。";
    }

    private GachaRecordQuery BuildDetailQuery(IReadOnlyList<Guid> ids)
    {
        PrepareForSection(GachaAnalysisSection.Details);
        IReadOnlySet<int>? ranks = SelectedRankFilter?.Value is int rank
            ? new HashSet<int> { rank }
            : null;
        IReadOnlySet<GachaPoolGroup>? pools =
            SelectedPoolFilter?.Value is GachaPoolGroup pool
                ? new HashSet<GachaPoolGroup> { pool }
                : null;
        return new GachaRecordQuery(
            ids,
            ranks,
            pools,
            HasStartDate ? ToStartOfDay(StartDate) : null,
            HasEndDate ? ToEndOfDay(EndDate) : null,
            SortOrder: IsNewestFirst
                ? GachaRecordSortOrder.NewestFirst
                : GachaRecordSortOrder.OldestFirst);
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

    private static GachaAnalyticsComponents GetCurrentComponent(
        GachaAnalysisSection section)
    {
        return section switch
        {
            GachaAnalysisSection.Overview => GachaAnalyticsComponents.Pools,
            GachaAnalysisSection.History => GachaAnalyticsComponents.History,
            GachaAnalysisSection.Calendar => GachaAnalyticsComponents.Calendar,
            GachaAnalysisSection.Items => GachaAnalyticsComponents.Items,
            _ => GachaAnalyticsComponents.None
        };
    }

    private GachaAnalyticsComponents GetCurrentComponent() =>
        GetCurrentComponent(CurrentSection);

    private async Task PopulateCurrentSectionAsync(GachaAnalyticsReport report)
    {
        switch (CurrentSection)
        {
            case GachaAnalysisSection.Overview:
                await PopulateOverviewAsync(report.Pools);
                break;
            case GachaAnalysisSection.History:
                await PopulateHistoryAsync(report.History);
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
        IReadOnlyList<GachaPoolStatistics> pools)
    {
        PoolCards = await Task.WhenAll(pools.Select(ToDisplayItemAsync));
    }

    private async Task PopulateHistoryAsync(
        IReadOnlyList<GachaHistoryPeriod> history)
    {
        HistoryItems.Clear();
        foreach (GachaHistoryPeriod period in history)
        {
            IReadOnlyList<GachaHistoryBannerDisplayItem> banners =
                period.EventPeriod is null
                    ? []
                    : await Task.WhenAll(period.EventPeriod.Banners.Select(
                        ToHistoryBannerDisplayItemAsync));
            string title = period.EventPeriod is null
                ? $"{period.StartTime:yyyy-MM-dd} · 未匹配卡池元数据"
                : string.Join(
                    " / ",
                    period.EventPeriod.Banners.Select(banner => banner.Name));
            string periodText = period.EventPeriod is null
                ? $"记录时间 {period.StartTime:yyyy-MM-dd HH:mm} 至 " +
                    $"{period.EndTime:yyyy-MM-dd HH:mm}"
                : $"版本 {period.EventPeriod.Version} · " +
                    $"第 {period.EventPeriod.PhaseOrder} 期 · " +
                    $"{period.StartTime:yyyy-MM-dd HH:mm} 至 " +
                    $"{period.EndTime:yyyy-MM-dd HH:mm}";
            string metadataNote = period.MatchQuality switch
            {
                GachaEventMatchQuality.Verified =>
                    $"来源：{FormatEventSource(period.EventPeriod!)} · 国服时间",
                GachaEventMatchQuality.RegionUnverified =>
                    $"来源：{FormatEventSource(period.EventPeriod!)} · " +
                        "账号区服未知，匹配未经验证",
                _ => "未找到适用的卡池事件元数据；记录按日期回退分组。"
            };
            HistoryItems.Add(new GachaHistoryDisplayItem(
                title,
                periodText,
                period.PoolName,
                $"{period.TotalPulls} 抽",
                FormatAccounts(period.GameAccountIds),
                FormatHistoryItems(period.Items),
                metadataNote,
                banners));
        }
    }

    private async Task<GachaHistoryBannerDisplayItem>
        ToHistoryBannerDisplayItemAsync(GachaEventBanner banner)
    {
        string? path = await bannerImageCache.GetOrRefreshAsync(
            GachaGame.GenshinImpact,
            banner.Id,
            banner.ImageUrl,
            banner.BackupImageUrl);
        return new GachaHistoryBannerDisplayItem(
            banner.Name,
            GetBannerTypeName(banner.GachaType),
            CreateFileImageSource(path),
            $"五星 UP {banner.UpFiveStarItemIds.Count} · " +
                $"四星 UP {banner.UpFourStarItemIds.Count}");
    }

    private static string GetBannerTypeName(int gachaType)
    {
        return gachaType switch
        {
            301 => "角色活动祈愿-1",
            400 => "角色活动祈愿-2",
            302 => "武器活动祈愿",
            500 => "集录祈愿",
            _ => $"卡池类型 {gachaType}"
        };
    }

    private static string FormatEventSource(GachaEventPeriod period)
    {
        string revision = period.SourceRevision.Length > 8
            ? period.SourceRevision[..8]
            : period.SourceRevision;
        return $"{period.Source}@{revision}";
    }

    private void PopulateCalendar(
        IReadOnlyList<GachaCalendarDay> calendar)
    {
        CalendarItems.Clear();
        foreach (GachaCalendarDay day in calendar)
        {
            CalendarItems.Add(new GachaCalendarDisplayItem(
                day.Date.ToString("MM-dd"),
                $"{day.TotalPulls} 抽",
                $"五星 {day.FiveStarCount} · 四星 {day.FourStarCount}",
                GetIntensityColor(day.IntensityLevel)));
        }
    }

    private async Task PopulateItemsAsync(
        IReadOnlyList<GachaItemStatistics> items)
    {
        GachaItemStatisticsDisplayItem[] displayItems =
            await Task.WhenAll(items.Select(async item =>
                new GachaItemStatisticsDisplayItem(
                    item,
                    await GetCachedIconPathAsync(
                        item.ItemId,
                        item.IconUrl))));
        ItemStatistics = displayItems;
        var rows = new List<GachaItemGridDisplayRow>();
        foreach (IGrouping<int?, GachaItemStatisticsDisplayItem> group in
            ItemStatistics.GroupBy(item => item.RankType))
        {
            GachaItemStatisticsDisplayItem[] groupItems = group.ToArray();
            rows.Add(new GachaItemGridDisplayRow(
                group.Key is int rank ? $"{rank} 星" : "未知星级",
                []));
            rows.Add(new GachaItemGridDisplayRow(null, groupItems));
        }
        ItemGridRows = rows;
    }

    private static string FormatHistoryItems(IReadOnlyList<GachaHistoryItem> items)
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

    private async Task<GachaPoolStatisticsDisplayItem> ToDisplayItemAsync(
        GachaPoolStatistics pool)
    {
        Dictionary<Guid, GameAccount> accountMap =
            accounts.ToDictionary(account => account.Id);
        var fiveStars = new List<FiveStarGachaDisplayItem>();
        if (!IsArchiveMode)
        {
            fiveStars.AddRange(await Task.WhenAll(
                pool.FiveStarHistory.Select(async item =>
                    new FiveStarGachaDisplayItem(
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

        GachaPoolItemCount[] archiveFiveStarItems = [];
        IReadOnlyList<GachaPoolItemCountDisplayItem> initialArchiveItems = [];
        Func<int, int, Task<IReadOnlyList<GachaPoolItemCountDisplayItem>>>?
            loadArchiveItems = null;
        if (IsArchiveMode)
        {
            // Archive item counts are aggregate results rather than chronological
            // events. Preserve the analytics service order when paging them.
            archiveFiveStarItems = pool.ItemCounts
                .Where(item => item.RankType == 5)
                .ToArray();
            loadArchiveItems = (skip, take) =>
                LoadArchiveItemCountDisplayItemsAsync(
                    archiveFiveStarItems,
                    skip,
                    take);
            initialArchiveItems = await loadArchiveItems(
                0,
                ArchiveOverviewInitialFiveStarItemCount);
        }

        return new GachaPoolStatisticsDisplayItem(
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
                : pool.PoolGroup is GachaPoolGroup.CharacterEvent or
                    GachaPoolGroup.WeaponEvent
                    ? "限定五星期望 —"
                    : "限定五星期望：不适用于该卡池",
            pool.MinimumFiveStarPulls is int minimum
                ? $"五星极值 {minimum} / {pool.MaximumFiveStarPulls} 抽"
                : "五星极值 —",
            $"五星 {pool.FiveStarCount}（{pool.FiveStarPercentage:F2}%） · " +
                $"四星 {pool.FourStarCount}（{pool.FourStarPercentage:F2}%） · " +
                $"三星 {pool.ThreeStarCount}（{pool.ThreeStarPercentage:F2}%）",
            $"距上个五星 {pool.PullsSinceLastFiveStar} 抽 · 距上个四星 {pool.PullsSinceLastFourStar} 抽",
            fiveStars,
            initialArchiveItems,
            archiveFiveStarItems.Length,
            loadArchiveItems);
    }

    private async Task<IReadOnlyList<GachaPoolItemCountDisplayItem>>
        LoadArchiveItemCountDisplayItemsAsync(
            IReadOnlyList<GachaPoolItemCount> items,
            int skip,
            int take)
    {
        GachaPoolItemCount[] page = items
            .Skip(skip)
            .Take(take)
            .ToArray();
        return await Task.WhenAll(page.Select(async item =>
            new GachaPoolItemCountDisplayItem(
                item.ItemName,
                CreateFileImageSource(
                    await GetCachedIconPathAsync(
                        item.ItemId,
                        item.IconUrl)),
                item.RankType,
                item.RankType is int rank ? $"{rank} 星" : "未知",
                $"× {item.Count}")));
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
        PoolCards = [];
        DetailRecords.Clear();
        HistoryItems.Clear();
        CalendarItems.Clear();
        ItemStatistics = [];
        ItemGridRows = [];
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

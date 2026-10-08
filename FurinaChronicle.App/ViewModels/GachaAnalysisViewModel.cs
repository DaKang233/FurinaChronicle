// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.History;
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
    IGachaItemMetadataProvider itemMetadataProvider,
    IGachaEventCatalog eventCatalog,
    IGachaBannerImageCache bannerImageCache,
    PreloadGachaBannerImages preloadGachaBannerImages,
    IGachaAtomicChangeStore atomicChangeStore,
    ManageGachaHistory manageGachaHistory)
    : ObservableObject
{
    private const int ArchiveOverviewInitialFiveStarItemCount = 12;

    private PlayerArchive? archive;
    private GameAccount? selectedAccount;
    private IReadOnlyList<GameAccount> accounts = [];
    private IReadOnlyDictionary<string, GachaHistoryPeriod>
        historyPeriodsByEventId =
        new Dictionary<string, GachaHistoryPeriod>(StringComparer.Ordinal);
    private bool automaticBannerCacheStarted;
    private int historySelectionRevision;

    public ObservableCollection<GachaAccountFilterItem> AccountFilters { get; } = [];
    [ObservableProperty]
    public partial IReadOnlyList<GachaPoolStatisticsDisplayItem> PoolCards { get; set; } = [];
    public ObservableCollection<GachaRecordAnalysisDisplayItem> DetailRecords { get; } = [];
    public ObservableCollection<GachaOperationHistoryDisplayItem> OperationHistory { get; } = [];
    public ObservableCollection<GachaRevisionDisplayItem> SelectedOperationRevisions { get; } = [];
    public ObservableCollection<GachaHistoryDisplayItem> HistoryItems { get; } = [];
    public ObservableCollection<GachaHistoryVersionOption> HistoryVersions { get; } = [];
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
    [NotifyPropertyChangedFor(nameof(CanManageBannerCache))]
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
    public partial GachaRecordAnalysisDisplayItem? SelectedDetailRecord { get; set; }

    [ObservableProperty]
    public partial GachaOperationHistoryDisplayItem? SelectedOperation { get; set; }

    [ObservableProperty]
    public partial int UndoLimit { get; set; } = 3;

    [ObservableProperty]
    public partial string HistoryStatus { get; set; } = "尚未加载修改历史。";

    [ObservableProperty]
    public partial string Summary { get; set; } = "暂无抽卡记录。";

    [ObservableProperty]
    public partial string? BannerCacheStatus { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanManageBannerCache))]
    public partial bool IsBannerCacheBusy { get; set; }

    public bool CanManageBannerCache => !IsBusy && !IsBannerCacheBusy;

    [ObservableProperty]
    public partial GachaHistoryVersionOption? SelectedHistoryVersion { get; set; }

    [ObservableProperty]
    public partial GachaHistoryDisplayItem? SelectedHistoryItem { get; set; }

    public bool HasNewerHistoryVersion =>
        GetSelectedHistoryVersionIndex() > 0;

    public bool HasOlderHistoryVersion
    {
        get
        {
            int index = GetSelectedHistoryVersionIndex();
            return index >= 0 && index < HistoryVersions.Count - 1;
        }
    }

    public string HistoryVersionSummary
    {
        get
        {
            int index = GetSelectedHistoryVersionIndex();
            return index < 0 || SelectedHistoryVersion is null
                ? "尚无版本"
                : $"{SelectedHistoryVersion.DisplayName} · " +
                    $"{index + 1} / {HistoryVersions.Count}";
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHistoryTextMode))]
    public partial bool IsHistoryIconMode { get; set; } = true;

    public bool IsHistoryTextMode => !IsHistoryIconMode;

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
        if (IsBusy || IsBannerCacheBusy)
        {
            return;
        }

        await CacheAllGachaBannerImagesAsync();
        if (CurrentSection == GachaAnalysisSection.History)
        {
            await ReloadSelectedHistoryVersionAsync();
        }
    }

    private void StartAutomaticBannerCache()
    {
        if (automaticBannerCacheStarted)
        {
            return;
        }

        automaticBannerCacheStarted = true;
        _ = RunAutomaticBannerCacheAsync();
    }

    private async Task RunAutomaticBannerCacheAsync()
    {
        try
        {
            await CacheAllGachaBannerImagesAsync();
            if (CurrentSection == GachaAnalysisSection.History)
            {
                await ReloadSelectedHistoryVersionAsync();
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            BannerCacheStatus = "卡池图片自动缓存未完成。";
        }
    }

    private async Task CacheAllGachaBannerImagesAsync()
    {
        if (IsBannerCacheBusy)
        {
            return;
        }

        IsBannerCacheBusy = true;
        ErrorMessage = null;
        BannerCacheStatus = "正在缓存全部限定卡池图片；完成前历史页仅显示文字……";
        try
        {
            GachaBannerImagePreloadResult result =
                await preloadGachaBannerImages.ExecuteAsync(
                    GachaGame.GenshinImpact);
            BannerCacheStatus = result.Failed == 0
                ? $"卡池图片缓存完成：{result.Succeeded} / {result.Total}。"
                : $"卡池图片缓存完成：成功 {result.Succeeded}，" +
                    $"失败 {result.Failed}；" +
                    "失败图片不影响其他横幅。";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            BannerCacheStatus = "卡池图片缓存未完成。";
        }
        finally
        {
            IsBannerCacheBusy = false;
        }
    }

    [RelayCommand]
    private async Task ClearGachaBannerImagesAsync()
    {
        if (IsBusy || IsBannerCacheBusy)
        {
            return;
        }

        IsBannerCacheBusy = true;
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
            IsBannerCacheBusy = false;
        }

        if (ErrorMessage is null &&
            CurrentSection == GachaAnalysisSection.History)
        {
            await ReloadSelectedHistoryVersionAsync();
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
        await LoadOperationHistoryCoreAsync();
    }

    public async Task LoadSelectedOperationRevisionsAsync()
    {
        SelectedOperationRevisions.Clear();
        if (SelectedOperation is null)
        {
            HistoryStatus = "请先选择一项操作。";
            return;
        }

        IReadOnlyList<GachaRevisionItem> revisions =
            await atomicChangeStore.GetRevisionsAsync(
                SelectedOperation.Item.ChangeSetId);
        foreach (GachaRevisionItem revision in revisions)
        {
            SelectedOperationRevisions.Add(
                GachaRevisionDisplayItem.FromDomain(revision));
        }
        HistoryStatus = revisions.Count == 0
            ? "此操作没有保留可显示的 Gacha 修订内容。"
            : $"已加载 {revisions.Count} 条修订。";
    }

    public GachaOperationHistoryDisplayItem? GetLatestUndoCandidate() =>
        OperationHistory.FirstOrDefault(item => item.IsUndoEligible);

    public async Task<GachaAtomicChangeResult> CorrectSelectedRecordAsync(
        OperationId operationId,
        string? itemName,
        string? itemId,
        int? rankType,
        DateTimeOffset occurredAt,
        Guid? cleanupConfirmationId = null)
    {
        GachaRecordAnalysisDisplayItem selected =
            SelectedDetailRecord ?? throw new InvalidOperationException(
                "请先选择一条抽卡记录。");
        GachaFactReference reference = new(
            selected.GameAccountId,
            selected.ExternalRecordId);
        GachaAtomicChangeResult result =
            await manageGachaHistory.CorrectAsync(
            operationId,
            reference,
            new GachaCorrectionInput(
                itemName,
                itemId,
                rankType,
                occurredAt),
            cleanupConfirmationId);
        await RefreshAfterAppliedAsync(result);
        return result;
    }

    public async Task<GachaAtomicChangeResult> DeleteSelectedRecordAsync(
        OperationId operationId,
        Guid? cleanupConfirmationId = null)
    {
        GachaRecordAnalysisDisplayItem selected =
            SelectedDetailRecord ?? throw new InvalidOperationException(
                "请先选择一条抽卡记录。");
        GachaFactReference reference = new(
            selected.GameAccountId,
            selected.ExternalRecordId);
        GachaAtomicChangeResult result =
            await manageGachaHistory.DeleteAsync(
                operationId,
                reference,
                cleanupConfirmationId);
        await RefreshAfterAppliedAsync(result);
        return result;
    }

    public async Task<GachaAtomicChangeResult>
        IrreversiblyDeleteSelectedRecordAsync(OperationId operationId)
    {
        GachaRecordAnalysisDisplayItem selected =
            SelectedDetailRecord ?? throw new InvalidOperationException(
                "请先选择一条抽卡记录。");
        GachaFactReference reference = new(
            selected.GameAccountId,
            selected.ExternalRecordId);
        GachaAtomicChangeResult result =
            await manageGachaHistory.IrreversiblyDeleteAsync(
                operationId,
                reference);
        await RefreshAfterAppliedAsync(result);
        return result;
    }

    public async Task<GachaAtomicChangeResult> UndoLatestAsync(
        OperationId operationId)
    {
        Guid archiveId = archive?.Id ?? throw new InvalidOperationException(
            "请先选择档案。");
        GachaAtomicChangeResult result =
            await manageGachaHistory.UndoLatestAsync(
                operationId,
                archiveId);
        await RefreshAfterAppliedAsync(result);
        return result;
    }

    public async Task SetUndoLimitAsync(int limit)
    {
        Guid archiveId = archive?.Id ?? throw new InvalidOperationException(
            "请先选择档案。");
        UndoLimitChangeResult result =
            await atomicChangeStore.SetUndoLimitAsync(archiveId, limit);
        UndoLimit = result.CurrentLimit;
        HistoryStatus = result.EvictedChangeSetIds.Count == 0
            ? $"撤销保留次数已设为 {result.CurrentLimit}。"
            : $"撤销保留次数已设为 {result.CurrentLimit}，" +
                $"并清理了 {result.EvictedChangeSetIds.Count} 份较早撤销材料。";
        await LoadOperationHistoryCoreAsync();
    }

    private async Task RefreshAfterAppliedAsync(
        GachaAtomicChangeResult result)
    {
        if (result.Status is ChangeExecutionStatus.Applied or
            ChangeExecutionStatus.AlreadyCommitted)
        {
            SelectedDetailRecord = null;
            await RefreshAsync();
        }
    }

    private async Task LoadOperationHistoryCoreAsync()
    {
        OperationHistory.Clear();
        SelectedOperationRevisions.Clear();
        SelectedOperation = null;
        if (archive is null)
        {
            HistoryStatus = "请先选择档案。";
            return;
        }
        UndoLimit = await atomicChangeStore.GetUndoLimitAsync(archive.Id);
        IReadOnlyList<OperationHistoryItem> history =
            await atomicChangeStore.GetHistoryAsync(archive.Id, 30);
        foreach (OperationHistoryItem item in history)
        {
            OperationHistory.Add(
                GachaOperationHistoryDisplayItem.FromDomain(item));
        }
        HistoryStatus = history.Count == 0
            ? "当前档案尚无可显示的修改操作。"
            : $"显示最近 {history.Count} 项操作；撤销仅作用于最新可撤销项。";
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
        historyPeriodsByEventId = history
            .Where(period =>
                period.EventPeriod is not null &&
                IsLimitedHistoryPool(period.PoolGroup))
            .ToDictionary(
                period => period.EventPeriod!.Id,
                StringComparer.Ordinal);

        IReadOnlySet<GameServerRegion> scopeRegions = GetScopeAccountIds()
            .Select(id => accounts.FirstOrDefault(account => account.Id == id))
            .Where(account => account is not null)
            .Select(account => account!.ServerRegion)
            .ToHashSet();
        DateTimeOffset now = DateTimeOffset.Now;
        IReadOnlyList<GachaEventPeriod> allPeriods =
            await eventCatalog.GetAllAsync(GachaGame.GenshinImpact);
        GachaHistoryVersionOption[] versions = allPeriods
            .Where(period =>
                IsLimitedHistoryPool(period.PoolGroup) &&
                period.StartsAt <= now &&
                SupportsAnyHistoryRegion(period, scopeRegions))
            .GroupBy(period => period.Version, StringComparer.Ordinal)
            .Select(group => new GachaHistoryVersionOption(
                group.Key,
                FormatHistoryVersionLabel(group.Key),
                group.Max(period => period.StartsAt),
                group
                    .OrderByDescending(period => period.StartsAt)
                    .ThenBy(period => GetHistoryPoolOrder(period.PoolGroup))
                    .ThenBy(period => period.Banners[0].GachaType)
                    .ToArray()))
            .OrderByDescending(option => option.StartsAt)
            .ToArray();

        HistoryVersions.Clear();
        foreach (GachaHistoryVersionOption version in versions)
        {
            HistoryVersions.Add(version);
        }

        GachaHistoryVersionOption? selected = versions.FirstOrDefault(
                option => option.EventPeriods.Any(period => period.Contains(now)))
            ?? versions.FirstOrDefault();
        SelectedHistoryVersion = selected;
        await PopulateSelectedHistoryVersionAsync(selected);
        StartAutomaticBannerCache();
    }

    [RelayCommand]
    private async Task SelectHistoryVersionAsync(
        GachaHistoryVersionOption? version)
    {
        if (version is null)
        {
            return;
        }

        SelectedHistoryVersion = version;
        await PopulateSelectedHistoryVersionAsync(version);
    }

    [RelayCommand]
    private Task ShowNewerHistoryVersionAsync() =>
        MoveHistoryVersionAsync(-1);

    [RelayCommand]
    private Task ShowOlderHistoryVersionAsync() =>
        MoveHistoryVersionAsync(1);

    private async Task MoveHistoryVersionAsync(int offset)
    {
        int index = GetSelectedHistoryVersionIndex();
        int target = index + offset;
        if (index < 0 || target < 0 || target >= HistoryVersions.Count)
        {
            return;
        }

        await SelectHistoryVersionAsync(HistoryVersions[target]);
    }

    private int GetSelectedHistoryVersionIndex() =>
        SelectedHistoryVersion is null
            ? -1
            : HistoryVersions.IndexOf(SelectedHistoryVersion);

    partial void OnSelectedHistoryVersionChanged(
        GachaHistoryVersionOption? value)
    {
        OnPropertyChanged(nameof(HasNewerHistoryVersion));
        OnPropertyChanged(nameof(HasOlderHistoryVersion));
        OnPropertyChanged(nameof(HistoryVersionSummary));
    }

    private Task ReloadSelectedHistoryVersionAsync() =>
        PopulateSelectedHistoryVersionAsync(SelectedHistoryVersion);

    private async Task PopulateSelectedHistoryVersionAsync(
        GachaHistoryVersionOption? version)
    {
        int revision = Interlocked.Increment(ref historySelectionRevision);
        if (version is null)
        {
            HistoryItems.Clear();
            SelectedHistoryItem = null;
            return;
        }

        string? selectedKey = SelectedHistoryItem?.Key;
        GachaHistoryDisplayItem[] displayItems = await Task.WhenAll(
            version.EventPeriods.Select(ToHistoryDisplayItemAsync));
        if (revision != historySelectionRevision ||
            SelectedHistoryVersion?.Key != version.Key)
        {
            return;
        }

        HistoryItems.Clear();
        foreach (GachaHistoryDisplayItem item in displayItems)
        {
            HistoryItems.Add(item);
        }
        SelectedHistoryItem = displayItems.FirstOrDefault(
                item => item.Key == selectedKey)
            ?? displayItems.FirstOrDefault();
        OnPropertyChanged(nameof(HasNewerHistoryVersion));
        OnPropertyChanged(nameof(HasOlderHistoryVersion));
        OnPropertyChanged(nameof(HistoryVersionSummary));
    }

    private async Task<GachaHistoryDisplayItem> ToHistoryDisplayItemAsync(
        GachaEventPeriod eventPeriod)
    {
        historyPeriodsByEventId.TryGetValue(
            eventPeriod.Id,
            out GachaHistoryPeriod? history);
        GachaHistoryBannerDisplayItem[] banners = await Task.WhenAll(
            eventPeriod.Banners.Select(ToHistoryBannerDisplayItemAsync));
        IReadOnlyList<GachaHistoryItem> items = history?.Items ?? [];
        GachaHistoryItemDisplayItem[] featuredItems = await BuildFeaturedItemsAsync(
            eventPeriod.Banners.Single(),
            items);
        GachaHistoryItemDisplayItem[] itemIcons = await Task.WhenAll(
            items.Select(async item => new GachaHistoryItemDisplayItem(
                item.ItemName,
                CreateFileImageSource(
                    await GetCachedIconPathAsync(item.ItemId, item.IconUrl)),
                $"× {item.Count}",
                item.RankType)));
        string metadataNote = history?.MatchQuality switch
        {
            GachaEventMatchQuality.Verified =>
                $"来源：{FormatEventSource(eventPeriod)} · 国服时间",
            GachaEventMatchQuality.RegionUnverified =>
                $"来源：{FormatEventSource(eventPeriod)} · " +
                    "账号区服未知，匹配未经验证",
            _ => $"来源：{FormatEventSource(eventPeriod)} · 当前范围无抽卡记录"
        };
        return new GachaHistoryDisplayItem(
            eventPeriod.Id,
            FormatHistoryVersionLabel(eventPeriod.Version),
            eventPeriod.Banners.Single().Name,
            $"版本 {eventPeriod.Version} · " +
                $"{eventPeriod.StartsAt:yyyy-MM-dd HH:mm} 至 " +
                $"{eventPeriod.EndsAt:yyyy-MM-dd HH:mm}",
            GachaPoolGroupResolver.GetDisplayName(eventPeriod.PoolGroup),
            $"{history?.TotalPulls ?? 0} 抽",
            history is null || history.GameAccountIds.Count == 0
                ? "无抽卡记录"
                : FormatAccounts(history.GameAccountIds),
            FormatHistoryItems(items),
            itemIcons,
            featuredItems,
            metadataNote,
            banners);
    }

    private async Task<GachaHistoryItemDisplayItem[]> BuildFeaturedItemsAsync(
        GachaEventBanner banner,
        IReadOnlyList<GachaHistoryItem> obtainedItems)
    {
        IReadOnlyList<GachaEventFeaturedItem> sourceItems =
            banner.FeaturedItems.Count > 0
                ? banner.FeaturedItems
                : banner.UpFiveStarItemIds
                    .Select(id => new GachaEventFeaturedItem(
                        id,
                        string.Empty,
                        5,
                        null))
                    .Concat(banner.UpFourStarItemIds.Select(id =>
                        new GachaEventFeaturedItem(
                            id,
                            string.Empty,
                            4,
                            null)))
                    .ToArray();
        return await Task.WhenAll(sourceItems.Select(async source =>
        {
            GachaItemMetadata? metadata = null;
            if (!string.IsNullOrWhiteSpace(source.ItemId))
            {
                metadata = await itemMetadataProvider.FindByIdAsync(
                    GachaGame.GenshinImpact,
                    source.ItemId);
            }
            else if (!string.IsNullOrWhiteSpace(source.Name))
            {
                metadata = await itemMetadataProvider.FindByNameAsync(
                    GachaGame.GenshinImpact,
                    source.Name);
            }

            string? itemId = source.ItemId ?? metadata?.ItemId;
            string itemName = !string.IsNullOrWhiteSpace(source.Name)
                ? source.Name
                : metadata?.Name ?? itemId ?? "未知物品";
            int count = obtainedItems
                .Where(item =>
                    (!string.IsNullOrWhiteSpace(itemId) &&
                        string.Equals(
                            item.ItemId,
                            itemId,
                            StringComparison.Ordinal)) ||
                    string.Equals(
                        item.ItemName,
                        itemName,
                        StringComparison.Ordinal))
                .Sum(item => item.Count);
            return new GachaHistoryItemDisplayItem(
                itemName,
                CreateFileImageSource(await GetCachedIconPathAsync(
                    itemId,
                    source.ImageUrl ?? metadata?.IconUrl)),
                $"× {count}",
                source.RankType);
        }));
    }

    private async Task<GachaHistoryBannerDisplayItem>
        ToHistoryBannerDisplayItemAsync(GachaEventBanner banner)
    {
        string? path = await bannerImageCache.GetCachedPathAsync(
            GachaGame.GenshinImpact,
            banner.Id);
        return new GachaHistoryBannerDisplayItem(
            banner.Name,
            GetBannerTypeName(banner.GachaType),
            CreateFileImageSource(path),
            $"五星 UP {banner.UpFiveStarItemIds.Count} · " +
                $"四星 UP {banner.UpFourStarItemIds.Count}");
    }

    private static bool IsLimitedHistoryPool(GachaPoolGroup pool) =>
        pool is GachaPoolGroup.CharacterEvent or
            GachaPoolGroup.WeaponEvent or
            GachaPoolGroup.Chronicled;

    private static bool SupportsAnyHistoryRegion(
        GachaEventPeriod period,
        IReadOnlySet<GameServerRegion> regions) =>
        regions.Count == 0 ||
        regions.Contains(GameServerRegion.Unknown) ||
        regions.Any(period.ServerRegions.Contains);

    private static string FormatHistoryVersionLabel(string version) =>
        $"v{version}";

    private static int GetHistoryPoolOrder(GachaPoolGroup pool) => pool switch
    {
        GachaPoolGroup.CharacterEvent => 0,
        GachaPoolGroup.WeaponEvent => 1,
        GachaPoolGroup.Chronicled => 2,
        _ => 3
    };

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
        var limitedFiveStars = new List<FiveStarGachaDisplayItem>();
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
            limitedFiveStars.AddRange(await Task.WhenAll(
                pool.LimitedFiveStarHistory.Select(async item =>
                    new FiveStarGachaDisplayItem(
                    item.ItemName,
                    CreateFileImageSource(
                        await GetCachedIconPathAsync(
                            item.ItemId,
                            item.IconUrl)),
                    item.PullsSincePreviousLimitedFiveStar is int pulls
                        ? $"{pulls} 抽"
                        : "—",
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
                ? $"任意五星均值 {average:F2} 抽"
                : "任意五星均值 —",
            pool.AverageUpFiveStarPulls is double upAverage
                ? $"限定五星均值 {upAverage:F2} 抽"
                : pool.PoolGroup is GachaPoolGroup.CharacterEvent or
                    GachaPoolGroup.WeaponEvent
                    ? "限定五星均值 —"
                    : "限定五星均值：不适用于该卡池",
            pool.MinimumFiveStarPulls is int minimum
                ? $"五星极值 {minimum} / {pool.MaximumFiveStarPulls} 抽"
                : "五星极值 —",
            $"五星 {pool.FiveStarCount}（{pool.FiveStarPercentage:F2}%） · " +
                $"四星 {pool.FourStarCount}（{pool.FourStarPercentage:F2}%） · " +
                $"三星 {pool.ThreeStarCount}（{pool.ThreeStarPercentage:F2}%）",
            $"距上个五星 {pool.PullsSinceLastFiveStar} 抽 · 距上个四星 {pool.PullsSinceLastFourStar} 抽",
            fiveStars,
            limitedFiveStars,
            !IsArchiveMode &&
                pool.PoolGroup == GachaPoolGroup.CharacterEvent,
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
        SelectedDetailRecord = null;
        OperationHistory.Clear();
        SelectedOperation = null;
        SelectedOperationRevisions.Clear();
        HistoryItems.Clear();
        HistoryVersions.Clear();
        SelectedHistoryVersion = null;
        SelectedHistoryItem = null;
        historyPeriodsByEventId =
            new Dictionary<string, GachaHistoryPeriod>(StringComparer.Ordinal);
        Interlocked.Increment(ref historySelectionRevision);
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

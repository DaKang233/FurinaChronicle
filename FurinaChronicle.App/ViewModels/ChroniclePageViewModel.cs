// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.App.ViewModels;

/// <summary>
/// MAUI Chronicle page state. Query/paging/stale logic lives in the shared
/// <see cref="ChronicleQueryController"/>; this view model only owns page-local
/// selection, filter options and display projection.
/// </summary>
public partial class ChroniclePageViewModel : ObservableObject
{
    private const int DefaultPageSize = 30;

    private readonly GetPlayerArchives getPlayerArchives;
    private readonly GetGameAccounts getGameAccounts;
    private readonly IDomainMetadataService metadata;
    private readonly ChronicleQueryController controller;
    private readonly Dictionary<string, string> itemNames =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> accountLabels = [];

    private IReadOnlyList<PlayerArchive> archives = [];
    private IReadOnlyList<GameAccount> accounts = [];
    private Guid? preferredArchiveId;
    private Guid? preferredAccountId;
    private bool suppressSelectionReload;
    private bool resetDisplays = true;
    private bool archiveLoadFailed;
    private bool accountsLoadFailed;
    private int loadVersion;
    private int contextVersion;

    public ChroniclePageViewModel(
        IChronicleQueryService chronicleQueryService,
        GetPlayerArchives getPlayerArchives,
        GetGameAccounts getGameAccounts,
        IDomainMetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(chronicleQueryService);
        this.getPlayerArchives = getPlayerArchives ??
            throw new ArgumentNullException(nameof(getPlayerArchives));
        this.getGameAccounts = getGameAccounts ??
            throw new ArgumentNullException(nameof(getGameAccounts));
        this.metadata = metadata ??
            throw new ArgumentNullException(nameof(metadata));
        controller = new ChronicleQueryController(chronicleQueryService);

        TimelineNames = ChronicleProfileOptions.Timelines
            .Select(ChronicleDisplayText.Timeline)
            .ToArray();
        GranularityNames = ChronicleProfileOptions.Granularities
            .Select(ChronicleDisplayText.Granularity)
            .ToArray();
        TimeRangeNames = ChronicleTimeRangeOptions.Presets
            .Select(ChronicleTimeRangeOptions.Name)
            .ToArray();
        DomainNames =
        [
            "全部领域",
            .. ChronicleFilterOptions.Domains.Select(ChronicleDisplayText.Domain),
        ];
        RankNames = ["全部星级", "五星", "四星", "三星"];
    }

    public ObservableCollection<string> ArchiveNames { get; } = [];

    public ObservableCollection<string> AccountNames { get; } = [];

    public ObservableCollection<ChronicleEntryDisplay> Entries { get; } = [];

    public IReadOnlyList<string> TimelineNames { get; }

    public IReadOnlyList<string> GranularityNames { get; }

    public IReadOnlyList<string> TimeRangeNames { get; }

    public IReadOnlyList<string> DomainNames { get; }

    public IReadOnlyList<string> RankNames { get; }

    [ObservableProperty]
    public partial int SelectedArchiveIndex { get; set; } = -1;

    [ObservableProperty]
    public partial int SelectedAccountIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedTimelineIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedGranularityIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedTimeRangeIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedDomainIndex { get; set; }

    [ObservableProperty]
    public partial int SelectedRankIndex { get; set; }

    [ObservableProperty]
    public partial bool IncludeStateChanges { get; set; }

    [ObservableProperty]
    public partial bool IncludeUndated { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanLoadMore))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStaleMessage))]
    public partial string? StaleMessage { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyMessage))]
    public partial string? EmptyMessage { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "正在载入游戏历程…";

    public bool CanLoadMore => !IsBusy && controller.State.HasMore;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasStaleMessage => !string.IsNullOrWhiteSpace(StaleMessage);

    public bool HasEmptyMessage => !string.IsNullOrWhiteSpace(EmptyMessage);

    private PlayerArchive? SelectedArchive =>
        SelectedArchiveIndex >= 0 && SelectedArchiveIndex < archives.Count
            ? archives[SelectedArchiveIndex]
            : null;

    private GameAccount? SelectedAccount =>
        SelectedAccountIndex > 0 && SelectedAccountIndex <= accounts.Count
            ? accounts[SelectedAccountIndex - 1]
            : null;

    [RelayCommand]
    public async Task InitializeAsync()
    {
        await LoadArchivesAsync();
        await ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        if (archiveLoadFailed)
        {
            ShowError("读取本地档案失败，请稍后重试。");
            return;
        }
        if (archives.Count == 0)
        {
            ShowEmpty(ChronicleEmptyReason.NoArchive);
            return;
        }
        if (SelectedArchive is not { } archive)
        {
            ShowEmpty(ChronicleEmptyReason.NoArchive);
            return;
        }
        if (accountsLoadFailed)
        {
            ShowError("读取游戏账号失败，请稍后重试。");
            return;
        }
        if (accounts.Count == 0)
        {
            ShowEmpty(ChronicleEmptyReason.NoAccount);
            return;
        }

        int version = ++loadVersion;
        resetDisplays = true;
        IsBusy = true;
        ErrorMessage = null;
        StaleMessage = null;
        EmptyMessage = null;
        StatusMessage = ChronicleStatusText.DescribeLoadState(
            ChronicleLoadState.LoadingFirstPage,
            0);

        ChronicleQuery query = BuildQuery(archive);
        await controller.LoadFirstPageAsync(query);
        if (version != loadVersion)
        {
            return;
        }

        IsBusy = false;
        await ApplyStateAsync(version);
    }

    [RelayCommand]
    public async Task LoadMoreAsync()
    {
        if (IsBusy || !controller.State.HasMore)
        {
            return;
        }

        int version = ++loadVersion;
        resetDisplays = false;
        IsBusy = true;
        StatusMessage = ChronicleStatusText.DescribeLoadState(
            ChronicleLoadState.LoadingMore,
            controller.State.LoadedCount);
        await controller.LoadMoreAsync();
        if (version != loadVersion)
        {
            return;
        }

        IsBusy = false;
        await ApplyStateAsync(version);
    }

    public void CancelActiveLoad()
    {
        loadVersion++;
        IsBusy = false;
        controller.CancelActive();
        OnPropertyChanged(nameof(CanLoadMore));
    }

    partial void OnSelectedArchiveIndexChanged(int value)
    {
        if (suppressSelectionReload || value < 0)
        {
            return;
        }

        preferredArchiveId = archives.ElementAtOrDefault(value)?.Id;
        preferredAccountId = null;
        _ = ReloadForArchiveAsync();
    }

    partial void OnSelectedAccountIndexChanged(int value)
    {
        if (suppressSelectionReload || value < 0)
        {
            return;
        }

        preferredAccountId = value > 0
            ? accounts.ElementAtOrDefault(value - 1)?.Id
            : null;
        _ = ReloadAsync();
    }

    partial void OnSelectedTimelineIndexChanged(int value) => QueueReload();

    partial void OnSelectedGranularityIndexChanged(int value) => QueueReload();

    partial void OnSelectedTimeRangeIndexChanged(int value) => QueueReload();

    partial void OnSelectedDomainIndexChanged(int value) => QueueReload();

    partial void OnSelectedRankIndexChanged(int value) => QueueReload();

    partial void OnIncludeStateChangesChanged(bool value) => QueueReload();

    partial void OnIncludeUndatedChanged(bool value) => QueueReload();

    private void QueueReload()
    {
        if (suppressSelectionReload)
        {
            return;
        }

        _ = ReloadAsync();
    }

    private async Task ReloadForArchiveAsync()
    {
        await LoadAccountsAsync();
        await ReloadAsync();
    }

    private async Task LoadArchivesAsync()
    {
        int version = ++contextVersion;
        IReadOnlyList<PlayerArchive> loaded;
        try
        {
            loaded = await getPlayerArchives.ExecuteAsync();
        }
        catch (Exception)
        {
            if (version == contextVersion)
            {
                archiveLoadFailed = true;
                archives = [];
                ArchiveNames.Clear();
                SelectedArchiveIndex = -1;
            }
            return;
        }

        if (version != contextVersion)
        {
            return;
        }

        archiveLoadFailed = false;
        archives = loaded;
        suppressSelectionReload = true;
        try
        {
            ArchiveNames.Clear();
            foreach (PlayerArchive archive in archives)
            {
                ArchiveNames.Add(archive.Name);
            }

            int index = 0;
            if (preferredArchiveId is { } preferred)
            {
                int found = IndexOfArchive(preferred);
                if (found >= 0)
                {
                    index = found;
                }
            }
            SelectedArchiveIndex = archives.Count == 0 ? -1 : index;
            preferredArchiveId = archives.Count == 0
                ? null
                : archives[index].Id;
        }
        finally
        {
            suppressSelectionReload = false;
        }

        await LoadAccountsAsync();
    }

    private async Task LoadAccountsAsync()
    {
        if (SelectedArchive is not { } archive)
        {
            accounts = [];
            accountsLoadFailed = false;
            accountLabels.Clear();
            AccountNames.Clear();
            SelectedAccountIndex = -1;
            return;
        }

        IReadOnlyList<GameAccount> loaded;
        try
        {
            loaded = await getGameAccounts.ExecuteAsync(archive.Id);
            accountsLoadFailed = false;
        }
        catch (Exception)
        {
            accountsLoadFailed = true;
            loaded = [];
        }

        accounts = loaded;
        accountLabels.Clear();
        foreach (GameAccount account in accounts)
        {
            accountLabels[account.Id] = ChronicleAccountLabel.Build(account);
        }

        suppressSelectionReload = true;
        try
        {
            AccountNames.Clear();
            AccountNames.Add("全部账号（档案）");
            foreach (GameAccount account in accounts)
            {
                AccountNames.Add(accountLabels[account.Id]);
            }

            int index = 0;
            if (preferredAccountId is { } preferred)
            {
                int found = IndexOfAccount(preferred);
                if (found >= 0)
                {
                    index = found + 1;
                }
            }
            SelectedAccountIndex = index;
            preferredAccountId = index > 0
                ? accounts[index - 1].Id
                : null;
        }
        finally
        {
            suppressSelectionReload = false;
        }
    }

    private int IndexOfArchive(Guid id)
    {
        for (int index = 0; index < archives.Count; index++)
        {
            if (archives[index].Id == id)
            {
                return index;
            }
        }
        return -1;
    }

    private int IndexOfAccount(Guid id)
    {
        for (int index = 0; index < accounts.Count; index++)
        {
            if (accounts[index].Id == id)
            {
                return index;
            }
        }
        return -1;
    }

    private ChronicleQuery BuildQuery(PlayerArchive archive)
    {
        ChronicleScope scope = SelectedAccount is { } account
            ? new ChronicleScope(ChronicleScopeKind.Account, account.Id)
            : new ChronicleScope(ChronicleScopeKind.Archive, archive.Id);

        ChronicleTimeline timeline = ChronicleProfileOptions.Timelines[
            Math.Clamp(
                SelectedTimelineIndex,
                0,
                ChronicleProfileOptions.Timelines.Count - 1)];
        ChronicleGranularity granularity = ChronicleProfileOptions.Granularities[
            Math.Clamp(
                SelectedGranularityIndex,
                0,
                ChronicleProfileOptions.Granularities.Count - 1)];
        bool includeStateChanges = IncludeStateChanges &&
            timeline is ChronicleTimeline.Character or ChronicleTimeline.Weapon;
        ChronicleProfileRequest profile = ChronicleProfileOptions.Build(
            timeline,
            granularity,
            includeStateChanges);

        ChronicleTimeRangePreset preset = ChronicleTimeRangeOptions.Presets[
            Math.Clamp(
                SelectedTimeRangeIndex,
                0,
                ChronicleTimeRangeOptions.Presets.Count - 1)];
        ChronicleTimeRange range = ChronicleTimeRangeOptions.Resolve(
            preset,
            DateTimeOffset.Now,
            TimeZoneInfo.Local);
        ChronicleDomain? domain = SelectedDomainIndex > 0 &&
            SelectedDomainIndex <= ChronicleFilterOptions.Domains.Count
                ? ChronicleFilterOptions.Domains[SelectedDomainIndex - 1]
                : null;
        int? rank = SelectedRankIndex > 0 &&
            SelectedRankIndex <= ChronicleFilterOptions.Ranks.Count
                ? ChronicleFilterOptions.Ranks[SelectedRankIndex - 1]
                : null;
        ChronicleFilter filter = ChronicleFilterOptions.Build(
            range,
            IncludeUndated,
            domain,
            rank);
        return new ChronicleQuery(
            scope,
            profile,
            filter,
            DefaultPageSize);
    }

    private async Task ApplyStateAsync(int version)
    {
        ChroniclePageState state = controller.State;
        if (resetDisplays || state.LoadedCount < Entries.Count)
        {
            Entries.Clear();
        }
        resetDisplays = false;

        if (state.LoadedCount > Entries.Count)
        {
            ChronicleEntry[] pending = state.Entries
                .Skip(Entries.Count)
                .ToArray();
            await PrefetchItemNamesAsync(pending);
            if (version != loadVersion)
            {
                return;
            }

            ChronicleDisplayContext context = BuildContext();
            foreach (ChronicleEntry entry in pending)
            {
                Entries.Add(ChronicleEntryDisplay.FromEntry(entry, context));
            }
        }

        UpdateStatus();
    }

    private async Task PrefetchItemNamesAsync(
        IReadOnlyList<ChronicleEntry> entries)
    {
        foreach (ChronicleEntry entry in entries)
        {
            InventoryItemKind kind;
            string typeId;
            string? stableKey = null;
            if (entry.SummaryArguments.TryGetValue("item_key", out string? key) &&
                ChronicleItemKey.TryParse(key, out kind, out typeId))
            {
                stableKey = key;
            }
            else if (entry.ItemId is { Length: > 0 } itemId)
            {
                kind = (entry.Timelines & ChronicleTimeline.Character) != 0
                    ? InventoryItemKind.Character
                    : InventoryItemKind.Weapon;
                typeId = itemId;
            }
            else
            {
                continue;
            }

            if (itemNames.ContainsKey(typeId) &&
                (stableKey is null || itemNames.ContainsKey(stableKey)))
            {
                continue;
            }

            string? name = null;
            try
            {
                var item = await metadata.ResolveItemAsync(kind, typeId);
                name = item?.Name;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Metadata is optional: an unresolved name must never block
                // the entry itself.
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            itemNames[typeId] = name;
            if (stableKey is not null)
            {
                itemNames[stableKey] = name;
            }
        }
    }

    private ChronicleDisplayContext BuildContext() => new()
    {
        ItemNames = itemNames,
        AccountLabels = accountLabels,
        DisplayTimeZone = TimeZoneInfo.Local,
    };

    private void UpdateStatus()
    {
        ChroniclePageState state = controller.State;
        OnPropertyChanged(nameof(CanLoadMore));
        if (state.RequiresReload)
        {
            StaleMessage = state.ErrorMessage;
            ErrorMessage = null;
            EmptyMessage = null;
            StatusMessage = ChronicleStatusText.DescribeStale(state.StaleReason);
            return;
        }

        StaleMessage = null;
        if (state.LoadState == ChronicleLoadState.Error)
        {
            ErrorMessage = state.ErrorMessage ?? "读取游戏历程失败，请稍后重试。";
            EmptyMessage = null;
            StatusMessage = "读取游戏历程失败。";
            return;
        }

        ErrorMessage = null;
        if (state.LoadState == ChronicleLoadState.Empty)
        {
            EmptyMessage = ChronicleEmptyStateText.Describe(
                DetermineEmptyReason());
            StatusMessage = "当前条件下没有可显示的历程记录。";
            return;
        }

        EmptyMessage = null;
        StatusMessage = ChronicleStatusText.DescribeLoadState(
            state.LoadState,
            state.LoadedCount);
    }

    private ChronicleEmptyReason DetermineEmptyReason()
    {
        if (archives.Count == 0)
        {
            return ChronicleEmptyReason.NoArchive;
        }
        if (SelectedArchive is not null && accounts.Count == 0)
        {
            return ChronicleEmptyReason.NoAccount;
        }
        return HasActiveDataFilter
            ? ChronicleEmptyReason.FilteredOut
            : ChronicleEmptyReason.NoData;
    }

    private bool HasActiveDataFilter =>
        SelectedTimeRangeIndex != 0 ||
        SelectedDomainIndex != 0 ||
        SelectedRankIndex != 0 ||
        !IncludeUndated;

    private void ShowEmpty(ChronicleEmptyReason reason)
    {
        loadVersion++;
        controller.Reset();
        Entries.Clear();
        resetDisplays = true;
        IsBusy = false;
        StaleMessage = null;
        ErrorMessage = null;
        EmptyMessage = ChronicleEmptyStateText.Describe(reason);
        StatusMessage = "当前条件下没有可显示的历程记录。";
        OnPropertyChanged(nameof(CanLoadMore));
    }

    private void ShowError(string message)
    {
        loadVersion++;
        controller.Reset();
        Entries.Clear();
        resetDisplays = true;
        IsBusy = false;
        StaleMessage = null;
        EmptyMessage = null;
        ErrorMessage = message;
        StatusMessage = "读取游戏历程失败。";
        OnPropertyChanged(nameof(CanLoadMore));
    }
}

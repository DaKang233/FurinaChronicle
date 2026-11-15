// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Presentation.History;

public sealed record ChronicleWorkspaceSnapshot
{
    public IReadOnlyList<HistoryChoice> Archives { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public IReadOnlyList<HistoryChoice> Accounts { get; init; } = [];
    public Guid? SelectedAccountId { get; init; }
    public ChronicleTimeline Timeline { get; init; } = ChronicleTimeline.Main;
    public ChronicleGranularity Granularity { get; init; } =
        ChronicleGranularity.Default;
    public ChronicleTimeRangePreset TimeRange { get; init; } =
        ChronicleTimeRangePreset.All;
    public ChronicleDomain? Domain { get; init; }
    public int? Rank { get; init; }
    public bool IncludeStateChanges { get; init; }
    public bool IncludeUndated { get; init; } = true;
    public IReadOnlyList<ChronicleEntryDisplay> Entries { get; init; } = [];
    public string? SelectedEntryId { get; init; }
    public bool IsBusy { get; init; }
    public bool HasMore { get; init; }
    public string Status { get; init; } = "尚未载入游戏历程。";
    public string? StaleMessage { get; init; }
    public string? Error { get; init; }
    public string? EmptyMessage { get; init; }
    public string GenerationId { get; init; } = string.Empty;

    public IReadOnlyList<HistoryChoice> AccountChoices =>
    [
        new HistoryChoice(Guid.Empty, "全部账号（档案）"),
        .. Accounts,
    ];

    public ChronicleEntryDisplay? SelectedEntry =>
        Entries.FirstOrDefault(entry =>
            string.Equals(entry.EntryId, SelectedEntryId, StringComparison.Ordinal));

    public string ScopeLabel => SelectedArchiveId is null
        ? "尚未选择档案"
        : Archives.FirstOrDefault(item =>
            item.Id == SelectedArchiveId)?.Label ?? "所选档案";
}

/// <summary>
/// Page-local Chronicle workspace for the isolated WinUI host. It composes the
/// existing shared query controller (cancellation, cursor paging, generation
/// invalidation) with page-local archive/account selection and filter options.
/// Nothing here resolves a global "current account".
/// </summary>
public sealed class ChronicleWorkspaceState
{
    private const int DefaultPageSize = 30;

    private readonly GetPlayerArchives archives;
    private readonly GetGameAccounts accounts;
    private readonly IDomainMetadataService metadata;
    private readonly ChronicleQueryController controller;
    private readonly Dictionary<string, string> itemNames =
        new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string> accountLabels = [];
    private IReadOnlyList<PlayerArchive> archiveEntities = [];
    private IReadOnlyList<GameAccount> accountEntities = [];
    private Guid? preferredArchiveId;
    private Guid? preferredAccountId;
    private string? preferredEntryId;
    private bool archiveLoadFailed;
    private bool accountLoadFailed;
    private int generation;
    private int contextGeneration;

    public ChronicleWorkspaceState(
        IChronicleQueryService chronicleQueryService,
        GetPlayerArchives archives,
        GetGameAccounts accounts,
        IDomainMetadataService metadata)
    {
        ArgumentNullException.ThrowIfNull(chronicleQueryService);
        this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
        this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        controller = new ChronicleQueryController(chronicleQueryService);
    }

    public ChronicleWorkspaceState(
        IChronicleQueryService chronicleQueryService,
        GetPlayerArchives archives,
        GetGameAccounts accounts)
        : this(
            chronicleQueryService,
            archives,
            accounts,
            NullDomainMetadataService.Instance)
    {
    }

    public ChronicleWorkspaceSnapshot Snapshot { get; private set; } = new();

    public IReadOnlyList<HistoryOption<ChronicleTimeline>> TimelineOptions { get; } =
        ChronicleProfileOptions.Timelines
            .Select(value => new HistoryOption<ChronicleTimeline>(
                value,
                ChronicleDisplayText.Timeline(value)))
            .ToArray();

    public IReadOnlyList<HistoryOption<ChronicleGranularity>> GranularityOptions
    {
        get;
    } = ChronicleProfileOptions.Granularities
        .Select(value => new HistoryOption<ChronicleGranularity>(
            value,
            ChronicleDisplayText.Granularity(value)))
        .ToArray();

    public IReadOnlyList<HistoryOption<ChronicleTimeRangePreset>> TimeRangeOptions
    {
        get;
    } = ChronicleTimeRangeOptions.Presets
        .Select(value => new HistoryOption<ChronicleTimeRangePreset>(
            value,
            ChronicleTimeRangeOptions.Name(value)))
        .ToArray();

    public IReadOnlyList<HistoryOption<ChronicleDomain?>> DomainOptions { get; } =
    [
        new HistoryOption<ChronicleDomain?>(null, "全部领域"),
        .. ChronicleFilterOptions.Domains.Select(value =>
            new HistoryOption<ChronicleDomain?>(
                value,
                ChronicleDisplayText.Domain(value))),
    ];

    public IReadOnlyList<HistoryOption<int?>> RankOptions { get; } =
    [
        new HistoryOption<int?>(null, "全部星级"),
        new HistoryOption<int?>(5, "五星"),
        new HistoryOption<int?>(4, "四星"),
        new HistoryOption<int?>(3, "三星"),
    ];

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await LoadContextAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public async Task SelectArchiveAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        preferredAccountId = null;
        preferredEntryId = null;
        Set(Snapshot with
        {
            SelectedArchiveId = archiveId,
            Accounts = [],
            SelectedAccountId = null,
        });
        await LoadAccountsAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
    }

    public Task SelectAccountAsync(
        Guid? accountId,
        CancellationToken cancellationToken = default)
    {
        preferredAccountId = accountId == Guid.Empty ? null : accountId;
        preferredEntryId = null;
        return ReloadAsync(cancellationToken);
    }

    public Task SetTimelineAsync(
        ChronicleTimeline value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Timeline == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { Timeline = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetGranularityAsync(
        ChronicleGranularity value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Granularity == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { Granularity = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetTimeRangeAsync(
        ChronicleTimeRangePreset value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.TimeRange == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { TimeRange = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetDomainAsync(
        ChronicleDomain? value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Domain == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { Domain = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetRankAsync(
        int? value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Rank == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { Rank = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetIncludeStateChangesAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.IncludeStateChanges == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { IncludeStateChanges = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public Task SetIncludeUndatedAsync(
        bool value,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.IncludeUndated == value)
        {
            return Task.CompletedTask;
        }
        Set(Snapshot with { IncludeUndated = value, SelectedEntryId = null });
        return ReloadAsync(cancellationToken);
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        int request = ++generation;
        if (archiveLoadFailed)
        {
            ShowError("读取本地档案失败，请稍后重试。");
            return;
        }
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            ShowEmpty(ChronicleEmptyReason.NoArchive);
            return;
        }
        if (accountLoadFailed)
        {
            ShowError("读取游戏账号失败，请稍后重试。");
            return;
        }
        if (accountEntities.Count == 0)
        {
            ShowEmpty(ChronicleEmptyReason.NoAccount);
            return;
        }

        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            StaleMessage = null,
            EmptyMessage = null,
            Status = "正在载入游戏历程…",
        });

        ChronicleQuery query = BuildQuery(archiveId);
        await controller.LoadFirstPageAsync(query, cancellationToken);
        if (request != generation)
        {
            return;
        }
        await ApplyAsync(request);
    }

    public async Task LoadMoreAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.IsBusy || !Snapshot.HasMore)
        {
            return;
        }

        int request = ++generation;
        Set(Snapshot with
        {
            IsBusy = true,
            Status = "正在载入更早的历程…",
        });
        await controller.LoadMoreAsync(cancellationToken);
        if (request != generation)
        {
            return;
        }
        await ApplyAsync(request);
    }

    public void SelectEntry(string? entryId)
    {
        preferredEntryId = entryId;
        Set(Snapshot with { SelectedEntryId = entryId });
    }

    public void Cancel()
    {
        generation++;
        controller.CancelActive();
        if (Snapshot.IsBusy)
        {
            Set(Snapshot with { IsBusy = false });
        }
    }

    public string BuildMarkdownReport() =>
        ChronicleMarkdownReport.Write(
            Snapshot.Entries,
            Snapshot.ScopeLabel,
            DateTimeOffset.Now);

    private async Task LoadContextAsync(CancellationToken cancellationToken)
    {
        int request = ++contextGeneration;
        IReadOnlyList<PlayerArchive> loaded;
        try
        {
            loaded = await archives.ExecuteAsync(cancellationToken);
            archiveLoadFailed = false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            archiveLoadFailed = true;
            if (request == contextGeneration)
            {
                archiveEntities = [];
                Set(Snapshot with
                {
                    Archives = [],
                    Accounts = [],
                    SelectedArchiveId = null,
                    SelectedAccountId = null,
                });
            }
            return;
        }
        if (request != contextGeneration)
        {
            return;
        }
        archiveEntities = loaded;
        Guid? archiveId = ResolveArchive(preferredArchiveId);
        IReadOnlyList<HistoryChoice> archiveChoices = archiveEntities
            .Select(item => new HistoryChoice(item.Id, item.Name))
            .ToArray();
        preferredArchiveId = archiveId;
        Set(Snapshot with
        {
            Archives = archiveChoices,
            SelectedArchiveId = archiveId,
        });
        await LoadAccountsAsync(cancellationToken);
    }

    private async Task LoadAccountsAsync(CancellationToken cancellationToken)
    {
        accountEntities = [];
        accountLabels.Clear();
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            Set(Snapshot with
            {
                Accounts = [],
                SelectedAccountId = null,
            });
            return;
        }

        IReadOnlyList<GameAccount> loaded;
        try
        {
            loaded = await accounts.ExecuteAsync(archiveId, cancellationToken);
            accountLoadFailed = false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            accountLoadFailed = true;
            Set(Snapshot with
            {
                Accounts = [],
                SelectedAccountId = null,
            });
            return;
        }

        accountEntities = loaded;
        foreach (GameAccount account in accountEntities)
        {
            accountLabels[account.Id] = ChronicleAccountLabel.Build(account);
        }
        Guid? accountId = ResolveAccount(preferredAccountId);
        preferredAccountId = accountId;
        Set(Snapshot with
        {
            Accounts = accountEntities
                .Select(item => new HistoryChoice(
                    item.Id,
                    accountLabels[item.Id]))
                .ToArray(),
            SelectedAccountId = accountId,
        });
    }

    private ChronicleQuery BuildQuery(Guid archiveId)
    {
        ChronicleScope scope = Snapshot.SelectedAccountId is Guid accountId
            ? new ChronicleScope(ChronicleScopeKind.Account, accountId)
            : new ChronicleScope(ChronicleScopeKind.Archive, archiveId);
        bool includeStateChanges = Snapshot.IncludeStateChanges &&
            Snapshot.Timeline is ChronicleTimeline.Character or
                ChronicleTimeline.Weapon;
        ChronicleProfileRequest profile = ChronicleProfileOptions.Build(
            Snapshot.Timeline,
            Snapshot.Granularity,
            includeStateChanges);
        ChronicleTimeRange range = ChronicleTimeRangeOptions.Resolve(
            Snapshot.TimeRange,
            DateTimeOffset.Now,
            TimeZoneInfo.Local);
        ChronicleFilter filter = ChronicleFilterOptions.Build(
            range,
            Snapshot.IncludeUndated,
            Snapshot.Domain,
            Snapshot.Rank);
        return new ChronicleQuery(scope, profile, filter, DefaultPageSize);
    }

    private async Task ApplyAsync(int request)
    {
        ChroniclePageState state = controller.State;
        IReadOnlyList<ChronicleEntryDisplay> entries = Snapshot.Entries;
        if (state.LoadedCount < entries.Count)
        {
            entries = [];
            preferredEntryId = null;
        }
        if (state.LoadedCount > entries.Count)
        {
            ChronicleEntry[] pending = state.Entries
                .Skip(entries.Count)
                .ToArray();
            await PrefetchItemNamesAsync(pending, request);
            if (request != generation)
            {
                return;
            }
            ChronicleDisplayContext context = BuildContext();
            var merged = entries.ToList();
            foreach (ChronicleEntry entry in pending)
            {
                merged.Add(ChronicleEntryDisplay.FromEntry(entry, context));
            }
            entries = merged;
        }

        string? selectedEntryId = entries.Any(entry =>
                string.Equals(entry.EntryId, preferredEntryId, StringComparison.Ordinal))
            ? preferredEntryId
            : entries.FirstOrDefault()?.EntryId;
        preferredEntryId = selectedEntryId;

        Snapshot = Snapshot with
        {
            Entries = entries,
            SelectedEntryId = selectedEntryId,
            IsBusy = false,
            HasMore = state.HasMore,
            GenerationId = state.GenerationId ?? string.Empty,
            Status = state.LoadState switch
            {
                ChronicleLoadState.LoadingFirstPage => "正在载入游戏历程…",
                ChronicleLoadState.LoadingMore => "正在载入更早的历程…",
                _ => ChronicleStatusText.DescribeLoadState(
                    state.LoadState,
                    state.LoadedCount),
            },
            StaleMessage = state.RequiresReload ? state.ErrorMessage : null,
            Error = state.RequiresReload
                ? null
                : state.LoadState == ChronicleLoadState.Error
                    ? state.ErrorMessage
                    : null,
            EmptyMessage = state.LoadState == ChronicleLoadState.Empty
                ? ChronicleEmptyStateText.Describe(DetermineEmptyReason())
                : null,
        };
    }

    private async Task PrefetchItemNamesAsync(
        IReadOnlyList<ChronicleEntry> entries,
        int request)
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
                // Optional metadata must never block the entry.
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
            if (request != generation)
            {
                return;
            }
        }
    }

    private ChronicleDisplayContext BuildContext() => new()
    {
        ItemNames = itemNames,
        AccountLabels = accountLabels,
        DisplayTimeZone = TimeZoneInfo.Local,
    };

    private ChronicleEmptyReason DetermineEmptyReason()
    {
        if (archiveEntities.Count == 0)
        {
            return ChronicleEmptyReason.NoArchive;
        }
        if (Snapshot.SelectedArchiveId is not null && accountEntities.Count == 0)
        {
            return ChronicleEmptyReason.NoAccount;
        }
        bool filtered = Snapshot.TimeRange != ChronicleTimeRangePreset.All ||
            Snapshot.Domain is not null ||
            Snapshot.Rank is not null ||
            !Snapshot.IncludeUndated;
        return filtered
            ? ChronicleEmptyReason.FilteredOut
            : ChronicleEmptyReason.NoData;
    }

    private Guid? ResolveArchive(Guid? preferred)
    {
        if (preferred is Guid wanted &&
            archiveEntities.Any(item => item.Id == wanted))
        {
            return wanted;
        }
        return archiveEntities.FirstOrDefault()?.Id;
    }

    private Guid? ResolveAccount(Guid? preferred)
    {
        if (preferred is Guid wanted &&
            accountEntities.Any(item => item.Id == wanted))
        {
            return wanted;
        }
        return accountEntities.FirstOrDefault()?.Id;
    }

    private void ShowEmpty(ChronicleEmptyReason reason)
    {
        generation++;
        controller.Reset();
        Set(Snapshot with
        {
            Entries = [],
            SelectedEntryId = null,
            IsBusy = false,
            HasMore = false,
            StaleMessage = null,
            Error = null,
            EmptyMessage = ChronicleEmptyStateText.Describe(reason),
            Status = "当前条件下没有可显示的历程记录。",
        });
    }

    private void ShowError(string message)
    {
        generation++;
        controller.Reset();
        Set(Snapshot with
        {
            Entries = [],
            SelectedEntryId = null,
            IsBusy = false,
            HasMore = false,
            StaleMessage = null,
            EmptyMessage = null,
            Error = message,
            Status = "读取游戏历程失败。",
        });
    }

    private void Set(ChronicleWorkspaceSnapshot snapshot) => Snapshot = snapshot;
}

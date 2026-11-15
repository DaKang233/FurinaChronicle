// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Presentation.History;

public sealed record CharacterWeaponHistorySnapshot
{
    public IReadOnlyList<HistoryChoice> Archives { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public IReadOnlyList<HistoryChoice> Accounts { get; init; } = [];
    public Guid? SelectedAccountId { get; init; }
    public InventoryItemKind Kind { get; init; } = InventoryItemKind.Character;
    public IReadOnlyList<CharacterWeaponObservationView> Observations { get; init; } = [];
    public Guid? SelectedObservationId { get; init; }
    public int Offset { get; init; }
    public int PageSize { get; init; } = CharacterWeaponHistoryState.DefaultPageSize;
    public int TotalCount { get; init; }
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入角色与武器观察。";
    public string? Error { get; init; }
    public string EmptyMessage { get; init; } = string.Empty;

    public bool HasPrevious => Offset > 0;

    public bool HasNext => Offset + PageSize < TotalCount;

    public CharacterWeaponObservationView? SelectedObservation =>
        Observations.FirstOrDefault(item =>
            item.ObservationId == SelectedObservationId);

    public string PageText => TotalCount == 0
        ? "0 次观察"
        : $"{Offset + 1}–{Math.Min(Offset + PageSize, TotalCount)} / {TotalCount} 次观察";
}

/// <summary>
/// Page-local character/weapon observation history. Selection is a stable ID
/// re-queried on every entry; every new selection cancels the previous query and
/// its result can no longer overwrite the current page.
/// </summary>
public sealed class CharacterWeaponHistoryState
{
    public const int DefaultPageSize = 20;

    private readonly IArchiveDomainHistoryStore history;
    private readonly GetPlayerArchives archives;
    private readonly GetGameAccounts accounts;
    private readonly IDomainMetadataService metadata;
    private Guid? preferredArchiveId;
    private Guid? preferredAccountId;
    private Guid? preferredObservationId;
    private InventoryItemKind kind = InventoryItemKind.Character;
    private int offset;
    private int generation;
    private CancellationTokenSource? active;

    public CharacterWeaponHistoryState(
        IArchiveDomainHistoryStore history,
        GetPlayerArchives archives,
        GetGameAccounts accounts,
        IDomainMetadataService metadata)
    {
        this.history = history ?? throw new ArgumentNullException(nameof(history));
        this.archives = archives ?? throw new ArgumentNullException(nameof(archives));
        this.accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        this.metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
    }

    public CharacterWeaponHistoryState(
        IArchiveDomainHistoryStore history,
        GetPlayerArchives archives,
        GetGameAccounts accounts)
        : this(history, archives, accounts, NullDomainMetadataService.Instance)
    {
    }

    public CharacterWeaponHistorySnapshot Snapshot { get; private set; } = new();

    public Task EnterAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(cancellationToken);

    public Task ReloadAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(cancellationToken);

    public Task SelectArchiveAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        preferredAccountId = null;
        preferredObservationId = null;
        offset = 0;
        return LoadAsync(cancellationToken);
    }

    public Task SelectAccountAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        preferredAccountId = accountId;
        preferredObservationId = null;
        offset = 0;
        return LoadAsync(cancellationToken);
    }

    public Task SetKindAsync(
        InventoryItemKind value,
        CancellationToken cancellationToken = default)
    {
        if (kind == value)
        {
            return Task.CompletedTask;
        }
        kind = value;
        preferredObservationId = null;
        offset = 0;
        return LoadAsync(cancellationToken);
    }

    public Task NextPageAsync(CancellationToken cancellationToken = default)
    {
        if (!Snapshot.HasNext)
        {
            return Task.CompletedTask;
        }
        offset += DefaultPageSize;
        return LoadAsync(cancellationToken);
    }

    public Task PreviousPageAsync(CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset - DefaultPageSize);
        return LoadAsync(cancellationToken);
    }

    public void SelectObservation(Guid? observationId)
    {
        preferredObservationId = observationId;
        Set(Snapshot with { SelectedObservationId = observationId });
    }

    public void Cancel()
    {
        generation++;
        active?.Cancel();
        if (Snapshot.IsBusy)
        {
            Set(Snapshot with { IsBusy = false });
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        int request = ++generation;
        CancelActive();
        var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        active = linked;
        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            Status = "正在读取角色与武器观察…",
        });
        try
        {
            IReadOnlyList<PlayerArchive> archiveList =
                await archives.ExecuteAsync(linked.Token);
            if (request != generation)
            {
                return;
            }
            Guid? archiveId = Resolve(
                archiveList.Select(item => item.Id),
                preferredArchiveId);
            IReadOnlyList<HistoryChoice> archiveChoices = archiveList
                .Select(item => new HistoryChoice(
                    item.Id,
                    HistoryLabels.ArchiveChoice(item)))
                .ToArray();

            IReadOnlyList<GameAccount> accountList = archiveId is Guid selectedArchive
                ? await accounts.ExecuteAsync(selectedArchive, linked.Token)
                : [];
            if (request != generation)
            {
                return;
            }
            Guid? accountId = Resolve(
                accountList.Select(item => item.Id),
                preferredAccountId);
            IReadOnlyList<HistoryChoice> accountChoices = accountList
                .Select(item => new HistoryChoice(
                    item.Id,
                    HistoryLabels.AccountLabel(item)))
                .ToArray();

            IReadOnlyList<ItemStateObservation> items = [];
            int total = 0;
            if (accountId is Guid selectedAccount)
            {
                ArchiveDomainPage<ItemStateObservation> page =
                    await history.GetItemObservationPageAsync(
                        new ItemObservationPageQuery(
                            selectedAccount,
                            kind,
                            Offset: offset,
                            Limit: DefaultPageSize),
                        linked.Token);
                items = page.Items;
                total = page.TotalCount;
                if (items.Count == 0 && offset > 0 && total > 0)
                {
                    offset = 0;
                    page = await history.GetItemObservationPageAsync(
                        new ItemObservationPageQuery(
                            selectedAccount,
                            kind,
                            Offset: offset,
                            Limit: DefaultPageSize),
                        linked.Token);
                    items = page.Items;
                    total = page.TotalCount;
                }
            }
            if (request != generation)
            {
                return;
            }

            GameAccount? selectedAccountEntity = accountList.FirstOrDefault(
                item => item.Id == accountId);
            string accountLabel = selectedAccountEntity is null
                ? "未知账号"
                : HistoryLabels.AccountLabel(selectedAccountEntity);
            var views = new List<CharacterWeaponObservationView>(items.Count);
            foreach (ItemStateObservation item in items)
            {
                views.Add(await HistoryObservationViewBuilder.CharacterWeaponAsync(
                    item,
                    metadata,
                    accountLabel,
                    linked.Token));
                if (request != generation)
                {
                    return;
                }
            }

            string kindLabel = HistoryLabels.Kind(kind);
            string empty = archiveChoices.Count == 0
                ? "还没有本地档案。请先在“档案与账号管理”中创建档案。"
                : accountChoices.Count == 0
                    ? "当前档案还没有游戏账号。请先在“档案与账号管理”中添加账号。"
                    : views.Count == 0
                        ? $"该账号还没有{kindLabel}状态观察。"
                        : string.Empty;
            Guid? selectedObservation = views.Any(item =>
                    item.ObservationId == preferredObservationId)
                ? preferredObservationId
                : views.FirstOrDefault()?.ObservationId;
            preferredArchiveId = archiveId;
            preferredAccountId = accountId;
            preferredObservationId = selectedObservation;
            Set(new CharacterWeaponHistorySnapshot
            {
                Archives = archiveChoices,
                SelectedArchiveId = archiveId,
                Accounts = accountChoices,
                SelectedAccountId = accountId,
                Kind = kind,
                Observations = views,
                SelectedObservationId = selectedObservation,
                Offset = offset,
                PageSize = DefaultPageSize,
                TotalCount = total,
                IsBusy = false,
                Status = empty.Length > 0
                    ? empty
                    : $"共 {total} 次{kindLabel}观察；默认显示最新观察，可切换历史观察。",
                Error = null,
                EmptyMessage = empty,
            });
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            if (request == generation)
            {
                Set(Snapshot with { IsBusy = false });
            }
        }
        catch (Exception)
        {
            if (request == generation)
            {
                Set(Snapshot with
                {
                    IsBusy = false,
                    Status = "读取角色与武器观察失败；本地数据未改变。",
                    Error = "读取角色与武器观察失败，请稍后重试。",
                });
            }
        }
        finally
        {
            if (ReferenceEquals(active, linked))
            {
                active = null;
            }
            linked.Dispose();
        }
    }

    private static Guid? Resolve(
        IEnumerable<Guid> available,
        Guid? preferred)
    {
        Guid[] ids = available.ToArray();
        if (preferred is Guid wanted && ids.Contains(wanted))
        {
            return wanted;
        }
        return ids.FirstOrDefault() is Guid first && first != Guid.Empty
            ? first
            : null;
    }

    private void CancelActive()
    {
        CancellationTokenSource? current = active;
        if (current is null)
        {
            return;
        }
        try
        {
            current.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void Set(CharacterWeaponHistorySnapshot snapshot) => Snapshot = snapshot;
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public partial class ArchiveHistoryPageViewModel(
    GetPlayerArchives getPlayerArchives,
    IGachaAtomicChangeStore atomicChangeStore,
    IArchiveOperationHistoryStore operationHistoryStore)
    : ObservableObject
{
    public ObservableCollection<PlayerArchive> Archives { get; } = [];

    public ObservableCollection<GachaOperationHistoryDisplayItem>
        OperationHistory { get; } = [];

    public ObservableCollection<GachaRevisionDisplayItem>
        SelectedOperationRevisions { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial PlayerArchive? SelectedArchive { get; set; }

    [ObservableProperty]
    public partial GachaOperationHistoryDisplayItem? SelectedOperation { get; set; }

    [ObservableProperty]
    public partial int UndoLimit { get; set; } = 3;

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "正在加载修改历史。";

    [ObservableProperty]
    public partial string RevisionStatus { get; set; } = "请选择一项操作查看修订。";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public Task InitializeAsync() => ReloadAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ReloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        Guid? selectedArchiveId = SelectedArchive?.Id;
        await ExecuteBusyAsync(async () =>
        {
            Archives.Clear();
            IReadOnlyList<PlayerArchive> archives =
                await getPlayerArchives.ExecuteAsync();
            foreach (PlayerArchive archive in archives)
            {
                Archives.Add(archive);
            }

            SelectedArchive = selectedArchiveId is null
                ? Archives.FirstOrDefault()
                : Archives.FirstOrDefault(item => item.Id == selectedArchiveId) ??
                    Archives.FirstOrDefault();
            await LoadHistoryCoreAsync();
        });
    }

    public async Task SelectArchiveAsync(PlayerArchive? archive)
    {
        if (IsBusy)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            SelectedArchive = archive;
            await LoadHistoryCoreAsync();
        });
    }

    public async Task LoadSelectedOperationRevisionsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ExecuteBusyAsync(async () =>
        {
            SelectedOperationRevisions.Clear();
            if (SelectedOperation is null)
            {
                RevisionStatus = "请先选择一项操作。";
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
            RevisionStatus = revisions.Count == 0
                ? "此操作没有保留可显示的 Gacha 修订内容。"
                : $"已加载 {revisions.Count} 条修订。";
        });
    }

    public GachaOperationHistoryDisplayItem? GetLatestUndoCandidate() =>
        OperationHistory.FirstOrDefault(item => item.IsUndoEligible);

    public async Task<ArchiveOperationResult> UndoLatestAsync(
        OperationId operationId)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("另一项修改历史操作仍在执行。");
        }

        ArchiveOperationResult? result = null;
        await ExecuteBusyAsync(async () =>
        {
            Guid archiveId = SelectedArchive?.Id ??
                throw new InvalidOperationException("请先选择档案。");
            DateTimeOffset now = DateTimeOffset.Now;
            result = await operationHistoryStore.UndoLatestAsync(
                new ArchiveUndoRequest(
                    operationId,
                    archiveId,
                    now,
                    now));
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                await LoadHistoryCoreAsync();
            }
        }, rethrow: true);
        return result ?? throw new InvalidOperationException("撤销操作未返回结果。");
    }

    public async Task SetUndoLimitAsync(int limit)
    {
        if (IsBusy)
        {
            throw new InvalidOperationException("另一项修改历史操作仍在执行。");
        }

        await ExecuteBusyAsync(async () =>
        {
            Guid archiveId = SelectedArchive?.Id ??
                throw new InvalidOperationException("请先选择档案。");
            UndoLimitChangeResult result =
                await operationHistoryStore.SetUndoLimitAsync(archiveId, limit);
            UndoLimit = result.CurrentLimit;
            StatusMessage = result.EvictedChangeSetIds.Count == 0
                ? $"撤销保留次数已设为 {result.CurrentLimit}。"
                : $"撤销保留次数已设为 {result.CurrentLimit}，" +
                    $"并清理了 {result.EvictedChangeSetIds.Count} 份较早撤销材料。";
            await LoadOperationListCoreAsync(updateStatus: false);
        }, rethrow: true);
    }

    partial void OnSelectedOperationChanged(
        GachaOperationHistoryDisplayItem? value)
    {
        SelectedOperationRevisions.Clear();
        RevisionStatus = value is null
            ? "请选择一项操作查看修订。"
            : "点击“查看所选操作修订”加载修订内容。";
    }

    private async Task LoadHistoryCoreAsync()
    {
        OperationHistory.Clear();
        SelectedOperation = null;
        SelectedOperationRevisions.Clear();
        if (SelectedArchive is null)
        {
            StatusMessage = "当前没有可选择的档案。";
            UndoLimit = 3;
            return;
        }

        UndoLimit = await operationHistoryStore.GetUndoLimitAsync(
            SelectedArchive.Id);
        await LoadOperationListCoreAsync(updateStatus: true);
    }

    private async Task LoadOperationListCoreAsync(bool updateStatus)
    {
        OperationHistory.Clear();
        SelectedOperation = null;
        SelectedOperationRevisions.Clear();
        if (SelectedArchive is null)
        {
            return;
        }

        IReadOnlyList<OperationHistoryItem> history =
            await operationHistoryStore.GetHistoryAsync(SelectedArchive.Id, 30);
        foreach (OperationHistoryItem item in history)
        {
            OperationHistory.Add(
                GachaOperationHistoryDisplayItem.FromDomain(item));
        }
        if (updateStatus)
        {
            StatusMessage = history.Count == 0
                ? $"档案“{SelectedArchive.Name}”尚无可显示的修改操作。"
                : $"档案“{SelectedArchive.Name}”最近 {history.Count} 项操作；" +
                    "撤销仅作用于最新可撤销项。";
        }
    }

    private bool CanRun() => !IsBusy;

    private async Task ExecuteBusyAsync(
        Func<Task> operation,
        bool rethrow = false)
    {
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await operation();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "操作已取消。";
            if (rethrow)
            {
                throw;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
            if (rethrow)
            {
                throw;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}

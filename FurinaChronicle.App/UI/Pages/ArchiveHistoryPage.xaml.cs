// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.App.UI.Pages;

public partial class ArchiveHistoryPage : ContentPage
{
    private bool actionRunning;

    private ArchiveHistoryPageViewModel ViewModel =>
        (ArchiveHistoryPageViewModel)BindingContext;

    public ArchiveHistoryPage(
        ArchiveHistoryPageViewModel viewModel,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.InitializeAsync();
    }

    private async void OnArchiveSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await ViewModel.SelectArchiveAsync(
                picker.SelectedItem as PlayerArchive);
        }
    }

    private async void OnUndoLatestClicked(object? sender, EventArgs e)
    {
        if (actionRunning)
        {
            return;
        }

        actionRunning = true;
        try
        {
            GachaOperationHistoryDisplayItem? candidate =
                ViewModel.GetLatestUndoCandidate();
            if (candidate is null)
            {
                await DisplayAlertAsync(
                    "没有可撤销操作",
                    "当前档案没有仍满足撤销资格的操作。",
                    "确定");
                return;
            }

            string scope = candidate.Item.ArchiveIds.Count > 1
                ? $"此操作同时涉及 {candidate.Item.ArchiveIds.Count} 个档案，撤销会整体作用于这些档案。"
                : "此操作仅涉及当前档案。";
            bool confirmed = await DisplayAlertAsync(
                "撤销最新可撤销操作",
                $"将撤销：{candidate.Operation} · {candidate.Summary}\n{scope}\n若任一事实已变化，整个撤销会安全失败且不写入。",
                "撤销",
                "取消");
            if (!confirmed)
            {
                return;
            }

            GachaAtomicChangeResult result = await ViewModel.UndoLatestAsync(
                OperationId.New());
            await ShowResultAsync(result);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
        finally
        {
            actionRunning = false;
        }
    }

    private async void OnSetUndoLimitClicked(object? sender, EventArgs e)
    {
        if (actionRunning)
        {
            return;
        }

        actionRunning = true;
        try
        {
            await ViewModel.SetUndoLimitAsync(ViewModel.UndoLimit);
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
        finally
        {
            actionRunning = false;
        }
    }

    private async void OnLoadRevisionsClicked(object? sender, EventArgs e)
    {
        try
        {
            await ViewModel.LoadSelectedOperationRevisionsAsync();
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(exception);
        }
    }

    private static Task ShowErrorAsync(Exception exception) =>
        Shell.Current.DisplayAlertAsync("操作失败", exception.Message, "确定");

    private static Task ShowResultAsync(GachaAtomicChangeResult result)
    {
        string message = result.Status switch
        {
            ChangeExecutionStatus.Applied =>
                $"操作已提交，影响 {result.AffectedRecordCount} 条记录。",
            ChangeExecutionStatus.AlreadyCommitted =>
                "此操作此前已经提交，未重复写入。",
            ChangeExecutionStatus.NoOp =>
                "保存结果与当前事实相同，未产生修订。",
            ChangeExecutionStatus.Conflict =>
                result.ConflictReason ?? "当前事实已变化，请重新加载。",
            ChangeExecutionStatus.Suppressed =>
                result.ConflictReason ?? "此记录受永久删除标记抑制。",
            _ => result.ConflictReason ?? result.Status.ToString()
        };
        return Shell.Current.DisplayAlertAsync("操作结果", message, "确定");
    }
}

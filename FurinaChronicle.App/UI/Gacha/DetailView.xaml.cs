// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.App.UI.Gacha;

public partial class DetailView : ContentView
{
    private bool actionRunning;

    private GachaAnalysisViewModel ViewModel =>
        (GachaAnalysisViewModel)BindingContext;

    public DetailView()
    {
        InitializeComponent();
    }

    private async void OnCorrectRecordClicked(object? sender, EventArgs e)
    {
        if (!TryBeginAction(out GachaRecordAnalysisDisplayItem selected))
        {
            return;
        }
        try
        {
            string? name = await Shell.Current.DisplayPromptAsync(
                "纠正抽卡记录",
                "物品名称（留空表示未知）：",
                "下一步",
                "取消",
                initialValue: selected.Record.ItemName ?? string.Empty);
            if (name is null)
            {
                return;
            }
            string? itemId = await Shell.Current.DisplayPromptAsync(
                "纠正抽卡记录",
                "物品 ID（留空表示未知）：",
                "下一步",
                "取消",
                initialValue: selected.Record.ItemId ?? string.Empty);
            if (itemId is null)
            {
                return;
            }
            string? rankText = await Shell.Current.DisplayPromptAsync(
                "纠正抽卡记录",
                "星级（3、4、5；留空表示未知）：",
                "下一步",
                "取消",
                keyboard: Keyboard.Numeric,
                initialValue: selected.Record.RankType?.ToString() ?? string.Empty);
            if (rankText is null)
            {
                return;
            }
            int? rank = string.IsNullOrWhiteSpace(rankText)
                ? null
                : int.TryParse(rankText, out int parsedRank) &&
                    parsedRank is >= 1 and <= 5
                    ? parsedRank
                    : throw new InvalidOperationException(
                        "星级必须是 1～5 的整数或留空。");
            string? timeText = await Shell.Current.DisplayPromptAsync(
                "纠正抽卡记录",
                "发生时间（包含时区，例如 2026-10-08T12:00:00+08:00）：",
                "保存",
                "取消",
                initialValue: selected.Record.Time.ToString("O"));
            if (timeText is null)
            {
                return;
            }
            if (!DateTimeOffset.TryParse(timeText, out DateTimeOffset occurredAt))
            {
                throw new InvalidOperationException(
                    "无法识别发生时间；请保留明确时区。");
            }
            bool confirmed = await Shell.Current.DisplayAlertAsync(
                "确认人工纠正",
                "保存后当前事实来源将标记为 UserEntered；原始事实和来源保留在修订历史中。",
                "保存",
                "取消");
            if (!confirmed)
            {
                return;
            }

            var operationId = FurinaChronicle.Core.History.OperationId.New();
            await ExecuteWithCapacityConfirmationAsync(
                confirmation => ViewModel.CorrectSelectedRecordAsync(
                    operationId,
                    name,
                    itemId,
                    rank,
                    occurredAt,
                    confirmation));
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

    private async void OnDeleteRecordClicked(object? sender, EventArgs e)
    {
        if (!TryBeginAction(out GachaRecordAnalysisDisplayItem selected))
        {
            return;
        }
        try
        {
            bool confirmed = await Shell.Current.DisplayAlertAsync(
                "普通删除抽卡记录",
                $"删除 {selected.Name}（记录 {selected.ExternalRecordId}）？此操作在撤销材料仍保留且当前状态未变化时可以撤销；以后刷新也可能重新取得该记录。",
                "删除",
                "取消");
            if (!confirmed)
            {
                return;
            }
            var operationId = FurinaChronicle.Core.History.OperationId.New();
            await ExecuteWithCapacityConfirmationAsync(
                confirmation => ViewModel.DeleteSelectedRecordAsync(
                    operationId,
                    confirmation));
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

    private async void OnPurgeRecordClicked(object? sender, EventArgs e)
    {
        if (!TryBeginAction(out GachaRecordAnalysisDisplayItem selected))
        {
            return;
        }
        try
        {
            bool confirmed = await Shell.Current.DisplayAlertAsync(
                "永久删除抽卡记录",
                $"永久清除 {selected.Name}（记录 {selected.ExternalRecordId}）的当前值和本机可恢复历史？此操作无法撤销，并会阻止自动刷新静默恢复该已知记录；导出文件、其他档案、其他设备和云端备份不受影响。",
                "永久删除",
                "取消");
            if (!confirmed)
            {
                return;
            }
            GachaAtomicChangeResult result =
                await ViewModel.IrreversiblyDeleteSelectedRecordAsync(
                    FurinaChronicle.Core.History.OperationId.New());
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
                await Shell.Current.DisplayAlertAsync(
                    "没有可撤销操作",
                    "当前档案没有仍满足撤销资格的操作。",
                    "确定");
                return;
            }
            string scope = candidate.Item.ArchiveIds.Count > 1
                ? $"此操作同时涉及 {candidate.Item.ArchiveIds.Count} 个档案，撤销会整体作用于这些档案。"
                : "此操作仅涉及当前档案。";
            bool confirmed = await Shell.Current.DisplayAlertAsync(
                "撤销最新可撤销操作",
                $"将撤销：{candidate.Operation} · {candidate.Summary}\n{scope}\n若任一事实已变化，整个撤销会安全失败且不写入。",
                "撤销",
                "取消");
            if (!confirmed)
            {
                return;
            }
            GachaAtomicChangeResult result = await ViewModel.UndoLatestAsync(
                FurinaChronicle.Core.History.OperationId.New());
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

    private bool TryBeginAction(
        out GachaRecordAnalysisDisplayItem selected)
    {
        selected = ViewModel.SelectedDetailRecord!;
        if (actionRunning)
        {
            return false;
        }
        if (ViewModel.SelectedDetailRecord is null)
        {
            _ = Shell.Current.DisplayAlertAsync(
                "请先选择记录",
                "请在明细表格中选择一条抽卡记录。",
                "确定");
            return false;
        }
        actionRunning = true;
        return true;
    }

    private async Task ExecuteWithCapacityConfirmationAsync(
        Func<Guid?, Task<GachaAtomicChangeResult>> execute)
    {
        GachaAtomicChangeResult result = await execute(null);
        if (result.Status ==
                FurinaChronicle.Core.History.ChangeExecutionStatus.NeedsConfirmation &&
            result.CleanupPlan is not null)
        {
            HistoryCleanupPlan plan = result.CleanupPlan;
            bool confirmed = await Shell.Current.DisplayAlertAsync(
                "需要清理较早撤销材料",
                $"本次原子保存空间不足。继续将清理 {plan.Candidates.Count} 份较早的撤销材料（约 {plan.ReclaimableBytes} 字节），但不会删除修订历史。",
                "清理并继续",
                "取消");
            if (!confirmed)
            {
                return;
            }
            result = await execute(plan.ConfirmationId);
        }
        await ShowResultAsync(result);
    }

    private static Task ShowErrorAsync(Exception exception) =>
        Shell.Current.DisplayAlertAsync("操作失败", exception.Message, "确定");

    private static Task ShowResultAsync(GachaAtomicChangeResult result)
    {
        string message = result.Status switch
        {
            FurinaChronicle.Core.History.ChangeExecutionStatus.Applied =>
                $"操作已提交，影响 {result.AffectedRecordCount} 条记录。",
            FurinaChronicle.Core.History.ChangeExecutionStatus.AlreadyCommitted =>
                "此操作此前已经提交，未重复写入。",
            FurinaChronicle.Core.History.ChangeExecutionStatus.NoOp =>
                "保存结果与当前事实相同，未产生修订。",
            FurinaChronicle.Core.History.ChangeExecutionStatus.Conflict =>
                result.ConflictReason ?? "当前事实已变化，请重新加载。",
            FurinaChronicle.Core.History.ChangeExecutionStatus.Suppressed =>
                result.ConflictReason ?? "此记录受永久删除标记抑制。",
            _ => result.ConflictReason ?? result.Status.ToString()
        };
        return Shell.Current.DisplayAlertAsync("操作结果", message, "确定");
    }
}

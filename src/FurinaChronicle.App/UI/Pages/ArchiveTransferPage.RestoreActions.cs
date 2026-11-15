// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.App.UI.Pages;

public partial class ArchiveTransferPage
{
    private async void OnRetryPreviewClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在重新校验目标状态并生成预览。"))
        {
            return;
        }
        try
        {
            await ViewModel.SetApplyPortableSettingsAsync(
                ViewModel.ApplyPortableSettings);
            if (!await ResolveMappingsAsync())
            {
                ViewModel.CompleteOperation(
                    "已取消映射选择；数据库未改变。");
                return;
            }
            ViewModel.CompleteOperation(
                ViewModel.CurrentPlan?.CanApply == true
                    ? "预览已更新，可以确认提交。"
                    : "预览已更新，仍存在阻塞项。");
        }
        catch (Exception exception)
        {
            ViewModel.CompleteOperation(
                "无法重新生成预览；数据库未改变。",
                exception.Message);
        }
    }

    private async void OnApplyPortableSettingsToggled(
        object? sender,
        ToggledEventArgs e)
    {
        if (changingSettings || !ViewModel.HasPlan || ViewModel.IsBusy)
        {
            return;
        }
        changingSettings = true;
        ViewModel.TryBeginOperation("正在按设置选择更新预览。");
        try
        {
            await ViewModel.SetApplyPortableSettingsAsync(e.Value);
            ViewModel.CompleteOperation(
                "设置差异预览已更新；数据库未改变。");
        }
        catch (Exception exception)
        {
            ViewModel.CompleteOperation(
                "无法更新设置差异预览。",
                exception.Message);
        }
        finally
        {
            changingSettings = false;
        }
    }

    private async void OnApplyRestoreClicked(object? sender, EventArgs e)
    {
        ArchiveRestorePlan? plan = ViewModel.CurrentPlan;
        if (plan is null || ViewModel.IsBusy)
        {
            return;
        }

        ArchiveRestoreConfirmationRequirement[] pending =
            plan.Confirmations.Where(item => !item.Satisfied).ToArray();
        if (pending.Length > 0)
        {
            string warnings = string.Join(
                Environment.NewLine,
                pending.Select(item => $"• {item.Message}"));
            bool confirmed = await DisplayAlertAsync(
                plan.Mode == ArchiveRestoreMode.Replace
                    ? "确认破坏性 Scoped Replace"
                    : "确认 Archive Merge",
                warnings + Environment.NewLine +
                "确认只适用于当前预览和当前 marker/version；目标变化后必须重新预览。",
                "接受并继续",
                "取消");
            if (!confirmed)
            {
                ViewModel.CompleteOperation(
                    "已在确认阶段取消；数据库未改变。");
                return;
            }
            await ViewModel.AcceptPendingConfirmationsAsync();
            plan = ViewModel.CurrentPlan;
        }

        if (plan?.CanApply != true)
        {
            await DisplayAlertAsync(
                "无法执行恢复",
                "预览仍存在未解决映射、冲突或确认项。",
                "确定");
            return;
        }

        if (!ViewModel.TryBeginOperation(
            "正在执行单一跨领域原子恢复；提交期间禁止其他操作。"))
        {
            return;
        }

        try
        {
            ArchiveRestoreApplyResult result =
                await ViewModel.ApplyRestoreAsync();
            if (result.Status == ChangeExecutionStatus.NeedsConfirmation &&
                result.CleanupPlan is HistoryCleanupPlan cleanup)
            {
                bool allowCleanup = await DisplayAlertAsync(
                    "撤销材料空间不足",
                    $"本次操作需要 {cleanup.RequiredBytes} 字节，" +
                    $"当前可用 {cleanup.AvailableBytes} 字节；" +
                    $"清理计划可回收 {cleanup.ReclaimableBytes} 字节。" +
                    Environment.NewLine +
                    "只清理计划列出的较早撤销材料，不删除当前业务事实。",
                    "允许清理并重试",
                    "取消");
                if (!allowCleanup)
                {
                    ViewModel.CompleteOperation(
                        "已拒绝清理撤销材料；恢复未提交。");
                    return;
                }
                result = await ViewModel.ApplyRestoreAsync(
                    cleanup.ConfirmationId);
            }

            string resultText =
                ArchiveTransferPageViewModel.FormatApplyResult(result);
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.NoOp or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                try
                {
                    await ViewModel.RefreshAfterRestoreAsync();
                    ViewModel.CompleteOperation(resultText);
                }
                catch (Exception refreshException)
                {
                    ViewModel.CompleteOperation(
                        resultText +
                        " 业务提交已完成，但页面刷新失败；请重新进入相关业务页。",
                        refreshException.Message);
                }
                await ViewModel.ReleaseRestoreSessionAsync();
                await DisplayAlertAsync("恢复结果", resultText, "确定");
                return;
            }

            ViewModel.CompleteOperation(
                "恢复未提交；数据库保持提交前状态。",
                resultText);
            await DisplayAlertAsync("恢复未提交", resultText, "确定");
        }
        catch (Exception exception)
        {
            ViewModel.CompleteOperation(
                "未找到持久化 Receipt，当前结果未知；请保留页面并使用相同预览重试，或重新进入后检查业务数据。",
                exception.Message);
            await DisplayAlertAsync(
                "恢复结果未知",
                "提交调用未返回，且当前未能查询到持久化 Receipt。" +
                Environment.NewLine + exception.Message,
                "确定");
        }
    }

    private async void OnCancelRestoreClicked(object? sender, EventArgs e)
    {
        if (ViewModel.IsBusy)
        {
            return;
        }
        await ViewModel.ReleaseRestoreSessionAsync();
        ViewModel.CompleteOperation(
            "当前预览会话已释放；数据库未改变。");
    }

    private async void OnOpenGachaExportsClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is AppShell shell)
        {
            await shell.ShowGachaPageAsync();
        }
    }
}

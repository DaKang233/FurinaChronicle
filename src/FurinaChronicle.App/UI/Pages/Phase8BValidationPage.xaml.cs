// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Exporting;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.App.UI.Pages;

public partial class Phase8BValidationPage : ContentPage
{
    private Phase8BValidationPageViewModel ViewModel =>
        (Phase8BValidationPageViewModel)BindingContext;

    public Phase8BValidationPage(Phase8BValidationPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.InitializeAsync();
    }

    private async void OnImportClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在选择领域 Portable JSON。"))
        {
            return;
        }

        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "选择 Furina 领域 Portable JSON",
                    FileTypes = JsonFileTypes()
                });
            if (file is null)
            {
                ViewModel.CompleteOperation("已取消选择文件。", error: null);
                return;
            }

            Guid? targetArchiveId = null;
            if (ViewModel.SelectedArchive is not null)
            {
                const string automatic = "按来源档案名称自动匹配／创建";
                const string selected = "导入到当前所选验收档案";
                string? target = await DisplayActionSheetAsync(
                    "选择导入目标",
                    "取消",
                    destruction: null,
                    automatic,
                    selected);
                if (target is null or "取消")
                {
                    ViewModel.CompleteOperation("已取消导入。", error: null);
                    return;
                }
                if (target == selected)
                {
                    targetArchiveId = ViewModel.SelectedArchive.Id;
                }
            }

            await using Stream source = await file.OpenReadAsync();
            Phase8BValidationImportSession session =
                await ViewModel.PrepareImportAsync(source, targetArchiveId);
            string preview = Phase8BValidationPageViewModel.FormatPlan(
                session.Package,
                session.Plan);
            if (session.Plan.ConflictCount > 0)
            {
                await DisplayAlertAsync(
                    "Preview 存在冲突",
                    preview + Environment.NewLine +
                    "数据未写入。请更换目标或修复冲突后重新预览。",
                    "确定");
                ViewModel.CompleteOperation(
                    "Preview 被冲突阻止；数据库未改变。",
                    error: null);
                return;
            }

            bool confirmed = await DisplayAlertAsync(
                "确认领域 Portable 导入",
                preview + Environment.NewLine +
                "确认后将调用正式原子 Apply。",
                "导入",
                "取消");
            if (!confirmed)
            {
                ViewModel.CompleteOperation(
                    "已在 Preview 后取消；数据库未改变。",
                    error: null);
                return;
            }

            ArchiveOperationResult result =
                await ViewModel.ApplyImportAsync(session);
            if (result.Status == ChangeExecutionStatus.NeedsConfirmation &&
                result.CleanupPlan is HistoryCleanupPlan cleanup)
            {
                bool allowCleanup = await DisplayAlertAsync(
                    "撤销材料空间不足",
                    $"本次操作需要 {cleanup.RequiredBytes} 字节，" +
                    $"当前可用 {cleanup.AvailableBytes} 字节。" +
                    $"允许清理较早撤销材料可回收 {cleanup.ReclaimableBytes} 字节。" +
                    Environment.NewLine +
                    "只会清理计划列出的较早撤销材料，不删除当前业务事实。",
                    "允许清理并重试",
                    "取消");
                if (!allowCleanup)
                {
                    ViewModel.CompleteOperation(
                        "已拒绝清理较早撤销材料；导入未提交。",
                        error: null);
                    return;
                }
                result = await ViewModel.ApplyImportAsync(
                    session,
                    cleanup.ConfirmationId);
            }

            string resultText =
                Phase8BValidationPageViewModel.FormatResult(result);
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.NoOp or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                await ViewModel.RefreshAfterImportAsync(session);
                await DisplayAlertAsync("导入结果", resultText, "确定");
                ViewModel.CompleteOperation(resultText, error: null);
                return;
            }

            await DisplayAlertAsync("导入未提交", resultText, "确定");
            ViewModel.CompleteOperation(
                "导入未提交；数据库保持原状态。",
                resultText);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "领域 Portable 导入失败",
                exception.Message,
                "确定");
            ViewModel.CompleteOperation("导入失败。", exception.Message);
        }
    }

    private async void OnExportPortableClicked(object? sender, EventArgs e)
    {
        await ExportAsync(markdown: false);
    }

    private async void OnExportMarkdownClicked(object? sender, EventArgs e)
    {
        await ExportAsync(markdown: true);
    }

    private async Task ExportAsync(bool markdown)
    {
        if (!ViewModel.TryBeginOperation(
                markdown
                    ? "正在生成 Markdown 验收报告。"
                    : "正在生成领域 Portable JSON。"))
        {
            return;
        }

        try
        {
            Phase8BValidationExportArtifact artifact = markdown
                ? await ViewModel.CreateMarkdownExportAsync()
                : await ViewModel.CreatePortableExportAsync();
            await using (artifact.Content)
            {
                ExportSaveResult saved =
                    await PlatformExportFileSaver.Default.SaveAsync(
                        artifact.FileName,
                        artifact.Content);
                if (saved.IsCanceled)
                {
                    ViewModel.CompleteOperation(
                        "已取消选择保存位置；未报告导出成功。",
                        error: null);
                    return;
                }
                if (!saved.IsSuccessful)
                {
                    throw saved.Exception ??
                        new IOException("系统文件提供器未能保存导出文件。");
                }

                string location = OperatingSystem.IsAndroid()
                    ? "已交给系统文档提供器保存"
                    : saved.FilePath ?? "保存位置由系统管理";
                string summary =
                    $"已导出 {artifact.FileName}；{location}；" +
                    $"{artifact.Content.Length} 字节。";
                await DisplayAlertAsync("导出完成", summary, "确定");
                ViewModel.CompleteOperation(summary, error: null);
            }
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("导出失败", exception.Message, "确定");
            ViewModel.CompleteOperation("导出失败。", exception.Message);
        }
    }

    private static FilePickerFileType JsonFileTypes() => new(
        new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".json"],
            [DevicePlatform.Android] =
                ["application/json", "text/json", "application/octet-stream"]
        });
}

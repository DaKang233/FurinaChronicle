// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.App.Exporting;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.App.UI.Pages;

public partial class ArchiveTransferPage : ContentPage
{
    private const string CancelOption = "取消";
    private bool changingSettings;

    private ArchiveTransferPageViewModel ViewModel =>
        (ArchiveTransferPageViewModel)BindingContext;

    public ArchiveTransferPage(
        ArchiveTransferPageViewModel viewModel,
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
        ViewModel.PropertyChanged -= OnBusyChangedWhileAway;
        if (ViewModel.IsBusy)
        {
            return;
        }
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            ViewModel.CompleteOperation(
                "无法读取备份与恢复状态。",
                exception.Message);
        }
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        ViewModel.PropertyChanged -= OnBusyChangedWhileAway;
        if (ViewModel.IsBusy)
        {
            ViewModel.PropertyChanged += OnBusyChangedWhileAway;
            return;
        }
        await ViewModel.ReleaseInactiveSessionAsync();
    }

    private async void OnBusyChangedWhileAway(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchiveTransferPageViewModel.IsBusy) && !ViewModel.IsBusy)
        {
            ViewModel.PropertyChanged -= OnBusyChangedWhileAway;
            await ViewModel.ReleaseInactiveSessionAsync();
        }
    }

    private async void OnExportArchiveClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在创建一致的完整逻辑 Archive。"))
        {
            return;
        }
        await ExportArtifactAsync(
            () => ViewModel.CreateArchiveExportAsync(),
            "完整逻辑 Archive");
    }

    private async void OnExportDomainPortableClicked(
        object? sender,
        EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在创建分类 Portable。"))
        {
            return;
        }
        await ExportArtifactAsync(
            () => ViewModel.CreateDomainExportAsync(markdown: false),
            "分类 Portable");
    }

    private async void OnExportDomainMarkdownClicked(
        object? sender,
        EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在创建分类 Markdown。"))
        {
            return;
        }
        await ExportArtifactAsync(
            () => ViewModel.CreateDomainExportAsync(markdown: true),
            "分类 Markdown");
    }

    private async void OnExportGachaSummaryClicked(
        object? sender,
        EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在创建抽卡 Markdown 摘要。"))
        {
            return;
        }
        await ExportArtifactAsync(
            () => ViewModel.CreateGachaMarkdownAsync(includeDetails: false),
            "抽卡 Markdown 摘要");
    }

    private async void OnExportGachaDetailsClicked(
        object? sender,
        EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在创建抽卡 Markdown 明细。"))
        {
            return;
        }
        await ExportArtifactAsync(
            () => ViewModel.CreateGachaMarkdownAsync(includeDetails: true),
            "抽卡 Markdown 明细");
    }

    private async Task ExportArtifactAsync(
        Func<Task<ArchiveTransferExportArtifact>> create,
        string title)
    {
        try
        {
            await using ArchiveTransferExportArtifact artifact = await create();
            await using var source = new FileStream(
                artifact.TemporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ExportSaveResult saved =
                await PlatformExportFileSaver.Default.SaveAsync(
                    artifact.FileName,
                    source);
            if (saved.IsCanceled)
            {
                ViewModel.CompleteOperation(
                    "已取消选择保存位置；临时导出已清理。");
                return;
            }
            if (!saved.IsSuccessful)
            {
                throw saved.Exception ??
                    new IOException("系统文件提供器未能保存导出文件。");
            }

            string location = string.IsNullOrWhiteSpace(saved.FilePath)
                ? "由系统文档提供器管理"
                : saved.FilePath;
            string message = $"{artifact.Summary}{Environment.NewLine}位置：{location}";
            ViewModel.CompleteOperation($"{title}已保存。");
            await DisplayAlertAsync($"{title}完成", message, "确定");
        }
        catch (Exception exception)
        {
            ViewModel.CompleteOperation($"{title}失败。", exception.Message);
            await DisplayAlertAsync($"{title}失败", exception.Message, "确定");
        }
    }

    private async void OnOpenArchiveClicked(object? sender, EventArgs e)
    {
        if (!ViewModel.TryBeginOperation("正在选择完整逻辑 Archive。"))
        {
            return;
        }

        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "选择 Furina 完整逻辑 Archive",
                    FileTypes = ArchiveFileTypes()
                });
            if (file is null)
            {
                ViewModel.CompleteOperation("已取消选择文件。");
                return;
            }

            await using (Stream source = await file.OpenReadAsync())
            {
                await ViewModel.StageRestoreAsync(source);
            }

            const string mergeOption = "Merge：补充／合并，不删除目标事实";
            const string replaceOption = "Scoped Replace：替换明确选择的范围";
            string? selectedMode = await DisplayActionSheetAsync(
                "选择恢复方式",
                CancelOption,
                destruction: null,
                mergeOption,
                replaceOption);
            if (selectedMode is null or CancelOption)
            {
                await ViewModel.ReleaseRestoreSessionAsync();
                ViewModel.CompleteOperation(
                    "已在预览前取消；数据库未改变。");
                return;
            }

            if (selectedMode == replaceOption)
            {
                ArchiveReplaceSelection? selection =
                    await SelectReplaceScopeAsync();
                if (selection is null)
                {
                    await ViewModel.ReleaseRestoreSessionAsync();
                    ViewModel.CompleteOperation(
                        "未选择 Replace 范围；数据库未改变。");
                    return;
                }
                await ViewModel.PlanRestoreAsync(
                    ArchiveRestoreMode.Replace,
                    selection);
            }
            else
            {
                await ViewModel.PlanRestoreAsync(ArchiveRestoreMode.Merge);
            }

            if (!await ResolveMappingsAsync())
            {
                ViewModel.CompleteOperation(
                    "已取消映射选择；数据库未改变。");
                return;
            }

            ViewModel.CompleteOperation(
                ViewModel.CurrentPlan?.CanApply == true
                    ? "完整预览已生成，可以确认提交。"
                    : "完整预览已生成；请处理冲突或确认项。");
        }
        catch (Exception exception)
        {
            await ViewModel.ReleaseRestoreSessionAsync();
            ViewModel.CompleteOperation(
                "Archive 校验或预览失败；数据库未改变。",
                exception.Message);
            await DisplayAlertAsync(
                "无法预览 Archive",
                exception.Message,
                "确定");
        }
    }
}

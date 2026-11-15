// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private GachaImportCoordinator? importCoordinator;
    private GachaImportModeState? importMode;
    private CancellationTokenSource? importCancellation;
    private Task? importFlowTask;
    private bool importBusy;
    private bool entered;

    private void EnsureImportComposition(WindowsHostEnvironment preview)
    {
        importMode ??= new GachaImportModeState();
        importCoordinator ??= new GachaImportCoordinator(
            preview.GachaImportReader,
            new ServiceGachaUigfImporter(preview.GachaImporter),
            new ServiceGachaImportPlanningSource(
                new GetPlayerArchives(preview.Archives),
                new GetGameAccounts(preview.Archives, preview.Accounts)));
    }

    private void RenderImportState()
    {
        if (importMode is null) return;
        ImportModeButton.Content = importMode.Label;
        AutomationProperties.SetName(
            ImportModeButton,
            $"导入模式（当前：{importMode.Label}）；点击切换");
        AutomationProperties.SetHelpText(ImportModeButton, importMode.Description);
        ImportModeDescription.Text = importMode.IsFull
            ? $"导入模式：{importMode.Label} · {importMode.Description}"
            : $"导入模式：{importMode.Label} · {importMode.Description} " +
                "切换为全量导入时只覆盖来源明确包含的同记录并保留修订；" +
                "两种模式都不会删除来源缺失的本地记录。";
    }

    private void UpdateCommandAvailability()
    {
        GachaReadOnlySnapshot? snapshot = state?.Snapshot;
        bool reading = snapshot?.IsBusy == true;
        bool idle = importFlowTask is null && exportTask is null &&
            exportCancellation is null && dataOperationTask is null;
        ArchivePicker.IsEnabled = idle && !reading;
        AccountPicker.IsEnabled = idle && !reading;
        RangeButton.IsEnabled = idle && !reading && snapshot is { Accounts.Count: > 0 };
        FilterButton.IsEnabled = idle && !reading;
        ViewPicker.IsEnabled = idle && !reading;
        ReloadButton.IsEnabled = idle;
        CancelButton.IsEnabled = reading && !importBusy && importFlowTask is null;
        bool calendar = snapshot?.View == GachaReadOnlyView.Calendar;
        PreviousButton.IsEnabled = idle && snapshot is not null &&
            (calendar ? !reading && snapshot.VisibleMonth.HasValue : snapshot.CanPrevious);
        NextButton.IsEnabled = idle && snapshot is not null &&
            (calendar ? !reading && snapshot.VisibleMonth.HasValue : snapshot.CanNext);
        ImportButton.IsEnabled = idle && !reading && snapshot is { ArchiveId: not null };
        ImportModeButton.IsEnabled = idle && !reading;
        UpdateExportAvailability();
        UpdateDataOperationAvailability();
    }

    private void OnToggleImportMode(object sender, RoutedEventArgs e)
    {
        if (importBusy || importFlowTask is not null || importMode is null ||
            dataOperationTask is not null || state?.Snapshot.IsBusy == true) return;
        importMode.Toggle();
        RenderImportState();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (importFlowTask is not null || dataOperationTask is not null) return;
        Task flow = RunImportAsync();
        importFlowTask = flow;
        UpdateCommandAvailability();
        try { await flow; }
        finally
        {
            importFlowTask = null;
            importBusy = false;
            UpdateCommandAvailability();
        }
    }

    private async Task RunImportAsync()
    {
        if (importCoordinator is null || importMode is null || state is null ||
            filePicker is null || state.Snapshot is not
            {
                IsBusy: false,
                ArchiveId: Guid archiveId
            } snapshot)
        {
            ShowInlineValidation("请先选择档案并等待当前读取结束，再导入抽卡文件。");
            return;
        }

        ClearInlineValidation();
        // Mode and target IDs are fixed before the file picker; the confirmation page only
        // re-displays this snapshot. A changed target always discards the previous preview.
        GachaImportMode mode = importMode.Snapshot();
        using var cancellation = new CancellationTokenSource();
        importCancellation = cancellation;
        try
        {
            StorageFile? file = await PickImportFileAsync();
            if (file is null) return;

            await ImportPreparedSourceAsync(
                CreateImportSource(file),
                snapshot,
                archiveId,
                mode,
                cancellation.Token,
                NotificationTaskKeys.GachaImportUigf,
                "导入抽卡文件");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Leaving the page may cancel the read/preview phase; nothing was written.
            ClearProgress(NotificationTaskKeys.GachaImportUigf);
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaImportUigf,
                "gacha.import.uigf",
                "导入失败",
                "导入未完成；未确认任何记录被修改，请重新载入后再试。",
                exception);
        }
        finally
        {
            importCancellation = null;
        }
    }

    // Shared by the file picker flow and the Teyvat Helper manual flow: an already materialized
    // import source goes through read -> target -> preview -> atomic commit, with the display
    // surface supplied by the caller.
    private async Task ImportPreparedSourceAsync(
        GachaImportSource source,
        GachaReadOnlySnapshot snapshot,
        Guid archiveId,
        GachaImportMode mode,
        CancellationToken cancellationToken,
        string taskKey,
        string operationTitle,
        GachaImportTargetRequest? preselectedTarget = null)
    {
        if (importCoordinator is null) return;
        importBusy = true;
        UpdateCommandAvailability();
        try
        {
            PublishProgress(taskKey, operationTitle, "正在读取并校验导入文件…");
            GachaImportSourceReadResult read = await importCoordinator.ReadSourceAsync(
                source, cancellationToken);
            if (!entered)
            {
                ClearProgress(taskKey);
                return;
            }
            if (!read.Succeeded || read.Summary is null)
            {
                ReportFailure(
                    taskKey,
                    taskKey,
                    "导入失败",
                    read.FailureMessage ?? "无法读取所选文件，未写入任何数据。");
                return;
            }

            GachaImportPlan? plan = null;
            GachaImportTargetRequest? target = preselectedTarget;
            while (true)
            {
                if (!entered)
                {
                    ClearProgress(taskKey);
                    return;
                }
                target ??= await ChooseImportTargetAsync(snapshot, archiveId);
                if (target is null)
                {
                    ClearProgress(taskKey);
                    return;
                }

                PublishProgress(taskKey, operationTitle, "正在生成导入预览…");
                GachaImportPlanResult planned = await importCoordinator.PlanAsync(
                    read.Summary, target, mode, cancellationToken);
                if (!planned.Succeeded || planned.Plan is null)
                {
                    ReportFailure(
                        taskKey,
                        taskKey,
                        "导入预览失败",
                        planned.FailureMessage ?? "无法生成导入预览，未写入任何数据。");
                    return;
                }

                plan = planned.Plan;
                if (!plan.CanExecute)
                {
                    ReportFailure(
                        taskKey,
                        taskKey,
                        "导入被阻止",
                        plan.BlockMessage ?? "导入目标与文件不匹配，未写入任何数据。");
                    return;
                }

                ContentDialogResult decision = await ShowImportPreviewAsync(plan);
                if (decision == ContentDialogResult.Primary) break;
                if (decision != ContentDialogResult.Secondary)
                {
                    ClearProgress(taskKey);
                    return;
                }

                // "更改目标" invalidates the preview: the loop re-plans from the same file
                // summary before any confirmation can be shown again.
                target = null;
            }

            PublishProgress(taskKey, operationTitle, "正在保存导入结果…");
            // The atomic commit is not cancelled by page navigation. Closing the window waits
            // for this task instead of interrupting an in-flight transaction.
            GachaImportExecution execution = await importCoordinator.ExecuteAsync(
                plan, CancellationToken.None);
            if (!execution.IsSuccess)
            {
                ReportFailure(
                    taskKey,
                    taskKey,
                    "导入失败",
                    execution.Message);
                return;
            }

            string message = execution.Result is { } result
                ? $"已导入 {result.ImportedCount} 条记录。新增 {result.ImportedCount} 条、" +
                    $"更新 {result.UpdatedCount} 条、" +
                    $"跳过 {result.DuplicateCount + result.IgnoredCount} 条、" +
                    $"冲突/无效 {result.InvalidCount} 条。"
                : execution.Message;
            await PublishCommittedResultAsync(taskKey, "导入完成", message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ClearProgress(taskKey);
        }
        catch (Exception exception)
        {
            ReportFailure(
                taskKey,
                taskKey,
                "导入失败",
                "导入未完成；未确认任何记录被修改，请重新载入后再试。",
                exception);
        }
        finally
        {
            importBusy = false;
            UpdateCommandAvailability();
        }
    }

    private async Task<StorageFile?> PickImportFileAsync()
    {
        if (filePicker is null) return null;
        return await filePicker.PickOpenFileAsync(".json");
    }

    private static GachaImportSource CreateImportSource(StorageFile file)
    {
        if (!string.IsNullOrWhiteSpace(file.Path) && Path.IsPathFullyQualified(file.Path))
        {
            return GachaImportSource.FromFile(file.Path);
        }

        return new GachaImportSource(file.Name, async _ => await file.OpenStreamForReadAsync());
    }

    private async Task<GachaImportTargetRequest?> ChooseImportTargetAsync(
        GachaReadOnlySnapshot snapshot,
        Guid archiveId)
    {
        string archiveLabel = snapshot.Archives.FirstOrDefault(archive => archive.Id == archiveId)?.Label
            ?? "当前档案";
        GachaScopeOption? account = snapshot.AccountId is Guid accountId
            ? snapshot.Accounts.FirstOrDefault(item => item.Id == accountId)
            : null;
        var current = new RadioButton
        {
            GroupName = "GachaImportTarget",
            IsEnabled = account is not null,
            IsChecked = account is not null,
            Content = account is null
                ? "当前所选账号（当前未选择账号）"
                : $"当前所选账号：{account.Label}"
        };
        AutomationProperties.SetAutomationId(current, "GachaImportTargetCurrent");
        var all = new RadioButton
        {
            GroupName = "GachaImportTarget",
            IsChecked = account is null,
            Content = $"当前档案全部账号（{snapshot.Accounts.Count} 个）：{archiveLabel}"
        };
        AutomationProperties.SetAutomationId(all, "GachaImportTargetArchive");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "选择导入目标",
            PrimaryButtonText = "使用此目标并生成预览",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "目标在流程开始时固定；确认页会再次展示。当前档案和账号副本的稳定 ID " +
                            "将随本次导入提交。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "当前所选账号：只导入与该账号 UID／区服一致的记录，文件中的其他 UID 会被忽略。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    current,
                    new TextBlock
                    {
                        Text = "当前档案全部账号：按 UID 在当前档案内匹配账号副本，缺失时新建副本；不会写入其他档案。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    all
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaImportTargetDialog");

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return current.IsChecked == true && account is not null
            ? new GachaImportTargetRequest(GachaImportTargetScope.CurrentAccount, archiveId)
            {
                GameAccountId = account.Id
            }
            : new GachaImportTargetRequest(GachaImportTargetScope.ArchiveAccounts, archiveId);
    }

    private async Task<ContentDialogResult> ShowImportPreviewAsync(GachaImportPlan plan)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导入预览与确认",
            PrimaryButtonText = "开始导入",
            SecondaryButtonText = "更改目标",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 460,
                Content = new TextBlock
                {
                    Text = plan.ConfirmationText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaImportPreviewDialog");
        return await dialog.ShowAsync();
    }
}

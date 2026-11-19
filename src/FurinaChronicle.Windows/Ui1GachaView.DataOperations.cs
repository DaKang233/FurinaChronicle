// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private GachaPortableImportCoordinator? portableImporter;
    private GachaPortableExportPlanner? portableExporter;
    private GachaRecordOperationCoordinator? recordOperations;
    private GachaSTokenRefreshCoordinator? stokenRefresh;
    private GachaManualRefreshCoordinator? manualRefresher;
    private Task? dataOperationTask;
    private GachaReadOnlyRow? selectedRow;

    private void EnsureDataOperationComposition(WindowsHostEnvironment preview)
    {
        var planning = new ServiceGachaImportPlanningSource(
            new GetPlayerArchives(preview.Archives),
            new GetGameAccounts(preview.Archives, preview.Accounts));
        portableImporter ??= new GachaPortableImportCoordinator(
            preview.PortableInputStager,
            preview.PortableIndexFactory,
            new ServiceGachaPortableImportPlanner(preview.PortablePlanner),
            preview.PortableApplier,
            TimeProvider.System);
        portableExporter ??= new GachaPortableExportPlanner(planning);
        recordOperations ??= new GachaRecordOperationCoordinator(
            preview.GachaHistory,
            preview.Changes);
        manualRefresher ??= new GachaManualRefreshCoordinator(
            planning,
            new ServiceGachaManualRefreshGateway(preview.GachaRefresher));
        EnsureFinalCommandComposition(preview);
    }

    private void UpdateDataOperationAvailability()
    {
        bool idle = !IsRefreshInteractionLocked && dataOperationTask is null && importFlowTask is null &&
            exportTask is null && exportCancellation is null;
        bool reading = state?.Snapshot.IsBusy == true;
        bool hasRecord = selectedRow?.Reference is not null;
        DataCommandsButton.IsEnabled = idle && !reading;
        CorrectRecordButton.IsEnabled = idle && !reading && hasRecord;
        DeleteRecordButton.IsEnabled = idle && !reading && hasRecord;
        IrreversibleDeleteButton.IsEnabled = idle && !reading && hasRecord;
        RecordOperationPanel.Visibility = hasRecord ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task RunDataOperationAsync(
        string taskKey,
        string operationTitle,
        Func<Task> operation)
    {
        if (dataOperationTask is not null || importFlowTask is not null ||
            exportTask is not null || exportCancellation is not null) return;
        ClearInlineValidation();
        Task task = operation();
        dataOperationTask = task;
        UpdateCommandAvailability();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            ClearProgress(taskKey);
        }
        catch (Exception exception)
        {
            ReportFailure(
                taskKey,
                taskKey,
                operationTitle + "失败",
                "操作未完成；未确认任何记录被修改，请重新载入后重试。",
                exception);
        }
        finally
        {
            dataOperationTask = null;
            UpdateCommandAvailability();
        }
    }

    private async void OnDataMenuCommand(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem item) return;
        switch (item.Tag?.ToString())
        {
            case "portable-import":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaImportPortable,
                    "Portable 导入",
                    ImportPortableAsync);
                break;
            case "portable-export":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaExportPortable,
                    "Portable 导出",
                    ExportPortableAsync);
                break;
            case "manual-refresh":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaRefreshManual,
                    "手动链接刷新",
                    ManualRefreshAsync);
                break;
            case "stoken-refresh":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaRefreshSToken,
                    "登录账号刷新",
                    RefreshFromLoginAccountAsync);
                break;
            case "windows-cache-refresh":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaRefreshWindowsCache,
                    "网页缓存刷新",
                    RefreshFromWindowsCacheAsync);
                break;
            case "teyvat-import":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaImportTeyvat,
                    "提瓦特小助手导入",
                    ImportFromTeyvatHelperAsync);
                break;
            case "clear-icon-cache":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaIconCache,
                    "图标缓存清理",
                    ClearIconCacheAsync);
                break;
            case "undo-record":
                await RunDataOperationAsync(
                    NotificationTaskKeys.GachaRecordUndo,
                    "撤销抽卡变更",
                    UndoLatestRecordAsync);
                break;
        }
    }

    private async Task ImportPortableAsync()
    {
        if (portableImporter is null || filePicker is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId } snapshot)
        {
            ShowInlineValidation("请先选择档案并等待当前读取结束。");
            return;
        }

        ClearInlineValidation();
        StorageFile? file = await PickDataFileAsync(".zip");
        if (file is null) return;

        // Target and mode are frozen before the file is read; changing the target later always
        // discards the previous preview and re-plans from the same index.
        GachaImportMode mode = importMode.Snapshot();
        GachaPortableImportTarget? target = await ChoosePortableTargetAsync(snapshot, archiveId);
        if (target is null) return;

        PublishProgress(
            NotificationTaskKeys.GachaImportPortable,
            "Portable 导入",
            "正在读取并索引 Portable 包…");
        GachaPortableImportPrepareResult prepared = await portableImporter.PrepareAsync(
            CreateImportSource(file),
            target,
            mode);

        if (!prepared.Succeeded || prepared.Session is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaImportPortable,
                "gacha.import.portable",
                "Portable 导入失败",
                prepared.FailureMessage ?? "Portable 文件无法读取，未写入任何数据。");
            return;
        }

        await using GachaPortableImportSession session = prepared.Session;
        while (true)
        {
            if (session.RequiresArchiveSelection)
            {
                GachaPortableImportTarget? mapping = await ChoosePortableArchiveMappingAsync(
                    session,
                    snapshot,
                    archiveId);
                if (mapping is null)
                {
                    ClearProgress(NotificationTaskKeys.GachaImportPortable);
                    return;
                }

                GachaPortableReplanResult replanned = await portableImporter.ReplanAsync(
                    session,
                    mapping);
                if (!replanned.Succeeded)
                {
                    ReportFailure(
                        NotificationTaskKeys.GachaImportPortable,
                        "gacha.import.portable",
                        "Portable 预览失败",
                        replanned.FailureMessage ?? "Portable 预览重建失败，未写入任何数据。");
                    return;
                }

                continue;
            }

            if (!session.Plan.CanApply)
            {
                ReportFailure(
                    NotificationTaskKeys.GachaImportPortable,
                    "gacha.import.portable",
                    "Portable 导入被阻止",
                    session.BlockingMessage);
                return;
            }

            ContentDialogResult decision = await ShowPortablePreviewAsync(session);
            if (decision == ContentDialogResult.Primary) break;
            if (decision != ContentDialogResult.Secondary)
            {
                ClearProgress(NotificationTaskKeys.GachaImportPortable);
                return;
            }

            GachaPortableImportTarget? changed = await ChoosePortableTargetAsync(snapshot, archiveId);
            if (changed is null)
            {
                ClearProgress(NotificationTaskKeys.GachaImportPortable);
                return;
            }

            GachaPortableReplanResult changedPlan = await portableImporter.ReplanAsync(
                session,
                changed);
            if (!changedPlan.Succeeded)
            {
                ReportFailure(
                    NotificationTaskKeys.GachaImportPortable,
                    "gacha.import.portable",
                    "Portable 预览失败",
                    changedPlan.FailureMessage ?? "Portable 预览重建失败，未写入任何数据。");
                return;
            }
        }

        string targetName = session.Plan.Archive.ProposedName;
        string modeLabel = session.Mode == GachaImportMode.Full
            ? "全量导入：覆盖同记录"
            : "仅补充";
        IReadOnlyList<TombstoneReintroductionConfirmation>? reintroductions = null;
        Guid? cleanupConfirmationId = null;
        GachaPortableApplyOutcome? outcome = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            PublishProgress(
                NotificationTaskKeys.GachaImportPortable,
                "Portable 导入",
                "正在以单一原子事务保存 Portable 导入…");
            outcome = reintroductions is null && cleanupConfirmationId is null
                ? await portableImporter.ApplyAsync(session)
                : await portableImporter.ApplyAsync(
                    session,
                    reintroductions ?? [],
                    cleanupConfirmationId);

            if (outcome.Kind == GachaPortableApplyOutcomeKind.RequiresReintroduction &&
                reintroductions is null)
            {
                IReadOnlyList<TombstoneReintroductionWarning> warnings =
                    outcome.ReintroductionWarnings ?? [];
                if (!await ConfirmReintroductionAsync(warnings))
                {
                    ClearProgress(NotificationTaskKeys.GachaImportPortable);
                    return;
                }

                reintroductions = warnings.Select(warning =>
                    new TombstoneReintroductionConfirmation(
                        warning.TombstoneKey,
                        warning.TombstoneVersion,
                        warning.Reference)).ToArray();
                continue;
            }

            if (outcome.Kind == GachaPortableApplyOutcomeKind.RequiresCleanup &&
                cleanupConfirmationId is null &&
                outcome.CleanupPlan is not null)
            {
                if (!await ConfirmCleanupAsync(outcome.CleanupPlan, "清理并继续"))
                {
                    ClearProgress(NotificationTaskKeys.GachaImportPortable);
                    return;
                }

                cleanupConfirmationId = outcome.CleanupPlan.ConfirmationId;
                continue;
            }

            break;
        }

        if (outcome is null || !outcome.IsSuccess)
        {
            ReportFailure(
                NotificationTaskKeys.GachaImportPortable,
                "gacha.import.portable",
                "Portable 导入失败",
                outcome?.Message ?? "Portable 导入未完成，未写入任何数据。");
            return;
        }

        string counts = outcome.Result is { } applied
            ? $"新增 {applied.InsertedRecordCount} 条、更新 {applied.UpdatedRecordCount} 条、" +
                $"跳过 {applied.SkippedRecordCount} 条。"
            : string.Empty;
        await PublishCommittedResultAsync(
            NotificationTaskKeys.GachaImportPortable,
            "Portable 导入完成",
            $"已导入 Portable 数据（{modeLabel}）；目标：{targetName}。{counts}");
    }

    private async Task ExportPortableAsync()
    {
        if (portableExporter is null || environment is null || filePicker is null ||
            state?.Snapshot is not
            {
                IsBusy: false,
                ArchiveId: Guid archiveId,
                Accounts.Count: > 0
            } snapshot)
        {
            ShowInlineValidation("请先选择档案并等待当前读取结束。");
            return;
        }

        ClearInlineValidation();
        Guid[] current = snapshot.ArchiveAccountIds?.ToArray() ??
            (snapshot.AccountId is Guid accountId ? [accountId] : []);
        Guid[] all = snapshot.Accounts.Select(account => account.Id).ToArray();
        var scopes = new List<(string Label, Guid[] Ids)>();
        if (current.Length > 0 && current.Length < all.Length)
        {
            string label = current.Length == 1
                ? snapshot.Accounts.First(account => account.Id == current[0]).Label
                : $"{current.Length} 个账号";
            scopes.Add(($"当前选择 · {label}", current));
        }

        scopes.Add(($"档案所有账号（{all.Length} 个）", all));
        var scope = new ComboBox
        {
            Header = "导出范围",
            ItemsSource = scopes.Select(item => item.Label).ToArray(),
            SelectedIndex = 0
        };
        var scopeDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "导出 Furina 抽卡数据集",
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Furina Gacha Portable v1 是抽卡数据集，不是完整应用备份；" +
                            "不包含凭据、档案设置或其他领域数据。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    scope
                }
            }
        };
        AutomationProperties.SetAutomationId(scopeDialog, "GachaPortableExportScopeDialog");
        if (await scopeDialog.ShowAsync() != ContentDialogResult.Primary) return;

        GachaPortableExportPlanResult planned = await portableExporter.PlanAsync(
            archiveId,
            scopes[scope.SelectedIndex].Ids);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaExportPortable,
                "gacha.export.portable",
                "Portable 导出失败",
                planned.FailureMessage ?? "无法生成 Portable 导出范围。");
            return;
        }

        GachaPortableExportPlan plan = planned.Plan;
        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认导出 Furina 抽卡数据集",
            PrimaryButtonText = "生成并保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = plan.ConfirmationText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
        AutomationProperties.SetAutomationId(confirmDialog, "GachaPortableExportConfirmDialog");
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

        string temporaryPath = Path.Combine(
            environment.TemporaryDirectory,
            $"gacha-portable-{Guid.NewGuid():N}.zip");
        try
        {
            PublishProgress(
                NotificationTaskKeys.GachaExportPortable,
                "Portable 导出",
                "正在私有临时目录生成并校验 Portable 抽卡数据集…");
            GachaPortableWriteResult result = await environment.PortableExport.ExportAsync(
                temporaryPath,
                archiveId,
                plan.AccountIds);

            StorageFile? destination = await filePicker.PickSaveFileAsync(
                $"FurinaChronicle-Gacha-Portable-{DateTime.Today:yyyyMMdd}",
                "Furina Gacha Portable（抽卡数据集）",
                ".zip");
            if (destination is null)
            {
                ClearProgress(NotificationTaskKeys.GachaExportPortable);
                return;
            }

            await using (var artifact = new FileStream(
                temporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await SaveExportAsync(destination.Path, artifact, CancellationToken.None);
            }

            string location = DescribeSavedLocation(destination.Path, destination.Name);
            Action? openFolder = CreateOpenFolderAction(destination.Path);
            PublishSuccess(
                NotificationTaskKeys.GachaExportPortable,
                "Portable 导出完成",
                $"已保存到 {location}：{result.AccountCount} 个账号、" +
                $"{result.RecordCount} 条记录。这是 Furina 抽卡数据集（Portable v1），" +
                "不是完整应用备份。",
                openFolder is null ? null : "打开文件夹",
                openFolder);
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaExportPortable,
                "gacha.export.portable",
                "Portable 导出失败",
                "Portable 导出未完成，未写出文件。",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private async Task ManualRefreshAsync()
    {
        if (manualRefresher is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false } snapshot)
        {
            ShowInlineValidation("请等待当前读取结束。");
            return;
        }

        ClearInlineValidation();
        var urlBox = new PasswordBox
        {
            Header = "抽卡链接（含 authkey，仅本次请求使用）",
            PlaceholderText = "https://...authkey=...",
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "手动链接刷新",
            PrimaryButtonText = "开始刷新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "链接仅在本次请求中使用；不显示、不保存、不写入日志。" +
                            "成功刷新需要有效 authkey。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    urlBox
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaManualRefreshDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string manualUrl = urlBox.Password;
        if (snapshot.ArchiveId is not Guid archiveId || snapshot.AccountId is not Guid accountId)
        {
            await RefreshWithoutAccountAsync(snapshot,
                FurinaChronicle.Services.Gacha.Refreshing.GachaRefreshSource.ManualUrl, manualUrl);
            return;
        }
        GachaManualRefreshPlanResult planned = await manualRefresher.PlanAsync(
            manualUrl,
            archiveId,
            accountId,
            importMode.Snapshot());
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshManual,
                "gacha.refresh.manual",
                "手动链接刷新失败",
                planned.FailureMessage ?? "抽卡链接无效，未写入任何数据。");
            return;
        }

        PublishProgress(
            NotificationTaskKeys.GachaRefreshManual,
            "手动链接刷新",
            "正在请求抽卡接口并以原子变更集提交…");
        GachaManualRefreshOutcome outcome;
        try
        {
            outcome = await WithRefreshProgressAsync("手动链接刷新", progress =>
                manualRefresher.ExecuteAsync(planned.Plan, manualUrl, progress: progress),
                result => (result.Succeeded, result.Result));
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshManual,
                "gacha.refresh.manual",
                "手动链接刷新失败",
                "刷新未完成，未写入任何数据。",
                exception);
            return;
        }

        if (!outcome.Succeeded)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshManual,
                "gacha.refresh.manual",
                "手动链接刷新失败",
                outcome.Message);
            return;
        }

        await PublishCommittedResultAsync(
            NotificationTaskKeys.GachaRefreshManual,
            "手动链接刷新完成",
            $"刷新完成：{DescribeRefresh(outcome.Result)}");
    }

    private async Task RefreshFromLoginAccountAsync()
    {
        if (stokenRefresh is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false } snapshot)
        {
            ShowInlineValidation("请等待当前读取结束。");
            return;
        }
        if (snapshot.AccountId is not Guid accountId)
        {
            await RefreshSTokenWithoutAccountAsync(snapshot);
            return;
        }

        ClearInlineValidation();
        // The selected mode is frozen here; the coordinator snapshots target account and mode before any
        // credential/network work.
        GachaSTokenRefreshPlanResult planned = await stokenRefresh.PlanAsync(accountId, importMode.Snapshot());
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshSToken,
                "gacha.refresh.stoken",
                "登录账号刷新失败",
                planned.FailureMessage ?? "无法通过登录账号刷新，未写入任何数据。");
            return;
        }

        GachaSTokenRefreshPlan plan = planned.Plan;
        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "通过登录账号刷新",
            PrimaryButtonText = "开始刷新",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock
                {
                    Text = plan.ConfirmationText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
        AutomationProperties.SetAutomationId(confirmDialog, "GachaSTokenRefreshDialog");
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary) return;

        PublishProgress(
            NotificationTaskKeys.GachaRefreshSToken,
            "登录账号刷新",
            "正在使用本机凭据请求抽卡接口并以原子变更集提交…");
        GachaSTokenRefreshOutcome outcome;
        try
        {
            outcome = await WithRefreshProgressAsync("登录账号刷新", progress => stokenRefresh.ExecuteAsync(plan, progress),
                result => (result.Succeeded, result.Result));
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshSToken,
                "gacha.refresh.stoken",
                "登录账号刷新失败",
                "刷新未完成，未写入任何数据。",
                exception);
            return;
        }

        if (!outcome.Succeeded)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRefreshSToken,
                "gacha.refresh.stoken",
                "登录账号刷新失败",
                outcome.Message);
            return;
        }

        await PublishCommittedResultAsync(
            NotificationTaskKeys.GachaRefreshSToken,
            "登录账号刷新完成",
            outcome.Message);
    }

    private async Task ClearIconCacheAsync()
    {
        if (environment is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除图标缓存",
            PrimaryButtonText = "清除缓存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBlock
            {
                Text = "只清除本机图标缓存文件；不会删除抽卡事实、档案、账号或元数据。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaClearIconCacheDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        PublishProgress(
            NotificationTaskKeys.GachaIconCache,
            "清除图标缓存",
            "正在清除本机图标缓存…");
        try
        {
            await environment.GachaIconCache.ClearAsync();
            PublishSuccess(
                NotificationTaskKeys.GachaIconCache,
                "图标缓存已清除",
                "本机图标缓存已清除；抽卡事实、档案、账号和元数据未改变。");
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaIconCache,
                "gacha.cache.icons",
                "清除图标缓存失败",
                "图标缓存未清除，可重试；业务数据未改变。",
                exception);
        }
    }

    private async Task UndoLatestRecordAsync()
    {
        if (environment is null ||
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId })
        {
            ShowInlineValidation("请先选择档案并等待当前读取结束。");
            return;
        }

        ClearInlineValidation();
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "撤销最近一次抽卡变更",
            PrimaryButtonText = "撤销最近一次变更",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "只撤销当前档案最近一次仍可撤销的抽卡写入；不可逆删除、已被后续操作替代或撤销材料已淘汰的变更不能撤销。撤销前会再次校验事实版本。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaRecordUndoDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        PublishProgress(
            NotificationTaskKeys.GachaRecordUndo,
            "撤销抽卡变更",
            "正在校验并撤销最近一次变更…");
        GachaAtomicChangeResult result;
        try
        {
            result = await environment.GachaHistory.UndoLatestAsync(
                OperationId.New(),
                archiveId);
        }
        catch (Exception exception)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordUndo,
                "gacha.record.undo",
                "撤销失败",
                "撤销未完成，未写入任何数据。",
                exception);
            return;
        }

        if (result.Status is not (ChangeExecutionStatus.Applied or
            ChangeExecutionStatus.NoOp or ChangeExecutionStatus.AlreadyCommitted))
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordUndo,
                "gacha.record.undo",
                "撤销失败",
                result.ConflictReason ??
                    "没有可撤销的变更，或撤销材料已失效；未写入任何数据。");
            return;
        }

        selectedRow = null;
        await PublishCommittedResultAsync(
            NotificationTaskKeys.GachaRecordUndo,
            "撤销完成",
            $"已撤销最近一次抽卡变更（影响 {result.AffectedRecordCount} 条记录）。");
    }

    private async void OnCorrectRecordCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(
                NotificationTaskKeys.GachaRecordCorrect,
                "纠正记录",
                () => CorrectRecordAsync(row));
        }
    }

    private async void OnDeleteRecordCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(
                NotificationTaskKeys.GachaRecordDelete,
                "删除记录",
                () => DeleteRecordAsync(row));
        }
    }

    private async void OnIrreversibleDeleteCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(
                NotificationTaskKeys.GachaRecordPurge,
                "永久删除记录",
                () => IrreversibleDeleteRecordAsync(row));
        }
    }

    private async Task CorrectRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || environment is null ||
            row.Reference is not GachaFactReference reference)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordCorrect,
                "gacha.record.correct",
                "纠正失败",
                "所选记录缺少稳定引用，请重新载入。");
            return;
        }

        GachaFactState? current = await environment.Changes.GetCurrentAsync(reference);
        if (current is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordCorrect,
                "gacha.record.correct",
                "纠正失败",
                "所选记录已不存在，未写入任何数据。请重新载入。");
            return;
        }

        GachaRecord record = current.Record;
        var name = new TextBox { Header = "物品名称", Text = record.ItemName ?? "" };
        var itemId = new TextBox { Header = "物品 ID（可留空）", Text = record.ItemId ?? "" };
        var rank = new ComboBox
        {
            Header = "星级",
            ItemsSource = new[] { "未知", "3 星", "4 星", "5 星" },
            SelectedIndex = record.RankType is int rankType ? rankType - 2 : 0
        };
        var time = new TextBox
        {
            Header = "记录时间（yyyy-MM-dd HH:mm:ss zzz）",
            Text = record.Time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "纠正抽卡记录",
            PrimaryButtonText = "继续",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "仅修改本页选中的 1 条记录；保留稳定记录 ID 与账号归属，" +
                            "来源记为“用户输入”，并保留变更前事实。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    name,
                    itemId,
                    rank,
                    time
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaRecordCorrectDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        if (!DateTimeOffset.TryParse(
            time.Text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTimeOffset occurredAt))
        {
            ShowInlineValidation("记录时间格式无效，未写入任何数据。");
            return;
        }

        int? rankValue = rank.SelectedIndex switch
        {
            1 => 3,
            2 => 4,
            3 => 5,
            _ => null
        };
        GachaRecordOperationPlanResult planned = await recordOperations.PlanAsync(
            reference,
            GachaRecordOperationKind.Correct,
            new GachaCorrectionInput(name.Text, itemId.Text, rankValue, occurredAt));
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordCorrect,
                "gacha.record.correct",
                "纠正失败",
                planned.FailureMessage ?? "无法生成纠正预览，未写入任何数据。");
            return;
        }

        if (!await ConfirmRecordOperationAsync(planned.Plan, "保存纠正", requireExplicit: false))
        {
            return;
        }

        await ExecuteRecordOperationAsync(
            planned.Plan,
            NotificationTaskKeys.GachaRecordCorrect,
            "纠正记录");
    }

    private async Task DeleteRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || row.Reference is not GachaFactReference reference)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordDelete,
                "gacha.record.delete",
                "删除失败",
                "所选记录缺少稳定引用，请重新载入。");
            return;
        }

        GachaRecordOperationPlanResult planned = await recordOperations.PlanAsync(
            reference,
            GachaRecordOperationKind.Delete);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordDelete,
                "gacha.record.delete",
                "删除失败",
                planned.FailureMessage ?? "无法生成删除预览，未写入任何数据。");
            return;
        }

        if (!await ConfirmRecordOperationAsync(
            planned.Plan,
            "删除记录（可撤销）",
            requireExplicit: false))
        {
            return;
        }

        await ExecuteRecordOperationAsync(
            planned.Plan,
            NotificationTaskKeys.GachaRecordDelete,
            "删除记录");
    }

    private async Task IrreversibleDeleteRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || row.Reference is not GachaFactReference reference)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordPurge,
                "gacha.record.purge",
                "永久删除失败",
                "所选记录缺少稳定引用，请重新载入。");
            return;
        }

        GachaRecordOperationPlanResult planned = await recordOperations.PlanAsync(
            reference,
            GachaRecordOperationKind.IrreversibleDelete);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ReportFailure(
                NotificationTaskKeys.GachaRecordPurge,
                "gacha.record.purge",
                "永久删除失败",
                planned.FailureMessage ?? "无法生成永久删除预览，未写入任何数据。");
            return;
        }

        if (!await ConfirmRecordOperationAsync(
            planned.Plan,
            "永久删除",
            requireExplicit: true))
        {
            return;
        }

        await ExecuteRecordOperationAsync(
            planned.Plan,
            NotificationTaskKeys.GachaRecordPurge,
            "永久删除记录");
    }

    private async Task ExecuteRecordOperationAsync(
        GachaRecordOperationPlan plan,
        string taskKey,
        string operationTitle)
    {
        if (recordOperations is null) return;
        PublishProgress(taskKey, operationTitle, "正在以单一原子变更集保存…");
        GachaRecordOperationOutcome outcome;
        try
        {
            outcome = await recordOperations.ExecuteAsync(plan);
        }
        catch (Exception exception)
        {
            ReportFailure(
                taskKey,
                taskKey,
                operationTitle + "失败",
                "操作未完成，未写入任何数据。",
                exception);
            return;
        }

        if (outcome.RequiresUserConfirmation && outcome.CleanupPlan is not null)
        {
            if (!await ConfirmCleanupAsync(outcome.CleanupPlan, "清理并继续"))
            {
                ClearProgress(taskKey);
                return;
            }

            outcome = await recordOperations.ExecuteAsync(
                plan,
                outcome.CleanupPlan.ConfirmationId);
        }

        if (!outcome.IsSuccess)
        {
            ReportFailure(
                taskKey,
                taskKey,
                operationTitle + "失败",
                outcome.Message);
            return;
        }

        selectedRow = null;
        await PublishCommittedResultAsync(taskKey, operationTitle + "完成", outcome.Message);
    }

    private async Task<bool> ConfirmRecordOperationAsync(
        GachaRecordOperationPlan plan,
        string primaryText,
        bool requireExplicit)
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = plan.Summary,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = plan.ImpactText,
            TextWrapping = TextWrapping.Wrap
        });
        CheckBox? acknowledgement = null;
        if (requireExplicit)
        {
            acknowledgement = new CheckBox
            {
                Content = "我理解此操作不可撤销，并会清除可恢复快照与撤销材料。"
            };
            content.Children.Add(acknowledgement);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = plan.KindLabel,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = !requireExplicit,
            Content = new ScrollViewer { MaxHeight = 420, Content = content }
        };
        if (acknowledgement is not null)
        {
            acknowledgement.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            acknowledgement.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        }

        AutomationProperties.SetAutomationId(dialog, "GachaRecordOperationDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<GachaPortableImportTarget?> ChoosePortableTargetAsync(
        GachaReadOnlySnapshot snapshot,
        Guid archiveId)
    {
        string archiveLabel = snapshot.Archives.FirstOrDefault(archive => archive.Id == archiveId)?.Label
            ?? "当前档案";
        var automatic = new RadioButton
        {
            GroupName = "PortableTarget",
            Content = "按来源名称自动匹配（推荐）",
            IsChecked = true
        };
        AutomationProperties.SetAutomationId(automatic, "GachaPortableTargetAutomatic");
        var current = new RadioButton
        {
            GroupName = "PortableTarget",
            Content = $"导入到当前所选档案：{archiveLabel}"
        };
        AutomationProperties.SetAutomationId(current, "GachaPortableTargetCurrent");
        var create = new RadioButton
        {
            GroupName = "PortableTarget",
            Content = "按来源名称新建档案"
        };
        AutomationProperties.SetAutomationId(create, "GachaPortableTargetNew");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "选择 Portable 导入目标",
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
                        Text = "目标在流程开始时固定；确认页会再次展示。版本、索引与包指纹随会话固定，" +
                            "预览后目标或本地事实变化会拒绝旧预览。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    automatic,
                    current,
                    create
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaPortableTargetDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        if (current.IsChecked == true)
        {
            return GachaPortableImportTarget.CurrentArchive(archiveId);
        }

        return create.IsChecked == true
            ? GachaPortableImportTarget.NewArchive
            : GachaPortableImportTarget.Automatic;
    }

    private async Task<GachaPortableImportTarget?> ChoosePortableArchiveMappingAsync(
        GachaPortableImportSession session,
        GachaReadOnlySnapshot snapshot,
        Guid archiveId)
    {
        string archiveLabel = snapshot.Archives.FirstOrDefault(archive => archive.Id == archiveId)?.Label
            ?? "当前档案";
        string candidates = session.Plan.Archive.Candidates.Count == 0
            ? "没有精确或相似名称的候选档案。"
            : string.Join(
                Environment.NewLine,
                session.Plan.Archive.Candidates.Take(8).Select(candidate =>
                    $"· {candidate.Name}" + (candidate.IsExactMatch ? "（精确同名）" : "（名称相似）")));
        var current = new RadioButton
        {
            GroupName = "PortableMapping",
            Content = $"导入到当前所选档案：{archiveLabel}",
            IsChecked = true
        };
        AutomationProperties.SetAutomationId(current, "GachaPortableTargetCurrent");
        var create = new RadioButton
        {
            GroupName = "PortableMapping",
            Content = "按来源名称新建档案"
        };
        AutomationProperties.SetAutomationId(create, "GachaPortableTargetNew");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "需要确认档案映射",
            PrimaryButtonText = "按此映射重新预览",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = $"来源档案：{session.Package.SourceArchive.Name}" +
                                Environment.NewLine + candidates,
                            TextWrapping = TextWrapping.Wrap
                        },
                        current,
                        create
                    }
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaPortableMappingDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return create.IsChecked == true
            ? GachaPortableImportTarget.NewArchive
            : GachaPortableImportTarget.CurrentArchive(archiveId);
    }

    private async Task<ContentDialogResult> ShowPortablePreviewAsync(
        GachaPortableImportSession session)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Portable 导入预览与确认",
            PrimaryButtonText = "应用导入",
            SecondaryButtonText = "更改目标",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new ScrollViewer
            {
                MaxHeight = 500,
                Content = new TextBlock
                {
                    Text = session.ConfirmationText,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaPortablePreviewDialog");
        return await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmReintroductionAsync(
        IReadOnlyList<TombstoneReintroductionWarning> warnings)
    {
        string records = string.Join(
            Environment.NewLine,
            warnings.Take(10).Select(warning => $"· 记录 {warning.Reference.ExternalRecordId}"));
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "将重新引入永久删除的记录",
            PrimaryButtonText = "明确重新引入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = $"此包命中 {warnings.Count} 条当前档案的本地永久删除标记。" +
                    "只有本次明确确认才会重新引入；取消不会改变数据库。" +
                    Environment.NewLine + records,
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaPortableReintroductionDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<bool> ConfirmCleanupAsync(HistoryCleanupPlan plan, string primaryText)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "需要清理较早撤销材料",
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = $"继续需要清理 {plan.Candidates.Count} 份较早撤销材料" +
                    $"（约 {plan.ReclaimableBytes} 字节）；不会删除 Revision 或业务事实。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaCleanupDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<StorageFile?> PickDataFileAsync(string extension)
    {
        if (filePicker is null) return null;
        return await filePicker.PickOpenFileAsync(extension);
    }

    private StackPanel CreateRecordOperationButtons(
        GachaReadOnlyRow row,
        ContentDialog detailDialog)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(OperationButton(
            "纠正",
            "GachaRecordCorrect",
            NotificationTaskKeys.GachaRecordCorrect,
            "纠正记录",
            () => CorrectRecordAsync(row)));
        panel.Children.Add(OperationButton(
            "删除",
            "GachaRecordDelete",
            NotificationTaskKeys.GachaRecordDelete,
            "删除记录",
            () => DeleteRecordAsync(row)));
        panel.Children.Add(OperationButton(
            "永久删除",
            "GachaRecordIrreversibleDelete",
            NotificationTaskKeys.GachaRecordPurge,
            "永久删除记录",
            () => IrreversibleDeleteRecordAsync(row)));
        return panel;

        Button OperationButton(
            string text,
            string automationId,
            string taskKey,
            string operationTitle,
            Func<Task> operation)
        {
            var button = new Button { Content = text };
            AutomationProperties.SetAutomationId(button, automationId);
            button.Click += async (_, _) =>
            {
                detailDialog.Hide();
                await Task.Yield();
                await RunDataOperationAsync(taskKey, operationTitle, operation);
            };
            return button;
        }
    }
}

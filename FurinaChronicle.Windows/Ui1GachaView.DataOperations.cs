// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
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

    private void EnsureDataOperationComposition(Ui1PreviewEnvironment preview)
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
        bool idle = dataOperationTask is null && importFlowTask is null &&
            exportTask is null && exportCancellation is null;
        bool reading = state?.Snapshot.IsBusy == true;
        bool hasRecord = selectedRow?.Reference is not null;
        DataCommandsButton.IsEnabled = idle && !reading;
        CorrectRecordButton.IsEnabled = idle && !reading && hasRecord;
        DeleteRecordButton.IsEnabled = idle && !reading && hasRecord;
        IrreversibleDeleteButton.IsEnabled = idle && !reading && hasRecord;
        RecordOperationPanel.Visibility = hasRecord ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowDataOperationStatus(string text, bool isError = false)
    {
        DataOperationStatus.Text = text;
        DataOperationStatus.Visibility = Visibility.Visible;
        // Keep the actual state text as the accessible name so the LiveSetting announcement
        // and UIA-based acceptance can read the result instead of a generic label.
        AutomationProperties.SetName(
            DataOperationStatus,
            (isError ? "数据操作未完成：" : "数据操作状态：") + text);
    }

    private async Task RunDataOperationAsync(Func<Task> operation)
    {
        if (dataOperationTask is not null || importFlowTask is not null ||
            exportTask is not null || exportCancellation is not null) return;
        Task task = operation();
        dataOperationTask = task;
        UpdateCommandAvailability();
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowDataOperationStatus(
                "操作未完成；未确认任何记录被修改，请重新载入后重试。",
                isError: true);
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
                await RunDataOperationAsync(ImportPortableAsync);
                break;
            case "portable-export":
                await RunDataOperationAsync(ExportPortableAsync);
                break;
            case "manual-refresh":
                await RunDataOperationAsync(ManualRefreshAsync);
                break;
            case "stoken-refresh":
                await RunDataOperationAsync(RefreshFromLoginAccountAsync);
                break;
            case "windows-cache-refresh":
                await RunDataOperationAsync(RefreshFromWindowsCacheAsync);
                break;
            case "teyvat-import":
                await RunDataOperationAsync(ImportFromTeyvatHelperAsync);
                break;
            case "clear-icon-cache":
                await RunDataOperationAsync(ClearIconCacheAsync);
                break;
        }
    }

    private async Task ImportPortableAsync()
    {
        if (portableImporter is null || filePicker is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId } snapshot)
        {
            ShowDataOperationStatus("请先选择档案并等待当前读取结束。", isError: true);
            return;
        }

        StorageFile? file = await PickDataFileAsync(".zip");
        if (file is null) return;

        // Target and mode are frozen before the file is read; changing the target later always
        // discards the previous preview and re-plans from the same index.
        GachaImportMode mode = importMode.Snapshot();
        GachaPortableImportTarget? target = await ChoosePortableTargetAsync(snapshot, archiveId);
        if (target is null)
        {
            ShowDataOperationStatus("已取消 Portable 导入，未写入任何数据。");
            return;
        }

        GachaPortableImportPrepareResult prepared;
        (ContentDialog prepareDialog, Task prepareShow) = ShowDataProgressDialog(
            "正在读取并索引 Portable 包",
            "正在把所选 ZIP 暂存到私有临时目录并建立磁盘索引；大型档案可能需要一些时间。");
        try
        {
            prepared = await portableImporter.PrepareAsync(
                CreateImportSource(file),
                target,
                mode);
        }
        finally
        {
            await CloseDataProgressDialogAsync(prepareDialog, prepareShow);
        }

        if (!prepared.Succeeded || prepared.Session is null)
        {
            ShowDataOperationStatus(
                prepared.FailureMessage ?? "Portable 文件无法读取，未写入任何数据。",
                isError: true);
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
                    ShowDataOperationStatus("已取消 Portable 导入，未写入任何数据。");
                    return;
                }

                GachaPortableReplanResult replanned = await portableImporter.ReplanAsync(
                    session,
                    mapping);
                if (!replanned.Succeeded)
                {
                    ShowDataOperationStatus(
                        replanned.FailureMessage ?? "Portable 预览重建失败，未写入任何数据。",
                        isError: true);
                    return;
                }

                continue;
            }

            if (!session.Plan.CanApply)
            {
                ShowDataOperationStatus(session.BlockingMessage, isError: true);
                return;
            }

            ContentDialogResult decision = await ShowPortablePreviewAsync(session);
            if (decision == ContentDialogResult.Primary) break;
            if (decision != ContentDialogResult.Secondary)
            {
                ShowDataOperationStatus("已取消 Portable 导入，未写入任何数据。");
                return;
            }

            GachaPortableImportTarget? changed = await ChoosePortableTargetAsync(snapshot, archiveId);
            if (changed is null)
            {
                ShowDataOperationStatus("已取消 Portable 导入，未写入任何数据。");
                return;
            }

            GachaPortableReplanResult changedPlan = await portableImporter.ReplanAsync(
                session,
                changed);
            if (!changedPlan.Succeeded)
            {
                ShowDataOperationStatus(
                    changedPlan.FailureMessage ?? "Portable 预览重建失败，未写入任何数据。",
                    isError: true);
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
            (ContentDialog applyDialog, Task applyShow) = ShowDataProgressDialog(
                "正在应用 Portable 数据",
                "正在以单一原子事务提交；此阶段不能取消。");
            try
            {
                outcome = reintroductions is null && cleanupConfirmationId is null
                    ? await portableImporter.ApplyAsync(session)
                    : await portableImporter.ApplyAsync(
                        session,
                        reintroductions ?? [],
                        cleanupConfirmationId);
            }
            finally
            {
                await CloseDataProgressDialogAsync(applyDialog, applyShow);
            }

            if (outcome.Kind == GachaPortableApplyOutcomeKind.RequiresReintroduction &&
                reintroductions is null)
            {
                IReadOnlyList<TombstoneReintroductionWarning> warnings =
                    outcome.ReintroductionWarnings ?? [];
                if (!await ConfirmReintroductionAsync(warnings))
                {
                    ShowDataOperationStatus("已取消重新引入，未写入任何数据。");
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
                    ShowDataOperationStatus("已取消清理撤销材料，未写入任何数据。");
                    return;
                }

                cleanupConfirmationId = outcome.CleanupPlan.ConfirmationId;
                continue;
            }

            break;
        }

        if (outcome is null || !outcome.IsSuccess)
        {
            ShowDataOperationStatus(
                outcome?.Message ?? "Portable 导入未完成，未写入任何数据。",
                isError: true);
            return;
        }

        string? refresh = await RefreshAfterImportAsync();
        ShowDataOperationStatus(
            $"已保存 Portable 导入（{modeLabel}）：{outcome.Message}" +
            $"\n目标：{targetName}" +
            (refresh is null ? "" : $"\n{refresh}"));
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
            ShowDataOperationStatus("请先选择档案并等待当前读取结束。", isError: true);
            return;
        }

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
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法生成 Portable 导出范围。",
                isError: true);
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
            GachaPortableWriteResult result;
            (ContentDialog exportDialog, Task exportShow) = ShowDataProgressDialog(
                "正在生成 Portable 文件",
                "正在私有临时目录生成并校验 Portable 抽卡数据集。");
            try
            {
                result = await environment.PortableExport.ExportAsync(
                    temporaryPath,
                    archiveId,
                    plan.AccountIds);
            }
            finally
            {
                await CloseDataProgressDialogAsync(exportDialog, exportShow);
            }

            StorageFile? destination = await filePicker.PickSaveFileAsync(
                $"FurinaChronicle-Gacha-Portable-{DateTime.Today:yyyyMMdd}",
                "Furina Gacha Portable（抽卡数据集）",
                ".zip");
            if (destination is null)
            {
                ShowDataOperationStatus("已取消保存，未写出 Portable 导出文件。");
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

            ShowDataOperationStatus(
                $"已保存 {destination.Name}：{result.AccountCount} 个账号、" +
                $"{result.RecordCount} 条记录。这是 Furina 抽卡数据集（Portable v1），" +
                "不是完整应用备份。");
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
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId } snapshot)
        {
            ShowDataOperationStatus("请先选择档案并等待当前读取结束。", isError: true);
            return;
        }

        if (snapshot.AccountId is not Guid accountId)
        {
            ShowDataOperationStatus("请先选择要刷新的账号。", isError: true);
            return;
        }

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
                            "成功刷新需要有效 authkey。本宿主不提供 SToken 与网页缓存刷新。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    urlBox
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaManualRefreshDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string manualUrl = urlBox.Password;
        GachaManualRefreshPlanResult planned = await manualRefresher.PlanAsync(
            manualUrl,
            archiveId,
            accountId,
            importMode.Snapshot());
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "抽卡链接无效，未写入任何数据。",
                isError: true);
            return;
        }

        GachaManualRefreshOutcome outcome;
        (ContentDialog refreshDialog, Task refreshShow) = ShowDataProgressDialog(
            "正在通过手动链接刷新",
            "正在请求抽卡接口并以原子变更集提交；此阶段不能取消。");
        try
        {
            outcome = await manualRefresher.ExecuteAsync(planned.Plan, manualUrl);
        }
        finally
        {
            await CloseDataProgressDialogAsync(refreshDialog, refreshShow);
        }

        if (!outcome.Succeeded)
        {
            ShowDataOperationStatus(outcome.Message, isError: true);
            return;
        }

        string? refresh = await RefreshAfterImportAsync();
        ShowDataOperationStatus(
            outcome.Message + (refresh is null ? "" : $"\n{refresh}"));
    }

    private async Task RefreshFromLoginAccountAsync()
    {
        if (stokenRefresh is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false, AccountId: Guid accountId } snapshot)
        {
            ShowDataOperationStatus("请先选择要刷新的账号并等待当前读取结束。", isError: true);
            return;
        }

        // Full mode is frozen here; the coordinator snapshots target account and mode before any
        // credential/network work.
        GachaSTokenRefreshPlanResult planned = await stokenRefresh.PlanAsync(accountId);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法通过登录账号刷新，未写入任何数据。",
                isError: true);
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
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            ShowDataOperationStatus("已取消通过登录账号刷新，未写入任何数据。");
            return;
        }

        GachaSTokenRefreshOutcome outcome;
        (ContentDialog progress, Task show) = ShowDataProgressDialog(
            "正在通过登录账号刷新",
            "正在使用本机隔离宿主凭据请求抽卡接口并以原子变更集提交；此阶段不能取消。");
        try
        {
            outcome = await stokenRefresh.ExecuteAsync(plan);
        }
        finally
        {
            await CloseDataProgressDialogAsync(progress, show);
        }

        if (!outcome.Succeeded)
        {
            ShowDataOperationStatus(outcome.Message, isError: true);
            return;
        }

        string? refresh = await RefreshAfterImportAsync();
        ShowDataOperationStatus(
            outcome.Message + (refresh is null ? "" : $"\n{refresh}"));
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
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            ShowDataOperationStatus("已取消清除图标缓存。");
            return;
        }

        await environment.GachaIconCache.ClearAsync();
        ShowDataOperationStatus("已清除图标缓存；本地抽卡事实、档案和元数据未改变。");
    }

    private async void OnCorrectRecordCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(() => CorrectRecordAsync(row));
        }
    }

    private async void OnDeleteRecordCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(() => DeleteRecordAsync(row));
        }
    }

    private async void OnIrreversibleDeleteCommand(object sender, RoutedEventArgs e)
    {
        if (selectedRow is GachaReadOnlyRow row)
        {
            await RunDataOperationAsync(() => IrreversibleDeleteRecordAsync(row));
        }
    }

    private async Task CorrectRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || environment is null ||
            row.Reference is not GachaFactReference reference)
        {
            ShowDataOperationStatus("所选记录缺少稳定引用，请重新载入。", isError: true);
            return;
        }

        GachaFactState? current = await environment.Changes.GetCurrentAsync(reference);
        if (current is null)
        {
            ShowDataOperationStatus("所选记录已不存在，未写入任何数据。请重新载入。", isError: true);
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
            ShowDataOperationStatus("记录时间格式无效，未写入任何数据。", isError: true);
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
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法生成纠正预览，未写入任何数据。",
                isError: true);
            return;
        }

        if (!await ConfirmRecordOperationAsync(planned.Plan, "保存纠正", requireExplicit: false))
        {
            ShowDataOperationStatus("已取消纠正，未写入任何数据。");
            return;
        }

        await ExecuteRecordOperationAsync(planned.Plan);
    }

    private async Task DeleteRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || row.Reference is not GachaFactReference reference)
        {
            ShowDataOperationStatus("所选记录缺少稳定引用，请重新载入。", isError: true);
            return;
        }

        GachaRecordOperationPlanResult planned = await recordOperations.PlanAsync(
            reference,
            GachaRecordOperationKind.Delete);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法生成删除预览，未写入任何数据。",
                isError: true);
            return;
        }

        if (!await ConfirmRecordOperationAsync(
            planned.Plan,
            "删除记录（可撤销）",
            requireExplicit: false))
        {
            ShowDataOperationStatus("已取消删除，未写入任何数据。");
            return;
        }

        await ExecuteRecordOperationAsync(planned.Plan);
    }

    private async Task IrreversibleDeleteRecordAsync(GachaReadOnlyRow row)
    {
        if (recordOperations is null || row.Reference is not GachaFactReference reference)
        {
            ShowDataOperationStatus("所选记录缺少稳定引用，请重新载入。", isError: true);
            return;
        }

        GachaRecordOperationPlanResult planned = await recordOperations.PlanAsync(
            reference,
            GachaRecordOperationKind.IrreversibleDelete);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法生成永久删除预览，未写入任何数据。",
                isError: true);
            return;
        }

        if (!await ConfirmRecordOperationAsync(
            planned.Plan,
            "永久删除",
            requireExplicit: true))
        {
            ShowDataOperationStatus("已取消永久删除，未写入任何数据。");
            return;
        }

        await ExecuteRecordOperationAsync(planned.Plan);
    }

    private async Task ExecuteRecordOperationAsync(GachaRecordOperationPlan plan)
    {
        if (recordOperations is null) return;
        GachaRecordOperationOutcome outcome;
        (ContentDialog progress, Task show) = ShowDataProgressDialog(
            "正在保存记录操作",
            "正在以单一原子变更集提交；此阶段不能取消。");
        try
        {
            outcome = await recordOperations.ExecuteAsync(plan);
        }
        finally
        {
            await CloseDataProgressDialogAsync(progress, show);
        }

        if (outcome.RequiresUserConfirmation && outcome.CleanupPlan is not null)
        {
            if (!await ConfirmCleanupAsync(outcome.CleanupPlan, "清理并继续"))
            {
                ShowDataOperationStatus("已取消清理撤销材料，未写入任何数据。");
                return;
            }

            outcome = await recordOperations.ExecuteAsync(
                plan,
                outcome.CleanupPlan.ConfirmationId);
        }

        if (!outcome.IsSuccess)
        {
            ShowDataOperationStatus(outcome.Message, isError: true);
            return;
        }

        selectedRow = null;
        string? refresh = await RefreshAfterImportAsync();
        ShowDataOperationStatus(
            outcome.Message + (refresh is null ? "" : $"\n{refresh}"));
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

    private (ContentDialog Dialog, Task ShowTask) ShowDataProgressDialog(
        string title,
        string detail)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    new ProgressRing
                    {
                        IsActive = true,
                        Width = 40,
                        Height = 40,
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock { Text = detail, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaDataProgressDialog");
        return (dialog, dialog.ShowAsync().AsTask());
    }

    private static async Task CloseDataProgressDialogAsync(ContentDialog dialog, Task showTask)
    {
        dialog.Hide();
        try { await showTask; } catch (Exception) { }
    }

    private StackPanel CreateRecordOperationButtons(
        GachaReadOnlyRow row,
        ContentDialog detailDialog)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(OperationButton("纠正", "GachaRecordCorrect", () => CorrectRecordAsync(row)));
        panel.Children.Add(OperationButton("删除", "GachaRecordDelete", () => DeleteRecordAsync(row)));
        panel.Children.Add(OperationButton(
            "永久删除",
            "GachaRecordIrreversibleDelete",
            () => IrreversibleDeleteRecordAsync(row)));
        return panel;

        Button OperationButton(string text, string automationId, Func<Task> operation)
        {
            var button = new Button { Content = text };
            AutomationProperties.SetAutomationId(button, automationId);
            button.Click += async (_, _) =>
            {
                detailDialog.Hide();
                await Task.Yield();
                await RunDataOperationAsync(operation);
            };
            return button;
        }
    }
}

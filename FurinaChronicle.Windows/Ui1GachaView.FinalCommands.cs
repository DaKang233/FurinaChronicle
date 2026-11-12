// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.IO;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private GachaWindowsCacheRefreshCoordinator? windowsCacheRefresh;
    private GachaTeyvatHelperImportCoordinator? teyvatImport;

    private void EnsureFinalCommandComposition(Ui1PreviewEnvironment preview)
    {
        var planning = new ServiceGachaImportPlanningSource(
            new GetPlayerArchives(preview.Archives),
            new GetGameAccounts(preview.Archives, preview.Accounts));
        windowsCacheRefresh ??= new GachaWindowsCacheRefreshCoordinator(
            planning,
            new ServiceGachaWindowsCacheRefreshGateway(
                preview.GachaRefresher,
                preview.WindowsCacheUrlProvider));
        teyvatImport ??= new GachaTeyvatHelperImportCoordinator(
            planning,
            new ServiceGachaTeyvatHelperImportGateway(preview.TeyvatHelperImport));
    }

    private async Task RefreshFromWindowsCacheAsync()
    {
        if (windowsCacheRefresh is null || importMode is null ||
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

        string? gameFolder = await PickGameInstallationFolderAsync();
        if (gameFolder is null)
        {
            ShowDataOperationStatus("已取消选择游戏安装目录，未刷新。");
            return;
        }

        // Full-import mode and the target account are fixed before the cache is read.
        GachaImportMode mode = importMode.Snapshot();
        GachaWindowsCacheRefreshPlanResult planned = await windowsCacheRefresh.PlanAsync(
            gameFolder,
            archiveId,
            accountId,
            mode);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "无法准备网页缓存刷新，未写入任何数据。",
                isError: true);
            return;
        }

        GachaWindowsCacheRefreshPlan plan = planned.Plan;
        var confirmDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "通过网页缓存刷新",
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
        AutomationProperties.SetAutomationId(confirmDialog, "GachaWindowsCacheConfirmDialog");
        if (await confirmDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            ShowDataOperationStatus("已取消网页缓存刷新，未写入任何数据。");
            return;
        }

        GachaWindowsCacheRefreshOutcome outcome;
        (ContentDialog progress, Task show) = ShowDataProgressDialog(
            "正在通过网页缓存刷新",
            "正在读取本机游戏网页缓存并请求抽卡接口；此阶段不能取消。");
        try
        {
            outcome = await windowsCacheRefresh.ExecuteAsync(plan);
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

    private async Task ImportFromTeyvatHelperAsync()
    {
        if (teyvatImport is null || importCoordinator is null || importMode is null ||
            state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId } snapshot)
        {
            ShowDataOperationStatus("请先选择档案并等待当前读取结束。", isError: true);
            return;
        }

        GachaImportMode mode = importMode.Snapshot();
        var uid = new TextBox
        {
            Header = "原神 UID",
            PlaceholderText = "例如 100000001"
        };
        var url = new PasswordBox
        {
            Header = "抽卡链接（含 authkey，仅本次使用）",
            PlaceholderText = "https://...authkey=...",
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        var inputDialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "从提瓦特小助手导入（手动）",
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
                        Text = "自动导入：不可用（需要用户页通行证登录与角色选择；孤立宿主未提供该上下文）。" +
                            "请手动输入 UID 与抽卡链接。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    new TextBlock
                    {
                        Text = "隐私提示：继续后会先选择本地导入目标；确认发送后才会把 UID 与含 authkey " +
                            "的抽卡链接发送给第三方 lelaer.com（提瓦特小助手）。链接不显示、不保存、不写入日志。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    uid,
                    url
                }
            }
        };
        AutomationProperties.SetAutomationId(inputDialog, "GachaTeyvatInputDialog");
        if (await inputDialog.ShowAsync() != ContentDialogResult.Primary) return;

        string uidText = uid.Text;
        string gachaUrl = url.Password;

        // Fix the local import target before the third-party request so a later page change
        // cannot redirect the downloaded payload.
        GachaImportTargetRequest? target = await ChooseImportTargetAsync(snapshot, archiveId);
        if (target is null)
        {
            ShowDataOperationStatus("已取消导入，未向第三方发送任何内容。");
            return;
        }

        GachaTeyvatHelperImportPlanResult planned = await teyvatImport.PlanAsync(
            uidText,
            gachaUrl,
            archiveId,
            mode);
        if (!planned.Succeeded || planned.Plan is null)
        {
            ShowDataOperationStatus(
                planned.FailureMessage ?? "UID 或抽卡链接无效，未发起第三方请求。",
                isError: true);
            return;
        }

        GachaTeyvatHelperImportPlan plan = planned.Plan;
        if (!await ConfirmTeyvatPrivacyAsync(plan))
        {
            ShowDataOperationStatus("已取消，未向第三方发送任何内容。");
            return;
        }

        GachaTeyvatHelperImportOutcome download;
        (ContentDialog progress, Task show) = ShowDataProgressDialog(
            "正在从提瓦特小助手获取数据",
            "正在把 UID 与抽卡链接发送给第三方 lelaer.com；此阶段不能取消。");
        try
        {
            download = await teyvatImport.DownloadAsync(plan, gachaUrl);
        }
        finally
        {
            await CloseDataProgressDialogAsync(progress, show);
        }

        if (!download.Succeeded || download.Content is null)
        {
            ShowDataOperationStatus(
                download.FailureMessage ?? "从提瓦特小助手导入未完成，未写入任何数据。",
                isError: true);
            return;
        }

        byte[] content = download.Content;
        string fileName = download.FileName ?? "teyvat-helper-uigf.json";
        var source = new GachaImportSource(
            fileName,
            _ => Task.FromResult<Stream>(new MemoryStream(content, writable: false)));
        await ImportPreparedSourceAsync(
            source,
            snapshot,
            archiveId,
            mode,
            CancellationToken.None,
            ShowDataOperationStatus,
            target);
    }

    private async Task<bool> ConfirmTeyvatPrivacyAsync(GachaTeyvatHelperImportPlan plan)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "确认发送给第三方",
            PrimaryButtonText = "发送给 lelaer.com 并导入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = plan.PrivacyNotice +
                    Environment.NewLine + Environment.NewLine +
                    "确认后才会发出请求；取消不会向第三方发送任何内容，也不会改变本地数据库。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaTeyvatPrivacyDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task<string?> PickGameInstallationFolderAsync()
    {
        if (filePicker is null) return null;
        var folder = await filePicker.PickFolderAsync();
        return string.IsNullOrWhiteSpace(folder?.Path) ? null : folder.Path;
    }
}

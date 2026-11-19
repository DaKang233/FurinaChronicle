// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Gacha.Refreshing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private RefreshUnselectedGachaRecords? unselectedRefresh;

    private async Task RefreshWithoutAccountAsync(
        GachaReadOnlySnapshot selection, GachaRefreshSource source,
        string? manualUrl = null, string? gameFolder = null)
    {
        if (unselectedRefresh is null || importMode is null) return;
        var request = new GachaUnselectedRefreshRequest(
            source, importMode.Snapshot(), selection.ArchiveId, manualUrl, gameFolder);
        string archiveName = selection.Archives.FirstOrDefault(item => item.Id == selection.ArchiveId)?.Label
            ?? "新的未命名档案";
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "刷新并自动选择账号",
            PrimaryButtonText = "开始刷新", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBlock
            {
                Text = $"目标档案：{archiveName}\n" +
                    "将从抽卡接口识别角色；目标档案存在对应账号时使用该副本，否则随抽卡记录原子创建账号。" +
                    "没有可保存的新记录则不创建空档案或账号。\n" +
                    "包含新档案或账号的操作不能整体撤销；已有账号上的记录沿用既有撤销规则。\n" +
                    "模式：" + new GachaImportModeOption(request.Mode).Label +
                    "。本机永久删除记录继续抑制，不静默恢复。",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        AutomationProperties.SetAutomationId(confirm, "GachaAutomaticTargetDialog");
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        string taskKey = source == GachaRefreshSource.ManualUrl
            ? NotificationTaskKeys.GachaRefreshManual : NotificationTaskKeys.GachaRefreshWindowsCache;
        string title = source == GachaRefreshSource.ManualUrl ? "手动链接刷新" : "网页缓存刷新";
        PublishProgress(taskKey, title, "正在识别来源角色并刷新…");
        GachaUnselectedRefreshResult outcome;
        try
        {
            outcome = await WithRefreshProgressAsync(title,
                progress => unselectedRefresh.ExecuteAsync(request, progress),
                value => (true, value.Refresh));
        }
        catch (Exception exception)
        {
            ReportFailure(taskKey, "gacha.refresh.automatic-target", title + "失败",
                "自动识别或刷新未完成；未确认任何数据被修改，请检查来源和目标后重试。", exception);
            return;
        }
        await PublishTargetRefreshResultAsync(taskKey, title, outcome.Refresh, outcome.Target);
    }

    private async Task RefreshSTokenWithoutAccountAsync(GachaReadOnlySnapshot selection)
    {
        if (stokenRefresh is null || importMode is null) return;
        var mode = importMode.Snapshot();
        IReadOnlyList<GachaSTokenRoleChoice> roles = await stokenRefresh.GetRoleChoicesAsync();
        if (roles.Count == 0)
        {
            ShowInlineValidation("没有可用于抽卡刷新的登录角色；请先在“登录账号”页登录并读取原神角色。");
            return;
        }
        var picker = new ComboBox
        {
            Header = "登录账号与角色", ItemsSource = roles, DisplayMemberPath = nameof(GachaSTokenRoleChoice.Label),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(picker, "GachaAutomaticRole");
        var choose = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "选择抽卡刷新角色",
            PrimaryButtonText = "继续", CloseButtonText = "取消", Content = picker,
        };
        AutomationProperties.SetAutomationId(choose, "GachaAutomaticRoleDialog");
        if (await choose.ShowAsync() != ContentDialogResult.Primary ||
            picker.SelectedItem is not GachaSTokenRoleChoice role) return;
        var planned = await stokenRefresh.PlanRoleAsync(role.PassportAccountId, role.Uid, selection.ArchiveId, mode);
        if (!planned.Succeeded || planned.Plan is not { } plan)
        {
            ShowInlineValidation(planned.FailureMessage ?? "无法准备刷新目标。");
            return;
        }
        var confirm = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "通过登录账号刷新",
            PrimaryButtonText = "开始刷新", CloseButtonText = "取消",
            Content = new TextBlock { Text = plan.ConfirmationText, TextWrapping = TextWrapping.Wrap },
        };
        AutomationProperties.SetAutomationId(confirm, "GachaSTokenRefreshDialog");
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        PublishProgress(NotificationTaskKeys.GachaRefreshSToken, "登录账号刷新", "正在使用已选角色刷新…");
        var outcome = await WithRefreshProgressAsync("登录账号刷新",
            progress => stokenRefresh.ExecuteAsync(plan, progress), value => (value.Succeeded, value.Result));
        if (!outcome.Succeeded || outcome.Result is null)
        {
            ReportFailure(NotificationTaskKeys.GachaRefreshSToken, "gacha.refresh.stoken",
                "登录账号刷新失败", outcome.Message);
            return;
        }
        await PublishTargetRefreshResultAsync(NotificationTaskKeys.GachaRefreshSToken,
            "登录账号刷新", outcome.Result, plan.TargetPreparation!, outcome.Message);
    }

    private Task PublishTargetRefreshResultAsync(
        string taskKey, string title, GachaRefreshResult result, PreparedGachaRefreshTarget target,
        string? resultMessage = null)
    {
        bool created = target.AccountToCreate is not null && result.InsertedCount > 0;
        bool exists = target.AccountToCreate is null || created;
        string parents = created ? "已随记录创建并选择目标账号；本次操作包含新父对象，不能整体撤销。"
            : exists ? "已选择目标档案内的账号副本。"
            : "未产生可保存的新记录，未创建空档案或账号。";
        return PublishCommittedResultAsync(taskKey, title + "完成",
            (resultMessage ?? DescribeRefresh(result)) + " " + parents,
            exists ? target.Account.PlayerArchiveId : null,
            exists ? target.Account.Id : null);
    }
}

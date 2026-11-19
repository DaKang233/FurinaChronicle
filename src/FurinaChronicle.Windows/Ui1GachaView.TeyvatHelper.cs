// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private async Task ImportFromTeyvatHelperAsync()
    {
        if (teyvatImport is null || state?.Snapshot is not { IsBusy: false, ArchiveId: Guid archiveId } snapshot)
        {
            ShowInlineValidation("请先选择档案并等待当前读取结束。");
            return;
        }
        IReadOnlyList<GachaSTokenRoleChoice> roles;
        try { roles = await teyvatImport.GetAutomaticRolesAsync(); }
        catch (Exception) { roles = []; } // Optional login availability must not block manual input.
        if (!entered) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "从提瓦特小助手导入",
            PrimaryButtonText = "自动导入", SecondaryButtonText = "手动导入", CloseButtonText = "取消",
            IsPrimaryButtonEnabled = roles.Count > 0,
            DefaultButton = roles.Count > 0 ? ContentDialogButton.Primary : ContentDialogButton.Secondary,
            Content = new TextBlock
            {
                Text = "自动导入：选择已登录国服角色，执行时生成新的抽卡链接。\n" +
                    "手动导入：自行填写 UID 和链接，不需要登录账号或选择角色。\n" +
                    "两种方式都会在发送前说明第三方接收的信息，下载后仍需确认本地导入预览。",
                TextWrapping = TextWrapping.Wrap,
            },
        };
        AutomationProperties.SetAutomationId(dialog, "GachaTeyvatModeDialog");
        var choice = await dialog.ShowAsync();
        if (choice == ContentDialogResult.Secondary) await ImportTeyvatManuallyAsync();
        else if (choice == ContentDialogResult.Primary)
            await ImportTeyvatAutomaticallyAsync(snapshot, archiveId, roles);
    }

    private async Task ImportTeyvatAutomaticallyAsync(
        GachaReadOnlySnapshot snapshot, Guid archiveId, IReadOnlyList<GachaSTokenRoleChoice> roles)
    {
        if (teyvatImport is null || importMode is null || importCoordinator is null) return;
        GachaImportMode mode = importMode.Snapshot();
        var rolePicker = new ComboBox
        {
            Header = "登录账号与国服角色", ItemsSource = roles, DisplayMemberPath = nameof(GachaSTokenRoleChoice.Label),
            SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(rolePicker, "GachaTeyvatRole");
        var choose = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "选择自动导入角色", PrimaryButtonText = "继续",
            CloseButtonText = "取消", Content = rolePicker,
        };
        AutomationProperties.SetAutomationId(choose, "GachaTeyvatRoleDialog");
        if (await choose.ShowAsync() != ContentDialogResult.Primary ||
            rolePicker.SelectedItem is not GachaSTokenRoleChoice role) return;
        var planned = await teyvatImport.PlanAutomaticAsync(role.PassportAccountId, role.Uid, archiveId, mode);
        if (!planned.Succeeded || planned.Plan is not { } plan)
        {
            ShowInlineValidation(planned.FailureMessage ?? "无法准备自动导入；未发出请求。");
            return;
        }
        GachaImportTargetRequest? target = await ChooseImportTargetAsync(snapshot, archiveId);
        if (target is null || !entered || !await ConfirmTeyvatPrivacyAsync(plan)) return;
        PublishProgress(NotificationTaskKeys.GachaImportTeyvat, "从提瓦特小助手导入",
            "正在生成新链接并向 lelaer.com 请求 UIGF…");
        var download = await teyvatImport.DownloadAutomaticAsync(plan);
        await ImportTeyvatPayloadAsync(download, snapshot, archiveId, mode, target, plan.Uid);
    }

    private async Task ImportTeyvatPayloadAsync(
        GachaTeyvatHelperImportOutcome download, GachaReadOnlySnapshot snapshot, Guid archiveId,
        GachaImportMode mode, GachaImportTargetRequest target, string expectedUid)
    {
        if (!download.Succeeded || download.Content is null)
        {
            ReportFailure(NotificationTaskKeys.GachaImportTeyvat, "gacha.import.teyvat",
                "提瓦特小助手导入失败", download.FailureMessage ?? "下载未完成，未写入任何数据。");
            return;
        }
        byte[] content = download.Content;
        var source = new GachaImportSource(download.FileName ?? "teyvat-helper-uigf.json",
            _ => Task.FromResult<Stream>(new MemoryStream(content, writable: false)));
        await ImportPreparedSourceAsync(source, snapshot, archiveId, mode, CancellationToken.None,
            NotificationTaskKeys.GachaImportTeyvat, "从提瓦特小助手导入", target, expectedUid);
    }
}

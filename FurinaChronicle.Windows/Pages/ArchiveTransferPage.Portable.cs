// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Portable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ArchiveTransferPage
{
    private bool portableBusy;
    private async void OnExportPortable(object sender, RoutedEventArgs e) =>
        await ExportDomainAsync(markdown: false);

    private async void OnExportDomainMarkdown(object sender, RoutedEventArgs e) =>
        await ExportDomainAsync(markdown: true);

    private async Task ExportDomainAsync(bool markdown)
    {
        if (PortableArchivePicker.SelectedItem is not PlayerArchive archive)
        {
            PortableStatus.Text = "请选择要导出的档案。";
            notifications.Publish(new(
                NotificationSeverity.Warning,
                NotificationCategory.Archive,
                "导出分类数据",
                PortableStatus.Text));
            return;
        }

        var kind = PortableKindPicker.SelectedIndex == 1
            ? ArchiveDomainDatasetKind.ChallengeHistory
            : ArchiveDomainDatasetKind.CharacterWeaponHistory;
        string slug = kind == ArchiveDomainDatasetKind.ChallengeHistory ? "challenges" : "characters-weapons";
        await RunPortableAsync(async () =>
        {
            // Generate the whole file before prompting for a destination.
            string temporary = Path.GetTempFileName();
            try
            {
                await using (var output = File.OpenWrite(temporary))
                {
                    if (markdown)
                    {
                        await using var writer = new StreamWriter(output);
                        await portable.ExportMarkdownAsync(archive.Id, kind, writer);
                    }
                    else
                    {
                        await portable.ExportAsync(archive.Id, kind, output, DateTimeOffset.UtcNow);
                    }
                }

                StorageFile? destination = await filePicker.PickSaveFileAsync(
                    $"furina-{slug}-{DateTime.Now:yyyyMMdd-HHmmss}",
                    markdown ? "Markdown" : "Furina 分类 Portable",
                    markdown ? ".md" : ".json");
                if (destination is null)
                {
                    PortableStatus.Text = "已取消导出。";
                    return;
                }

                StorageFile staged = await StorageFile.GetFileFromPathAsync(temporary);
                await staged.CopyAndReplaceAsync(destination);
                PortableStatus.Text = $"已导出 {destination.Name}。";
                notifications.Publish(new(
                    NotificationSeverity.Success,
                    NotificationCategory.Archive,
                    markdown ? "导出分类 Markdown" : "导出分类 Portable",
                    PortableStatus.Text));
            }
            finally
            {
                File.Delete(temporary);
            }
        });
    }

    private async void OnImportPortable(object sender, RoutedEventArgs e)
    {
        if (portableBusy) return;
        await RunPortableAsync(async () =>
        {
            StorageFile? file = await filePicker.PickOpenFileAsync(".json");
            if (file is null) return;
            await using Stream source = await file.OpenStreamForReadAsync();
            ArchiveDomainPortablePackage package = await portable.ReadAsync(source);
            var targetPicker = new ComboBox { Header = "导入目标", HorizontalAlignment = HorizontalAlignment.Stretch };
            targetPicker.Items.Add("按来源档案名称匹配或创建");
            IReadOnlyList<PlayerArchive> targets = state.Snapshot.Archives;
            foreach (PlayerArchive archive in targets)
            {
                targetPicker.Items.Add(archive.Name);
            }
            targetPicker.SelectedIndex = 0;
            if (!await ConfirmPortableAsync("选择导入目标", "继续预览", targetPicker)) return;
            int targetIndex = targetPicker.SelectedIndex - 1;
            PlayerArchive? target = targetIndex >= 0 ? targets[targetIndex] : null;
            ArchiveDomainPortablePlan plan = await portable.PlanAsync(package, target?.Id);
            string summary = $"来源档案：{package.SourceArchiveName}\n" +
                $"目标：{target?.Name ?? package.SourceArchiveName}（{(plan.CreateArchive ? "新建" : "现有")}）\n" +
                $"账号映射：{plan.Accounts.Count}，新增：{plan.AddCount}，跳过：{plan.SkipCount}，冲突：{plan.ConflictCount}";
            if (plan.ConflictCount > 0)
            {
                await ShowMessageAsync("导入存在冲突", summary + "\n" + string.Join("\n", plan.Conflicts.Take(5)));
                notifications.Publish(new(
                    NotificationSeverity.Warning,
                    NotificationCategory.Archive,
                    "分类 Portable 导入",
                    $"导入存在 {plan.ConflictCount} 项冲突，未提交。"));
                return;
            }
            if (!await ConfirmPortableAsync("确认分类 Portable 导入", "导入",
                new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap })) return;

            var request = new ArchiveDomainPortableApplyRequest(
                OperationId.New(), package, plan, DateTimeOffset.UtcNow, "分类 Portable 导入");
            ArchiveOperationResult result = await portableApplier.ApplyArchiveDomainPortableAsync(request);
            if (result.Status == ChangeExecutionStatus.NeedsConfirmation && result.CleanupPlan is { } cleanup)
            {
                var details = new TextBlock
                {
                    Text = $"需要回收较早的撤销材料以继续。可回收 {cleanup.ReclaimableBytes} 字节。",
                    TextWrapping = TextWrapping.Wrap
                };
                if (!await ConfirmPortableAsync("撤销材料空间不足", "清理并重试", details)) return;
                result = await portableApplier.ApplyArchiveDomainPortableAsync(
                    request with { CleanupConfirmationId = cleanup.ConfirmationId });
            }

            if (result.Status == ChangeExecutionStatus.Applied)
            {
                PortableStatus.Text = $"导入完成，写入 {result.AffectedRecordCount} 项。";
                notifications.Publish(new(
                    NotificationSeverity.Success,
                    NotificationCategory.Archive,
                    "分类 Portable 导入",
                    PortableStatus.Text));
                RestoreApplied?.Invoke(this, EventArgs.Empty);
                try { await EnterAsync(); }
                catch { PortableStatus.Text += " 刷新档案列表失败，请重新打开此页。"; }
            }
            else
            {
                PortableStatus.Text = result.Status == ChangeExecutionStatus.NoOp
                    ? "没有新增记录，已有数据保持原样。"
                    : "导入未提交，请重新预览后重试。";
                notifications.Publish(new(
                    result.Status == ChangeExecutionStatus.NoOp
                        ? NotificationSeverity.Information
                        : NotificationSeverity.Warning,
                    NotificationCategory.Archive,
                    "分类 Portable 导入",
                    PortableStatus.Text));
            }
        });
    }

    private async Task RunPortableAsync(Func<Task> operation)
    {
        if (portableBusy || state.Snapshot.IsBusy) return;
        portableBusy = true;
        SetPortableButtons();
        PortableStatus.Text = string.Empty;
        try { await operation(); }
        catch (Exception exception)
        {
            PortableStatus.Text = $"分类数据操作未完成。{Presentation.Management.LogSanitizer.Sanitize(exception.Message)}";
            notifications.Publish(new(
                NotificationSeverity.Error,
                NotificationCategory.Archive,
                "分类数据操作",
                PortableStatus.Text));
        }
        finally
        {
            portableBusy = false;
            SetPortableButtons();
        }
    }

    private void SetPortableButtons()
    {
        bool enabled = !portableBusy && !state.Snapshot.IsBusy;
        ExportPortableButton.IsEnabled = enabled && state.Snapshot.Archives.Count > 0;
        ExportDomainMarkdownButton.IsEnabled = ExportPortableButton.IsEnabled;
        ImportPortableButton.IsEnabled = enabled;
    }

    private async Task<bool> ConfirmPortableAsync(string title, string confirm, FrameworkElement content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            PrimaryButtonText = confirm,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            Content = content
        };
        AutomationProperties.SetAutomationId(dialog, "DomainPortableConfirm");
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

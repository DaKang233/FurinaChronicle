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

    private void ShowPortableValidation(string message)
    {
        PortableValidationText.Text = message;
        PortableValidationText.Visibility = Visibility.Visible;
        AutomationProperties.SetName(PortableValidationText, $"分类数据提示：{message}");
    }

    private void ClearPortableValidation()
    {
        PortableValidationText.Text = string.Empty;
        PortableValidationText.Visibility = Visibility.Collapsed;
    }

    private async Task ExportDomainAsync(bool markdown)
    {
        if (PortableArchivePicker.SelectedItem is not PlayerArchive archive)
        {
            ShowPortableValidation("请选择要导出的档案。");
            return;
        }

        ClearPortableValidation();
        var kind = PortableKindPicker.SelectedIndex == 1
            ? ArchiveDomainDatasetKind.ChallengeHistory
            : ArchiveDomainDatasetKind.CharacterWeaponHistory;
        string slug = kind == ArchiveDomainDatasetKind.ChallengeHistory
            ? "challenges"
            : "characters-weapons";
        string taskKey = markdown
            ? NotificationTaskKeys.DomainMarkdownExport
            : NotificationTaskKeys.DomainPortableExport;
        string title = markdown ? "导出分类 Markdown" : "导出分类 Portable";
        await RunPortableAsync(taskKey, title, async () =>
        {
            PublishProgress(taskKey, title, "正在生成分类数据文件…");
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
                        await portable.ExportAsync(
                            archive.Id,
                            kind,
                            output,
                            DateTimeOffset.UtcNow);
                    }
                }

                StorageFile? destination = await filePicker.PickSaveFileAsync(
                    $"furina-{slug}-{DateTime.Now:yyyyMMdd-HHmmss}",
                    markdown ? "Markdown" : "Furina 分类 Portable",
                    markdown ? ".md" : ".json");
                if (destination is null)
                {
                    ClearProgress(taskKey);
                    return;
                }

                StorageFile staged = await StorageFile.GetFileFromPathAsync(temporary);
                await staged.CopyAndReplaceAsync(destination);
                Action? openFolder = CreateOpenFolderAction(destination.Path);
                PublishSuccess(
                    taskKey,
                    title + "完成",
                    $"已保存到 {DescribeSavedLocation(destination.Path, destination.Name)}。",
                    openFolder is null ? null : "打开文件夹",
                    openFolder);
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
        await RunPortableAsync(
            NotificationTaskKeys.DomainPortableImport,
            "导入分类 Portable",
            async () =>
            {
                StorageFile? file = await filePicker.PickOpenFileAsync(".json");
                if (file is null) return;
                PublishProgress(
                    NotificationTaskKeys.DomainPortableImport,
                    "导入分类 Portable",
                    "正在读取并校验分类 Portable…");
                await using Stream source = await file.OpenStreamForReadAsync();
                ArchiveDomainPortablePackage package = await portable.ReadAsync(source);
                var targetPicker = new ComboBox
                {
                    Header = "导入目标",
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                targetPicker.Items.Add("按来源档案名称匹配或创建");
                IReadOnlyList<PlayerArchive> targets = state.Snapshot.Archives;
                foreach (PlayerArchive archive in targets)
                {
                    targetPicker.Items.Add(archive.Name);
                }

                targetPicker.SelectedIndex = 0;
                if (!await ConfirmPortableAsync("选择导入目标", "继续预览", targetPicker))
                {
                    ClearProgress(NotificationTaskKeys.DomainPortableImport);
                    return;
                }

                int targetIndex = targetPicker.SelectedIndex - 1;
                PlayerArchive? target = targetIndex >= 0 ? targets[targetIndex] : null;
                ArchiveDomainPortablePlan plan = await portable.PlanAsync(package, target?.Id);
                string summary = $"来源档案：{package.SourceArchiveName}\n" +
                    $"目标：{target?.Name ?? package.SourceArchiveName}" +
                    $"（{(plan.CreateArchive ? "新建" : "现有")}）\n" +
                    $"账号映射：{plan.Accounts.Count}，新增：{plan.AddCount}，" +
                    $"跳过：{plan.SkipCount}，冲突：{plan.ConflictCount}";
                if (plan.ConflictCount > 0)
                {
                    await ShowPortableConflictPreviewAsync(
                        "导入存在冲突",
                        summary + Environment.NewLine +
                        string.Join(Environment.NewLine, plan.Conflicts.Take(5)));
                    ReportWarning(
                        NotificationTaskKeys.DomainPortableImport,
                        "分类 Portable 导入未提交",
                        $"导入存在 {plan.ConflictCount} 项冲突，未提交。");
                    return;
                }

                if (!await ConfirmPortableAsync(
                    "确认分类 Portable 导入",
                    "导入",
                    new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap }))
                {
                    ClearProgress(NotificationTaskKeys.DomainPortableImport);
                    return;
                }

                PublishProgress(
                    NotificationTaskKeys.DomainPortableImport,
                    "导入分类 Portable",
                    "正在以原子变更集写入分类 Portable…");
                var request = new ArchiveDomainPortableApplyRequest(
                    OperationId.New(),
                    package,
                    plan,
                    DateTimeOffset.UtcNow,
                    "分类 Portable 导入");
                ArchiveOperationResult result =
                    await portableApplier.ApplyArchiveDomainPortableAsync(request);
                if (result.Status == ChangeExecutionStatus.NeedsConfirmation &&
                    result.CleanupPlan is { } cleanup)
                {
                    var details = new TextBlock
                    {
                        Text = $"需要回收较早的撤销材料以继续。可回收 {cleanup.ReclaimableBytes} 字节。",
                        TextWrapping = TextWrapping.Wrap
                    };
                    if (!await ConfirmPortableAsync("撤销材料空间不足", "清理并重试", details))
                    {
                        ClearProgress(NotificationTaskKeys.DomainPortableImport);
                        return;
                    }

                    result = await portableApplier.ApplyArchiveDomainPortableAsync(
                        request with { CleanupConfirmationId = cleanup.ConfirmationId });
                }

                if (result.Status == ChangeExecutionStatus.Applied)
                {
                    RestoreApplied?.Invoke(this, EventArgs.Empty);
                    bool refreshed = await RefreshPortableImportAsync();
                    if (refreshed)
                    {
                        PublishSuccess(
                            NotificationTaskKeys.DomainPortableImport,
                            "分类 Portable 导入完成",
                            $"已导入 {result.AffectedRecordCount} 项；目标：{target?.Name ?? package.SourceArchiveName}。");
                    }
                    else
                    {
                        PublishWarning(
                            NotificationTaskKeys.DomainPortableImport,
                            "分类 Portable 已导入",
                            $"已导入 {result.AffectedRecordCount} 项，列表更新失败；数据已保存，只需重新加载显示。",
                            "重新加载",
                            CreateReloadAction());
                    }
                }
                else if (result.Status == ChangeExecutionStatus.NoOp)
                {
                    PublishSuccess(
                        NotificationTaskKeys.DomainPortableImport,
                        "分类 Portable 导入完成",
                        "没有新增记录，已有数据保持原样。");
                }
                else
                {
                    ReportFailure(
                        NotificationTaskKeys.DomainPortableImport,
                        "archive.domain.portable.import",
                        "分类 Portable 导入未提交",
                        result.Status == ChangeExecutionStatus.Conflict
                            ? "目标状态或预览已变化，请重新导入；未写入任何数据。"
                            : "导入未提交，请重新预览后重试。");
                }
            });
    }

    private async Task<bool> RefreshPortableImportAsync()
    {
        try
        {
            await state.EnterAsync();
            Render();
            return string.IsNullOrWhiteSpace(state.Snapshot.Error);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task ShowPortableConflictPreviewAsync(string title, string details)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            CloseButtonText = "关闭",
            XamlRoot = XamlRoot,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text = details,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "DomainPortableConflictDialog");
        await dialog.ShowAsync();
    }

    private async Task RunPortableAsync(
        string taskKey,
        string operationTitle,
        Func<Task> operation)
    {
        if (portableBusy || state.Snapshot.IsBusy) return;
        portableBusy = true;
        SetPortableButtons();
        ClearPortableValidation();
        try
        {
            await operation();
        }
        catch (Exception exception)
        {
            ReportFailure(
                taskKey,
                taskKey,
                operationTitle + "失败",
                "分类数据操作未完成；未确认写入的数据请重新载入核对。",
                exception);
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

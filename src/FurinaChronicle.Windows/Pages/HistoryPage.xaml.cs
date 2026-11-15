// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Presentation.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class HistoryPage : UserControl
{
    private readonly INotificationCenter notifications;
    private readonly IDiagnosticService diagnostics;
    private readonly ArchiveHistoryState state;
    private bool renderGuard;
    private string? lastLoadError;

    public HistoryPage(
        ArchiveHistoryState state,
        INotificationCenter notifications,
        IDiagnosticService diagnostics)
    {
        this.state = state;
        this.notifications = notifications;
        this.diagnostics = diagnostics;
        InitializeComponent();
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await state.EnterAsync(cancellationToken);
            Render();
        }
        catch (Exception exception)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Archive,
                "history-load",
                "操作历史读取失败",
                "操作历史暂未载入；可重试。",
                DiagnosticSeverity.Error,
                exception));
        }
    }

    private void Render()
    {
        renderGuard = true;
        try
        {
            ArchiveHistorySnapshot snapshot = state.Snapshot;
            ReportLoadError(snapshot.Error);
            ArchivePicker.ItemsSource = snapshot.Archives;
            ArchivePicker.SelectedItem = snapshot.Archives.FirstOrDefault(
                archive => archive.Id == snapshot.SelectedArchiveId);
            HistoryList.ItemsSource = snapshot.Entries;
            HistoryList.SelectedItem = snapshot.Entries.FirstOrDefault(
                entry => entry.ChangeSetId == snapshot.SelectedChangeSetId);
            RevisionList.ItemsSource = snapshot.Revisions;
            RevisionStatusText.Text = snapshot.RevisionStatus;
            UndoLimitBox.Value = snapshot.UndoLimit;
            bool idle = !snapshot.IsBusy;
            UndoLatestButton.IsEnabled = idle && snapshot.LatestUndoCandidate is not null;
            ApplyUndoLimitButton.IsEnabled = idle && snapshot.SelectedArchiveId is not null;
            ReloadButton.IsEnabled = idle;
            HistoryList.IsEnabled = idle;
        }
        finally
        {
            renderGuard = false;
        }
    }

    private async void OnArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || ArchivePicker.SelectedItem is not PlayerArchive archive) return;
        await state.SelectArchiveAsync(archive.Id);
        Render();
    }

    private async void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || HistoryList.SelectedItem is not ArchiveHistoryEntry entry) return;
        await state.SelectOperationAsync(entry.ChangeSetId);
        Render();
    }

    private async void OnUndoLatest(object sender, RoutedEventArgs e)
    {
        ArchiveHistoryEntry? candidate = state.Snapshot.LatestUndoCandidate;
        if (candidate is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "撤销最新可撤销操作",
            PrimaryButtonText = "撤销",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = state.BuildUndoConfirmationText(),
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4HistoryUndoDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        ArchiveHistoryActionResult result = await state.UndoLatestAsync();
        Render();
        ReportResult("撤销操作", NotificationTaskKeys.HistoryUndo, result);
    }

    private async void OnApplyUndoLimit(object sender, RoutedEventArgs e)
    {
        double value = UndoLimitBox.Value;
        if (double.IsNaN(value))
        {
            UndoLimitValidation.Text = "请输入 0～1000 的整数。";
            UndoLimitValidation.Visibility = Visibility.Visible;
            return;
        }

        UndoLimitValidation.Visibility = Visibility.Collapsed;
        ArchiveHistoryActionResult result = await state.SetUndoLimitAsync((int)value);
        Render();
        ReportResult("设置撤销保留次数", NotificationTaskKeys.HistoryUndoLimit, result);
    }

    private async void OnReload(object sender, RoutedEventArgs e) => await EnterAsync();

    private void ReportLoadError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error) || error == lastLoadError)
        {
            return;
        }
        lastLoadError = error;
        diagnostics.Report(new DiagnosticEvent(
            NotificationCategory.Archive,
            "history-load",
            "操作历史读取失败",
            error,
            DiagnosticSeverity.Error));
    }

    private void ReportResult(string operation, string taskKey, ArchiveHistoryActionResult result)
    {
        if (!result.Succeeded)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Archive,
                "history",
                operation,
                result.Message,
                DiagnosticSeverity.Warning)
            {
                TaskKey = taskKey,
            });
            return;
        }

        notifications.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Archive,
            operation,
            result.Message)
        {
            TaskKey = taskKey,
        });
    }
}

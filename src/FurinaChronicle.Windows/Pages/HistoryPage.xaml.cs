// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class HistoryPage : UserControl
{
    private readonly ArchiveHistoryState state;
    private bool renderGuard;

    public HistoryPage(ArchiveHistoryState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        renderGuard = true;
        try
        {
            ArchiveHistorySnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
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
            await ShowMessageAsync("没有可撤销操作", "当前档案没有仍满足撤销资格的操作。");
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
        await ShowMessageAsync("撤销结果", result.Message);
    }

    private async void OnApplyUndoLimit(object sender, RoutedEventArgs e)
    {
        double value = UndoLimitBox.Value;
        if (double.IsNaN(value))
        {
            await ShowMessageAsync("撤销保留次数无效", "请输入 0～1000 的整数。");
            return;
        }

        ArchiveHistoryActionResult result = await state.SetUndoLimitAsync((int)value);
        Render();
        if (!result.Succeeded)
        {
            await ShowMessageAsync("撤销保留次数未修改", result.Message);
        }
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        await state.EnterAsync();
        Render();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定"
        };
        await dialog.ShowAsync();
    }
}

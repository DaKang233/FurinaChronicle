// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class OverviewPage : UserControl
{
    private readonly OverviewState state;

    public OverviewPage(OverviewState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public event EventHandler? ContinueRequested;

    public event EventHandler? BackupRestoreRequested;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        OverviewSnapshot snapshot = state.Snapshot;
        StatusText.Text = snapshot.Status;
        ErrorText.Text = snapshot.Error ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
            ? Visibility.Collapsed
            : Visibility.Visible;
        ArchiveCountText.Text = snapshot.ArchiveCount.ToString();
        AccountCountText.Text = snapshot.AccountCount.ToString();
        RecordCountText.Text = snapshot.TotalGachaRecords.ToString();
        UnresolvedCountText.Text = snapshot.UnresolvedCount.ToString();
        ArchiveList.ItemsSource = snapshot.Archives;
        RecentList.ItemsSource = snapshot.RecentActivities;
        NotesText.Text = string.Join(Environment.NewLine, snapshot.CoverageNotes);
        ContinueButton.Content = snapshot.ContinueTargetLabel;
        AutomationProperties.SetName(ContinueButton, snapshot.ContinueTargetLabel);
        ReloadButton.IsEnabled = !snapshot.IsBusy;
        AutomationProperties.SetName(
            StatusText,
            snapshot.IsBusy ? "概览正在载入" : "概览状态");
    }

    private void OnContinue(object sender, RoutedEventArgs e) =>
        ContinueRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackupRestore(object sender, RoutedEventArgs e) =>
        BackupRestoreRequested?.Invoke(this, EventArgs.Empty);

    private async void OnReload(object sender, RoutedEventArgs e) =>
        await EnterAsync();
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Presentation.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class OverviewPage : UserControl
{
    private readonly IDiagnosticService diagnostics;
    private readonly OverviewState state;
    private string? lastLoadError;

    public OverviewPage(OverviewState state, IDiagnosticService diagnostics)
    {
        this.state = state;
        this.diagnostics = diagnostics;
        InitializeComponent();
    }

    public event EventHandler? ContinueRequested;

    public event EventHandler? BackupRestoreRequested;

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
                NotificationCategory.System,
                "overview-load",
                "概览读取失败",
                "概览暂未载入；可重试。",
                DiagnosticSeverity.Error,
                exception));
        }
    }

    private void Render()
    {
        OverviewSnapshot snapshot = state.Snapshot;
        ReportLoadError(snapshot.Error);
        ArchiveCountText.Text = snapshot.ArchiveCount.ToString();
        AccountCountText.Text = snapshot.AccountCount.ToString();
        RecordCountText.Text = snapshot.TotalGachaRecords.ToString();
        UnresolvedCountText.Text = snapshot.UnresolvedCount.ToString();
        ArchiveList.ItemsSource = snapshot.Archives;
        RecentList.ItemsSource = snapshot.RecentActivities;
        ContinueButton.Content = snapshot.ContinueTargetLabel;
        AutomationProperties.SetName(ContinueButton, snapshot.ContinueTargetLabel);
        ReloadButton.IsEnabled = !snapshot.IsBusy;
    }

    private void ReportLoadError(string? error)
    {
        if (string.IsNullOrWhiteSpace(error) || error == lastLoadError)
        {
            return;
        }
        lastLoadError = error;
        diagnostics.Report(new DiagnosticEvent(
            NotificationCategory.System,
            "overview-load",
            "概览读取失败",
            error,
            DiagnosticSeverity.Error));
    }

    private void OnContinue(object sender, RoutedEventArgs e) =>
        ContinueRequested?.Invoke(this, EventArgs.Empty);

    private void OnBackupRestore(object sender, RoutedEventArgs e) =>
        BackupRestoreRequested?.Invoke(this, EventArgs.Empty);

    private async void OnReload(object sender, RoutedEventArgs e) =>
        await EnterAsync();
}

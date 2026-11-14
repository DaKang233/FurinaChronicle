// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class StartupPage : UserControl
{
    public StartupPage(bool isIsolated)
    {
        InitializeComponent();
        StartupIntro.Text = isIsolated
            ? "正在打开隔离合成数据库；本宿主不会发现生产库或凭据。"
            : "正在检查 Windows 业务数据库与接管状态。";
    }

    public event EventHandler? RetryRequested;

    public void Render(StartupSnapshot snapshot)
    {
        Progress.IsActive = snapshot.IsBusy;
        StatusText.Text = snapshot.IsReady
            ? snapshot.Status
            : snapshot.HasFailed
                ? $"{snapshot.FailureKindLabel}：{snapshot.Status}"
                : snapshot.Status;
        DetailText.Text = snapshot.Detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrWhiteSpace(snapshot.Detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        RetryButton.Visibility = snapshot.CanRetry ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.IsEnabled = !snapshot.IsBusy;
        AutomationProperties.SetName(
            StatusText,
            snapshot.HasFailed
                ? $"启动失败：{snapshot.FailureKindLabel}"
                : snapshot.IsBusy ? "正在初始化" : "启动状态");
    }

    public void SetVersion(string versionText) => VersionText.Text = versionText;

    private void OnRetry(object sender, RoutedEventArgs e) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);
}

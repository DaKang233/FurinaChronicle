// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class HostAdoptionPage : UserControl
{
    private readonly HostAdoptionState state;

    public HostAdoptionPage(HostAdoptionState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public event EventHandler? Completed;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        HostAdoptionSnapshot snapshot = state.Snapshot;
        StatusText.Text = snapshot.Status;
        ErrorText.Text = snapshot.Error ?? string.Empty;
        ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
            ? Visibility.Collapsed
            : Visibility.Visible;
        Progress.IsActive = snapshot.IsBusy;
        DatabaseText.Text =
            $"数据库：{snapshot.Paths.DatabasePath}\n" +
            $"存在：{(snapshot.Database.Exists ? "是" : "否")}；" +
            $"大小：{(snapshot.Database.Exists ? $"{snapshot.Database.SizeBytes} B" : "无")}；" +
            $"最后写入：{(snapshot.Database.LastWriteUtc is { } time ? time.ToString("yyyy-MM-dd HH:mm:ss zzz") : "无")}";
        BackupText.Text = $"接管前强制备份目录：{snapshot.Paths.BackupsDirectory}";
        MarkerText.Text = snapshot.Marker is { } marker
            ? $"接管标记：{marker.Status}；时间 {marker.CompletedAtUtc:yyyy-MM-dd HH:mm:ss zzz}；" +
                $"备份 {marker.BackupFileName ?? "无（新库初始化）"}；" +
                $"数据库 SHA-256 {marker.DatabaseSha256[..Math.Min(16, marker.DatabaseSha256.Length)]}…"
            : "接管标记：尚未写入。";
        LegacyText.Text = snapshot.LegacyHostRunning
            ? "检测到旧 MAUI 宿主可能正在运行；请先关闭旧宿主，WinUI 与 MAUI 不可同时打开同一库。"
            : string.Empty;
        LegacyText.Visibility = snapshot.LegacyHostRunning
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdoptButton.Visibility = snapshot.Database.Exists && !snapshot.IsAdopted
            ? Visibility.Visible
            : Visibility.Collapsed;
        AdoptButton.IsEnabled = snapshot.Database.Exists &&
            !snapshot.IsAdopted && !snapshot.LegacyHostRunning && !snapshot.IsBusy;
        InitializeButton.Visibility = snapshot.CanInitialize
            ? Visibility.Visible
            : Visibility.Collapsed;
        InitializeButton.IsEnabled = snapshot.CanInitialize &&
            !snapshot.LegacyHostRunning && !snapshot.IsBusy;
        RetryButton.IsEnabled = !snapshot.IsBusy;
        RollbackText.Text = state.BuildRollbackText() + Environment.NewLine +
            state.CredentialsText;
    }

    private async void OnAdopt(object sender, RoutedEventArgs e)
    {
        HostAdoptionSnapshot snapshot = state.Snapshot;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "接管现有数据",
            PrimaryButtonText = "接管并使用同一数据库",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = $"数据库（原路径、同样文件）：{snapshot.Paths.DatabasePath}\n" +
                    $"接管前备份：{snapshot.Paths.BackupsDirectory}\n\n" +
                    "WinUI 将直接读写该数据库，不复制、不移动。原库不会被删除；" +
                    "接管前强制备份并校验。回退=关闭 WinUI 用旧 MAUI；凭据需重新授权。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui6AdoptionConfirmDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        HostAdoptionResult result = await state.AdoptAsync();
        Render();
        await ShowResultAsync(result);
        if (result.Succeeded) Completed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnInitialize(object sender, RoutedEventArgs e)
    {
        HostAdoptionSnapshot snapshot = state.Snapshot;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "初始化新生产库",
            PrimaryButtonText = "创建并使用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = $"将按 MAUI Schema 初始化：{snapshot.Paths.DatabasePath}\n" +
                    "不会播种合成数据；不会复制任何旧库。创建后写入脱敏接管标记。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui6AdoptionInitializeDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        HostAdoptionResult result = await state.InitializeAsync();
        Render();
        await ShowResultAsync(result);
        if (result.Succeeded) Completed?.Invoke(this, EventArgs.Empty);
    }

    private async void OnRetry(object sender, RoutedEventArgs e)
    {
        await state.EnterAsync();
        Render();
    }

    private async Task ShowResultAsync(HostAdoptionResult result)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = result.Succeeded ? "接管完成" : "接管未完成",
            CloseButtonText = "确定",
            Content = new TextBlock { Text = result.Message, TextWrapping = TextWrapping.Wrap }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui6AdoptionResultDialog");
        await dialog.ShowAsync();
    }
}

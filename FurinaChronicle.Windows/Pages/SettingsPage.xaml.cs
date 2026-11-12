// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class SettingsPage : UserControl
{
    private static readonly string[] ThemeLabels = ["跟随系统", "浅色", "深色"];

    private readonly SettingsState state;
    private readonly Action<SettingsTheme> applyTheme;
    private bool renderGuard;

    public SettingsPage(SettingsState state, Action<SettingsTheme> applyTheme)
    {
        this.state = state;
        this.applyTheme = applyTheme;
        InitializeComponent();
        ThemePicker.ItemsSource = ThemeLabels;
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        applyTheme(state.Snapshot.Theme);
        Render();
    }

    private void Render()
    {
        renderGuard = true;
        try
        {
            SettingsSnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ThemePicker.SelectedIndex = (int)snapshot.Theme;
            DataRootText.Text = snapshot.DataRootPath;
            DatabasePathText.Text = snapshot.DatabasePath;
            CachePathText.Text = snapshot.CacheDirectoryPath;
            DatabaseSizeText.Text = $"数据库：{snapshot.DatabaseSizeText}";
            CacheSizeText.Text = $"图标缓存：{snapshot.CacheSizeText}";
            HistoryArchivePicker.ItemsSource = snapshot.Archives;
            HistoryArchivePicker.SelectedItem = snapshot.Archives.FirstOrDefault(
                archive => archive.Id == snapshot.SelectedArchiveId);
            UndoLimitBox.Value = snapshot.UndoLimit;
            VersionText.Text =
                $"{snapshot.VersionText} · Phase {snapshot.PhaseLabel} · Commit {snapshot.Commit}";
            bool idle = !snapshot.IsBusy;
            ClearIconCacheButton.IsEnabled = idle;
            ApplyUndoLimitButton.IsEnabled = idle && snapshot.SelectedArchiveId is not null;
            LicenseButton.IsEnabled = idle;
            ThirdPartyButton.IsEnabled = idle;
            ExportDiagnosticsButton.IsEnabled = idle;
        }
        finally
        {
            renderGuard = false;
        }
    }

    private async void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || ThemePicker.SelectedIndex < 0) return;
        SettingsTheme theme = (SettingsTheme)ThemePicker.SelectedIndex;
        SettingsActionResult result = await state.SetThemeAsync(theme);
        if (result.Succeeded)
        {
            applyTheme(theme);
        }

        Render();
    }

    private async void OnClearIconCache(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除图标缓存",
            PrimaryButtonText = "清除缓存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "只清除可重建的图标缓存；抽卡事实、档案、账号和元数据不会被删除。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4SettingsClearCacheDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        SettingsActionResult result = await state.ClearIconCacheAsync();
        Render();
        if (!result.Succeeded) await ShowMessageAsync("缓存未清除", result.Message);
    }

    private async void OnHistoryArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || HistoryArchivePicker.SelectedItem is not PlayerArchive archive) return;
        await state.SelectArchiveAsync(archive.Id);
        Render();
    }

    private async void OnApplyUndoLimit(object sender, RoutedEventArgs e)
    {
        double value = UndoLimitBox.Value;
        if (double.IsNaN(value))
        {
            await ShowMessageAsync("撤销保留次数无效", "请输入 0～1000 的整数。");
            return;
        }

        SettingsActionResult result = await state.SetUndoLimitAsync((int)value);
        Render();
        if (!result.Succeeded) await ShowMessageAsync("撤销保留次数未修改", result.Message);
    }

    private async void OnLicense(object sender, RoutedEventArgs e) =>
        await ShowTextAsync("许可", state.LicenseText, "Ui4SettingsLicenseDialog");

    private async void OnThirdParty(object sender, RoutedEventArgs e) =>
        await ShowTextAsync("第三方声明", state.ThirdPartyNoticesText, "Ui4SettingsThirdPartyDialog");

    private async void OnExportDiagnostics(object sender, RoutedEventArgs e)
    {
        SettingsActionResult result = await state.ExportDiagnosticsAsync();
        Render();
        await ShowMessageAsync(result.Succeeded ? "诊断已导出" : "诊断导出失败", result.Message);
    }

    private async Task ShowTextAsync(string title, string text, string automationId)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            CloseButtonText = "关闭",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }
            }
        };
        AutomationProperties.SetAutomationId(dialog, automationId);
        await dialog.ShowAsync();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            CloseButtonText = "确定",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        };
        await dialog.ShowAsync();
    }
}

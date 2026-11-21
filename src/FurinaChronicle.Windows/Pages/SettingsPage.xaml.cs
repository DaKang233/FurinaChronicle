// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class SettingsPage : UserControl
{
    private readonly INotificationCenter notifications;
    private readonly IDiagnosticService diagnostics;
    private static readonly string[] ThemeLabels = ["跟随系统", "浅色", "深色"];

    private readonly SettingsState state;
    private readonly FurinaChronicle.Presentation.Ai.AiSettingsState aiState;
    private readonly Action<SettingsTheme> applyTheme;
    private bool renderGuard;
    private string? lastLoadError;

    public SettingsPage(
        SettingsState state,
        Action<SettingsTheme> applyTheme,
        INotificationCenter notifications,
        IDiagnosticService diagnostics,
        FurinaChronicle.Presentation.Ai.AiSettingsState aiState)
    {
        this.state = state;
        this.notifications = notifications;
        this.diagnostics = diagnostics;
        this.applyTheme = applyTheme;
        InitializeComponent();
        ThemePicker.ItemsSource = ThemeLabels;
        this.aiState = aiState;
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await state.EnterAsync(cancellationToken);
            applyTheme(state.Snapshot.Theme);
            Render();
        }
        catch (Exception exception)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.System,
                "settings-load",
                "设置读取失败",
                "设置暂未载入；可重试。",
                DiagnosticSeverity.Error,
                exception));
        }
    }

    private async void OnOpenAiSettings(object sender, RoutedEventArgs e)
    {
        if (!AiSettingsButton.IsEnabled) return;
        AiSettingsButton.IsEnabled = false;
        try { await AiSettingsDialog.ShowAsync(XamlRoot, aiState); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            notifications.Publish(new(NotificationSeverity.Warning, NotificationCategory.System,
                "AI 设置", "AI 设置暂未打开，请重试。"));
        }
        finally { AiSettingsButton.IsEnabled = true; }
    }

    private void Render()
    {
        renderGuard = true;
        try
        {
            SettingsSnapshot snapshot = state.Snapshot;
            ReportLoadError(snapshot.Error);
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
            VersionText.Text = snapshot.VersionText;
#if DEBUG
            VersionText.Text +=
                $" · Phase {snapshot.PhaseLabel} · Commit {snapshot.Commit}";
#endif
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
        else
        {
            ReportFailure("应用主题失败", result.Message);
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
        ReportActionResult(
            "清除图标缓存",
            NotificationTaskKeys.SettingsClearIconCache,
            result);
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
            UndoLimitValidation.Text = "请输入 0～1000 的整数。";
            UndoLimitValidation.Visibility = Visibility.Visible;
            return;
        }

        UndoLimitValidation.Visibility = Visibility.Collapsed;
        SettingsActionResult result = await state.SetUndoLimitAsync((int)value);
        Render();
        ReportActionResult(
            "设置撤销保留次数",
            NotificationTaskKeys.SettingsUndoLimit,
            result);
    }

    private async void OnLicense(object sender, RoutedEventArgs e) =>
        await ShowTextAsync("许可", state.LicenseText, "Ui4SettingsLicenseDialog");

    private async void OnThirdParty(object sender, RoutedEventArgs e) =>
        await ShowTextAsync("第三方声明", state.ThirdPartyNoticesText, "Ui4SettingsThirdPartyDialog");

    private async void OnExportDiagnostics(object sender, RoutedEventArgs e)
    {
        SettingsActionResult result = await state.ExportDiagnosticsAsync();
        Render();
        ReportActionResult(
            "导出诊断报告",
            NotificationTaskKeys.SettingsExportDiagnostics,
            result);
    }

    private void ReportActionResult(
        string operation,
        string taskKey,
        SettingsActionResult result)
    {
        if (!result.Succeeded)
        {
            ReportFailure(operation, result.Message, taskKey);
            return;
        }

        notifications.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.System,
            operation,
            result.Message)
        {
            TaskKey = taskKey,
        });
    }

    private void ReportFailure(string operation, string message, string? taskKey = null)
    {
        diagnostics.Report(new DiagnosticEvent(
            NotificationCategory.System,
            "settings",
            operation,
            message,
            DiagnosticSeverity.Warning)
        {
            TaskKey = taskKey,
        });
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
            "settings-load",
            "设置读取失败",
            error,
            DiagnosticSeverity.Warning));
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
}

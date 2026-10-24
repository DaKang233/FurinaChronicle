// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FurinaChronicle.Windows;

public sealed partial class MainWindow : Window
{
    private readonly IReadOnlyList<PreviewRecord> previewRecords =
    [
        new("受控角色记录", "虚构数据 · 角色活动祈愿", "已核验"),
        new("受控武器记录", "虚构数据 · 武器活动祈愿", "待查看"),
        new("未知来源示例", "明确保留未知状态，不伪造来源", "未知"),
        new("无新增结果", "核验完成，没有新增记录", "完成"),
        new("显示刷新失败", "数据保存与 UI 刷新结果分开表达", "需处理")
    ];

    public MainWindow()
    {
        InitializeComponent();
        PreviewList.ItemsSource = previewRecords;
        Navigation.SelectedItem = Navigation.MenuItems[0];
        IsolationPathText.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FurinaChronicle",
            "UI0",
            "WindowsPreview");

        RootGrid.Loaded += OnRootLoaded;
        RootGrid.ActualThemeChanged += OnActualThemeChanged;
        ApplyBackdrop(forceSolid: false);
        ResizeInitialWindow();
    }

    private void ResizeInitialWindow()
    {
        nint windowHandle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1100, 720));
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeStatus();
        PreviewList.Focus(FocusState.Programmatic);
    }

    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        UpdateThemeStatus();
    }

    private void UpdateThemeStatus()
    {
        ThemeStatusText.Text = $"实际主题：{RootGrid.ActualTheme}";
    }

    private void ApplyBackdrop(bool forceSolid)
    {
        if (!forceSolid)
        {
            try
            {
                RootGrid.Background = new SolidColorBrush(Colors.Transparent);
                SystemBackdrop = new MicaBackdrop();
                BackdropStatusText.Text =
                    "已请求 Mica。系统关闭透明、高对比度或不支持时由 WinUI 使用兼容回退。";
                return;
            }
            catch (Exception exception)
            {
                BackdropStatusText.Text =
                    $"Mica 不可用，已回退到系统纯色：{exception.GetType().Name}";
            }
        }

        SystemBackdrop = null;
        RootGrid.Background =
            (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        if (forceSolid)
        {
            BackdropStatusText.Text = "已强制使用系统纯色，用于验证无透明材质路径。";
        }
    }

    private void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        (SectionTitle.Text, SectionDescription.Text) = item.Tag?.ToString() switch
        {
            "theme" => (
                "主题与材质",
                "跟随系统主题，并提供确定性的纯色回退测试开关。"),
            "scope" => (
                "UI-0 边界",
                "此宿主不连接真实数据库、凭据或业务流程；后续纵切按页面独立保存范围。"),
            _ => (
                "组件样例",
                "独立 WinUI 3 宿主 · 仅使用虚构数据")
        };
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        ShowStatus("已完成受控刷新，没有访问网络或用户数据。", InfoBarSeverity.Success);
    }

    private async void OnPickFileClicked(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".json");
        picker.FileTypeFilter.Add(".zip");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSingleFileAsync();
        ShowStatus(
            file is null
                ? "文件选择已取消；未读取任何内容。"
                : $"已验证窗口关联：{file.Name}。技术小样未读取文件内容。",
            InfoBarSeverity.Informational);
    }

    private void OnExportClicked(object sender, RoutedEventArgs e)
    {
        ShowStatus("导出命令已触发；UI-0 不创建业务文件。", InfoBarSeverity.Informational);
    }

    private async void OnShowDialogClicked(object sender, RoutedEventArgs e)
    {
        var input = new TextBox
        {
            Header = "受控备注",
            PlaceholderText = "可用 Tab 导航，按 Esc 关闭",
            AcceptsReturn = true,
            MaxLength = 120,
            MinWidth = 320,
            TextWrapping = TextWrapping.Wrap
        };
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = "原生 ContentDialog 样例",
            Content = input,
            PrimaryButtonText = "确认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary
        };

        ContentDialogResult result = await dialog.ShowAsync();
        ShowStatus(
            result == ContentDialogResult.Primary
                ? "对话框已确认；未持久化输入。"
                : "对话框已关闭；Esc 与关闭按钮使用同一原生生命周期。",
            InfoBarSeverity.Informational);
        OpenDialogButton.Focus(FocusState.Programmatic);
    }

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        RootGrid.RequestedTheme = item.Tag?.ToString() switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        UpdateThemeStatus();
    }

    private void OnSolidFallbackToggled(object sender, RoutedEventArgs e)
    {
        ApplyBackdrop(SolidFallbackToggle.IsOn);
    }

    private void OnPreviewItemClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PreviewRecord record)
        {
            ShowStatus($"已选择：{record.Title}", InfoBarSeverity.Informational);
        }
    }

    private void OnMenuActionClicked(object sender, RoutedEventArgs e)
    {
        string label = (sender as MenuFlyoutItem)?.Text ?? "菜单命令";
        ShowStatus($"已触发：{label}。", InfoBarSeverity.Informational);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
        StatusInfoBar.IsOpen = true;
    }

    private sealed record PreviewRecord(string Title, string Detail, string Status);
}

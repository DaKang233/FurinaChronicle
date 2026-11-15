// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Notifications;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace FurinaChronicle.Windows.Shell;

/// <summary>
/// Global notification center docked to the bottom-right corner of the shell.
///
/// Design constraint from the maintainer: when there are no notifications the whole
/// overlay (panel and bell) is hidden, so it never blocks the page underneath. With
/// notifications present the panel starts collapsed as a small bell; warnings and
/// errors expand it automatically. The bell only occupies its own 52x52 area.
/// </summary>
public sealed partial class NotificationCenterPanel : UserControl
{
    private readonly INotificationCenter center;
    private readonly IDiagnosticService? diagnostics;
    private readonly IDiagnosticReportExporter? exporter;
    private bool expanded;
    private Guid? lastNotificationId;

    public NotificationCenterPanel(
        INotificationCenter center,
        IDiagnosticService? diagnostics = null,
        IDiagnosticReportExporter? exporter = null)
    {
        this.center = center;
        this.diagnostics = diagnostics;
        this.exporter = exporter;
        InitializeComponent();
        center.Changed += OnCenterChanged;
        Refresh();
    }

    /// <summary>Keeps the panel usable in narrow windows without covering the page.</summary>
    public void SetAvailableWidth(double rootWidth)
    {
        if (rootWidth <= 0)
        {
            return;
        }
        PanelCard.Width = Math.Clamp(rootWidth - 32, 240, 380);
    }

    private void OnCenterChanged(object? sender, EventArgs e)
    {
        AppNotification? latest = center.Latest();
        if (latest is not null && latest.Id != lastNotificationId)
        {
            lastNotificationId = latest.Id;
            if (latest.Severity is NotificationSeverity.Warning or NotificationSeverity.Error)
            {
                expanded = true;
                Announce(latest);
            }
        }
        Refresh();
    }

    private void Refresh()
    {
        NotificationList.ItemsSource = center.Items
            .Select(NotificationRow.From)
            .ToList();
        bool empty = center.Items.Count == 0;
        if (empty)
        {
            expanded = false;
        }
        Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        UnreadText.Text = center.UnreadCount > 0
            ? $"{center.UnreadCount} 条未读"
            : "无未读";
        MarkAllReadButton.IsEnabled = center.UnreadCount > 0;
        ClearButton.IsEnabled = !empty;
        ExportDiagnosticsButton.IsEnabled = exporter is not null;
        ExportDiagnosticsButton.Visibility = exporter is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        BellBadge.Visibility = center.UnreadCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        BellBadgeText.Text = center.UnreadCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        ApplyExpandedState();
    }

    private void ApplyExpandedState()
    {
        PanelCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        OpenButton.Visibility = expanded || center.Items.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e)
    {
        expanded = true;
        ApplyExpandedState();
        NotificationList.Focus(FocusState.Programmatic);
    }

    private void OnCollapseClicked(object sender, RoutedEventArgs e)
    {
        expanded = false;
        ApplyExpandedState();
        OpenButton.Focus(FocusState.Programmatic);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == global::Windows.System.VirtualKey.Escape && expanded)
        {
            expanded = false;
            ApplyExpandedState();
            OpenButton.Focus(FocusState.Programmatic);
            e.Handled = true;
        }
    }

    private void OnMarkAllReadClicked(object sender, RoutedEventArgs e) =>
        center.MarkAllRead();

    private void OnClearClicked(object sender, RoutedEventArgs e) => center.Clear();

    private void OnDismissClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Guid id })
        {
            center.Remove(id);
        }
    }

    private void OnActionClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: NotificationRow row })
        {
            return;
        }
        center.MarkRead(row.Id);
        try
        {
            row.Action?.Invoke();
        }
        catch (Exception exception)
        {
            diagnostics?.Report(new DiagnosticEvent(
                NotificationCategory.System,
                "notification-action",
                "通知操作失败",
                "通知中的操作无法继续执行；可稍后重试。",
                DiagnosticSeverity.Warning,
                exception));
        }
    }

    private async void OnExportDiagnosticsClicked(object sender, RoutedEventArgs e)
    {
        if (exporter is null)
        {
            return;
        }
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            string? path = await exporter.ExportAsync();
            if (path is null)
            {
                return;
            }
            center.Publish(new NotificationRequest(
                NotificationSeverity.Success,
                NotificationCategory.System,
                "诊断报告已导出",
                $"已保存到：{path}"));
        }
        catch (Exception exception)
        {
            if (diagnostics is not null)
            {
                diagnostics.Report(new DiagnosticEvent(
                    NotificationCategory.System,
                    "diagnostic-export",
                    "诊断报告导出失败",
                    "无法保存诊断报告；错误已记录，可重试。",
                    DiagnosticSeverity.Error,
                    exception));
            }
            else
            {
                center.Publish(new NotificationRequest(
                    NotificationSeverity.Error,
                    NotificationCategory.System,
                    "诊断报告导出失败",
                    "无法保存诊断报告，可重试。"));
            }
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }

    private void Announce(AppNotification notification)
    {
        try
        {
            AutomationPeer? peer = FrameworkElementAutomationPeer.CreatePeerForElement(this);
            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.ActionCompleted,
                AutomationNotificationProcessing.ImportantMostRecent,
                $"{notification.Title}：{notification.Message}",
                "furina.notification");
        }
        catch (Exception)
        {
            // Screen-reader announcement must never break the notification pipeline.
        }
    }
}

public sealed record NotificationRow(
    Guid Id,
    string Title,
    string Message,
    string CategoryText,
    string TimeText,
    string RepeatText,
    string Glyph,
    Brush Accent,
    Visibility UnreadVisibility,
    string? ActionLabel,
    Action? Action,
    string ProgressText,
    Visibility ProgressVisibility,
    string DiagnosticText,
    Visibility DiagnosticVisibility)
{
    public Visibility ActionVisibility => Action is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public static NotificationRow From(AppNotification notification) => new(
        notification.Id,
        notification.Title,
        notification.Message,
        notification.Category switch
        {
            NotificationCategory.System => "系统",
            NotificationCategory.Archive => "档案",
            NotificationCategory.Collection => "采集",
            NotificationCategory.Passport => "登录",
            NotificationCategory.Gacha => "抽卡",
            NotificationCategory.Chronicle => "游戏历程",
            _ => "设置",
        },
        notification.OccurredAt.ToLocalTime().ToString("MM-dd HH:mm"),
        notification.RepeatCount > 1 ? $"×{notification.RepeatCount}" : string.Empty,
        notification.IsInProgress
            ? "\uE895"
            : notification.Severity switch
            {
                NotificationSeverity.Success => "\uE73E",
                NotificationSeverity.Warning => "\uE7BA",
                NotificationSeverity.Error => "\uEA39",
                _ => "\uE946",
            },
        new SolidColorBrush(notification.Severity switch
        {
            NotificationSeverity.Success => Colors.SeaGreen,
            NotificationSeverity.Warning => Colors.Orange,
            NotificationSeverity.Error => Colors.IndianRed,
            _ => Colors.SteelBlue,
        }),
        notification.IsRead ? Visibility.Collapsed : Visibility.Visible,
        notification.ActionLabel,
        notification.Action,
        notification.IsInProgress ? "进行中…" : string.Empty,
        notification.IsInProgress ? Visibility.Visible : Visibility.Collapsed,
        string.IsNullOrWhiteSpace(notification.DiagnosticId)
            ? string.Empty
            : $"诊断编号 {notification.DiagnosticId}",
        string.IsNullOrWhiteSpace(notification.DiagnosticId)
            ? Visibility.Collapsed
            : Visibility.Visible);
}

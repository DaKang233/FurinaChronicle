// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Notifications;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FurinaChronicle.Windows.Shell;

/// <summary>
/// Global notification center docked to the bottom-right corner of the shell.
/// It stays above every page; the bell stays available when the panel is
/// collapsed.
/// </summary>
public sealed partial class NotificationCenterPanel : UserControl
{
    private readonly INotificationCenter center;
    private bool expanded = true;
    private Guid? lastNotificationId;

    public NotificationCenterPanel(INotificationCenter center)
    {
        this.center = center;
        InitializeComponent();
        center.Changed += OnCenterChanged;
        Refresh();
    }

    public bool AutoExpandForWarningOrError { get; set; } = true;

    private void OnCenterChanged(object? sender, EventArgs e)
    {
        AppNotification? latest = center.Latest();
        if (latest is not null && latest.Id != lastNotificationId)
        {
            lastNotificationId = latest.Id;
            if (AutoExpandForWarningOrError &&
                latest.Severity is NotificationSeverity.Warning
                    or NotificationSeverity.Error)
            {
                SetExpanded(true);
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
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        // The whole center, including the bell, stays out of the global UI
        // until there is at least one notification.
        if (empty)
        {
            expanded = true;
        }
        Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        UnreadText.Text = center.UnreadCount > 0
            ? $"{center.UnreadCount} 条未读"
            : "无未读";
        MarkAllReadButton.IsEnabled = center.UnreadCount > 0;
        ClearButton.IsEnabled = !empty;
        BellBadge.Visibility = center.UnreadCount > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        BellBadgeText.Text = center.UnreadCount.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        ApplyExpandedState();
    }

    private void SetExpanded(bool value)
    {
        if (expanded == value)
        {
            return;
        }
        expanded = value;
        ApplyExpandedState();
    }

    private void ApplyExpandedState()
    {
        PanelCard.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        OpenButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenClicked(object sender, RoutedEventArgs e) => SetExpanded(true);

    private void OnCollapseClicked(object sender, RoutedEventArgs e) => SetExpanded(false);

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
        row.Action?.Invoke();
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
    Action? Action)
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
        notification.Severity switch
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
        notification.Action);
}

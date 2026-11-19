// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private void RenderCalendar(GachaReadOnlySnapshot snapshot)
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarMonthText.Text = "";
        CalendarSummaryText.Text = "";
        if (snapshot.IsBusy || snapshot.VisibleMonth is not DateOnly anchor) return;
        var heatmap = GachaCalendarHeatmap.FromRows(anchor.Year, snapshot.Rows);
        IReadOnlyList<GachaCalendarCell> days = heatmap.Days;
        CalendarMonthText.Text = $"{anchor.Year} 年抽卡分布 · {snapshot.Calendar.TimeZoneLabel}";
        CalendarSummaryText.Text = $"共 {heatmap.TotalPulls} 抽 · " +
            $"{heatmap.ActiveDays} 个活跃日 · 颜色按当前年份的每日抽数分级";
        int weeks = heatmap.Weeks;
        const double size = 14; // Fixed cells avoid recreating hundreds of controls during window resize.
        CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        for (int week = 0; week < weeks; week++)
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(size) });
        for (int row = 0; row < 7; row++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(size) });
        CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int weekday = 0; weekday < 7; weekday += 2)
        {
            var label = new TextBlock
            {
                Text = GachaCalendarCell.Weekdays[weekday], FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(label, weekday);
            CalendarGrid.Children.Add(label);
        }
        foreach (GachaCalendarCell day in days)
        {
            var button = new Button
            {
                Width = size, Height = size, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0),
                BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3),
                Background = CalendarDayBrush(day.IntensityLevel),
            };
            AutomationProperties.SetName(button, day.Label);
            AutomationProperties.SetAutomationId(button, $"GachaCalendarDay{day.Date:yyyyMMdd}");
            ToolTipService.SetToolTip(button, $"{day.Label}\n{day.HighRanks} · {snapshot.Calendar.TimeZoneLabel}");
            button.Click += async (_, _) => { if (state is not null) await state.OpenCalendarDayAsync(day.Date); };
            Grid.SetColumn(button, day.Week + 1);
            Grid.SetRow(button, day.Column);
            CalendarGrid.Children.Add(button);
            if (day.Date.Day != 1) continue;
            var monthLabel = new TextBlock { Text = $"{day.Date.Month}月", FontSize = 11, Margin = new Thickness(0, 5, 0, 0) };
            Grid.SetColumn(monthLabel, day.Week + 1);
            Grid.SetRow(monthLabel, 7);
            Grid.SetColumnSpan(monthLabel, Math.Min(4, weeks - day.Week));
            CalendarGrid.Children.Add(monthLabel);
        }
        Border[] legend = [CalendarLegend0, CalendarLegend1, CalendarLegend2, CalendarLegend3, CalendarLegend4, CalendarLegend5];
        for (int i = 0; i < legend.Length; i++) legend[i].Background = CalendarDayBrush(i);
    }

    private SolidColorBrush CalendarDayBrush(int intensity)
    {
        string hex = GachaCalendarHeatmap.ColorHex(intensity, ActualTheme != ElementTheme.Light);
        return new(Color.FromArgb(255, Convert.ToByte(hex.Substring(1, 2), 16),
            Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16)));
    }

    private void OnCalendarThemeChanged(FrameworkElement sender, object args)
    {
        if (state?.Snapshot.View == GachaReadOnlyView.Calendar) RenderCalendar(state.Snapshot);
    }
}

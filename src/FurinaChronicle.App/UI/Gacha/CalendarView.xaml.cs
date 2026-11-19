// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Presentation.Gacha;
using Microsoft.Maui.Controls.Shapes;

namespace FurinaChronicle.App.UI.Gacha;

public partial class CalendarView : ContentView
{
    private GachaAnalysisViewModel? model;
    private bool loaded;
    private bool changingYear;
    private int? year;
    private DateOnly? selectedDate;
    private Border? selectedCell;
    private Application? application;

    public CalendarView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (loaded) AttachModel();
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        loaded = true;
        application = Application.Current;
        if (application is not null) application.RequestedThemeChanged += OnThemeChanged;
        AttachModel();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        loaded = false;
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        model = null;
        if (application is not null) application.RequestedThemeChanged -= OnThemeChanged;
        application = null;
        ClearGrid();
    }

    private void AttachModel()
    {
        if (model is not null) model.PropertyChanged -= OnModelChanged;
        model = BindingContext as GachaAnalysisViewModel;
        if (model is not null) model.PropertyChanged += OnModelChanged;
        year = null;
        Render();
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GachaAnalysisViewModel.CalendarDays)) Render();
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e) => Render();

    private void OnPreviousYear(object? sender, EventArgs e)
    {
        if (year is > 1) { year--; Render(); }
    }

    private void OnNextYear(object? sender, EventArgs e)
    {
        if (year is < 9998) { year++; Render(); }
    }

    private void OnYearChanged(object? sender, EventArgs e)
    {
        if (changingYear || YearPicker.SelectedItem is not int value) return;
        year = value;
        Render();
    }

    private void ClearGrid()
    {
        HeatmapGrid.Children.Clear();
        HeatmapGrid.RowDefinitions.Clear();
        HeatmapGrid.ColumnDefinitions.Clear();
        selectedCell = null;
    }

    private void Render()
    {
        ClearGrid();
        if (!loaded || model is null) return;
        var days = model.CalendarDays;
        year ??= days.Count == 0 ? DateTime.Today.Year : days.Max(day => day.Date.Year);
        GachaCalendarHeatmap map = GachaCalendarHeatmap.FromDailyCounts(year.Value, days);
        bool dark = Application.Current?.RequestedTheme == AppTheme.Dark;
        Color DayColor(int level) => Color.FromArgb(GachaCalendarHeatmap.ColorHex(level, dark));
        changingYear = true;
        YearPicker.ItemsSource = days.Select(day => day.Date.Year).Append(year.Value)
            .Distinct().OrderDescending().ToArray();
        YearPicker.SelectedItem = year.Value;
        changingYear = false;
        PreviousYear.IsEnabled = !model.IsBusy && year > 1;
        NextYear.IsEnabled = !model.IsBusy && year < 9998;
        YearSummary.Text = $"{year} 年 · 共 {map.TotalPulls} 抽 · {map.ActiveDays} 个活跃日";
        SelectedDay.Text = "";
        HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(22)));
        for (int week = 0; week < map.Weeks; week++)
            HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(18)));
        for (int row = 0; row < 7; row++)
            HeatmapGrid.RowDefinitions.Add(new RowDefinition(new GridLength(18)));
        HeatmapGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int weekday = 0; weekday < 7; weekday += 2)
            HeatmapGrid.Add(new Label { Text = GachaCalendarCell.Weekdays[weekday], FontSize = 10 }, 0, weekday);
        foreach (GachaCalendarCell day in map.Days)
        {
            var cell = new Border
            {
                WidthRequest = 18, HeightRequest = 18, Padding = 0,
                BackgroundColor = DayColor(day.IntensityLevel),
                StrokeShape = new RoundRectangle { CornerRadius = 3 }, StrokeThickness = 0,
                AutomationId = $"GachaCalendarDay{day.Date:yyyyMMdd}",
            };
            SemanticProperties.SetDescription(cell, $"{day.Label}，{day.HighRanks}");
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                if (selectedCell is not null) selectedCell.StrokeThickness = 0;
                selectedCell = cell;
                selectedDate = day.Date;
                cell.Stroke = dark ? Colors.White : Colors.Black;
                cell.StrokeThickness = 2;
                SelectedDay.Text = $"{day.Label} · {day.HighRanks}";
            };
            cell.GestureRecognizers.Add(tap);
            HeatmapGrid.Add(cell, day.Week + 1, day.Column);
            if (selectedDate == day.Date)
            {
                selectedCell = cell;
                cell.Stroke = dark ? Colors.White : Colors.Black;
                cell.StrokeThickness = 2;
                SelectedDay.Text = $"{day.Label} · {day.HighRanks}";
            }
            if (day.Date.Day != 1) continue;
            var label = new Label { Text = $"{day.Date.Month}月", FontSize = 10, Margin = new Thickness(0, 4, 0, 0) };
            HeatmapGrid.Add(label, day.Week + 1, 7);
            Grid.SetColumnSpan(label, Math.Min(4, map.Weeks - day.Week));
        }
        Legend.Children.Clear();
        Legend.Children.Add(new Label { Text = "少", FontSize = 11 });
        for (int i = 0; i <= 5; i++)
            Legend.Children.Add(new Border
            {
                WidthRequest = 12, HeightRequest = 12, BackgroundColor = DayColor(i), StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 2 }, VerticalOptions = LayoutOptions.Center,
            });
        Legend.Children.Add(new Label { Text = "多", FontSize = 11 });
    }
}

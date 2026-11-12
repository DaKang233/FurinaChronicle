// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView : UserControl
{
    private Ui1PreviewEnvironment? environment;
    private ServiceGachaReadOnlySource? source;
    private IUiDispatcher? dispatcher;
    private IFilePickerService? filePicker;
    private GachaReadOnlyState? state;
    private CancellationTokenSource? detailLoad;
    private bool rendering;

    public Ui1GachaView()
    {
        InitializeComponent();
        ViewPicker.ItemsSource = GachaReadOnlyViewOption.Options;
    }

    // Called once by the shell after the shared composition root resolves the isolated
    // environment and all stateless gacha coordinators.
    public void Initialize(GachaPageServices services)
    {
        environment = services.Environment;
        source = services.Source;
        dispatcher = services.Dispatcher;
        filePicker = services.FilePicker;
        importCoordinator = services.Import;
        portableImporter = services.PortableImport;
        portableExporter = services.PortableExport;
        recordOperations = services.RecordOperations;
        manualRefresher = services.ManualRefresh;
        windowsCacheRefresh = services.WindowsCacheRefresh;
        teyvatImport = services.TeyvatImport;
        importMode ??= new GachaImportModeState();
        RenderImportState();
    }

    public async Task EnterAsync()
    {
        try
        {
            if (state is null)
            {
                if (environment is null || source is null || dispatcher is null)
                {
                    StatusText.Text = "共享隔离环境尚未初始化；请重新启动应用。";
                    return;
                }

                state = new(source, dispatcher);
                state.PropertyChanged += (_, _) => Render();
                EnsureImportComposition(environment);
                EnsureDataOperationComposition(environment);
                PathText.Text = environment.DatabasePath;
            }
            await state.EnterAsync();
            entered = true;
        }
        catch (Exception)
        {
            entered = false;
            StatusText.Text = "无法打开测试数据，请检查目录访问权限并重试。";
        }
    }

    public void Leave()
    {
        entered = false;
        state?.Leave();
    }

    public async Task CloseAsync()
    {
        Leave();
        exportCancellation?.Cancel();
        if (exportTask is Task runningExport) await runningExport;
        // A commit already started must not be interrupted by page/window navigation; the
        // atomic use case finishes and the window waits for it before disposing the database.
        importCancellation?.Cancel();
        if (importFlowTask is Task runningImport)
        {
            try { await runningImport; } catch (Exception) { }
        }
        if (dataOperationTask is Task runningOperation)
        {
            try { await runningOperation; } catch (Exception) { }
        }
    }

    private void Render()
    {
        if (state is null) return;
        rendering = true;
        try
        {
            GachaReadOnlySnapshot s = state.Snapshot;
            ArchivePicker.ItemsSource = s.Archives;
            AccountPicker.ItemsSource = s.Accounts;
            ArchivePicker.SelectedItem = s.Archives.FirstOrDefault(a => a.Id == s.ArchiveId);
            AccountPicker.SelectedItem = s.Accounts.FirstOrDefault(a => a.Id == s.AccountId);
            ViewPicker.SelectedItem = GachaReadOnlyViewOption.Options.First(option => option.View == s.View);
            RangeButton.IsEnabled = s.Accounts.Count > 0 && !s.IsBusy;
            UpdateExportAvailability();
            RangeButton.Content = s.ArchiveAccountIds is { } ids ? $"档案范围（{ids.Count}）" : "档案多选";
            FilterButton.Visibility = s.View is GachaReadOnlyView.Records or GachaReadOnlyView.Calendar ? Visibility.Visible : Visibility.Collapsed;
            FilterButton.Content = s.View == GachaReadOnlyView.Calendar ? "月份与时区" :
                s.Filter.Day is DateOnly day ? $"筛选 · {day:MM-dd}" : s.Filter == new GachaReadOnlyFilter() ? "筛选" : "筛选 · 已应用";
            detailLoad?.Cancel();
            selectedRow = null;
            DetailTitle.Text = "请选择条目";
            DetailBody.Text = "";
            bool calendar = s.View == GachaReadOnlyView.Calendar;
            bool items = s.View == GachaReadOnlyView.Items;
            RecordsList.ItemsSource = calendar || items ? null : s.Rows;
            RecordsList.Visibility = calendar || items ? Visibility.Collapsed : Visibility.Visible;
            ItemGrid.Visibility = items ? Visibility.Visible : Visibility.Collapsed;
            ItemGrid.ItemsSource = items ? s.Rows : null;
            CalendarScroll.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
            if (calendar) RenderCalendar(s);
            UpdateLayoutMode();
            SummaryText.Text = s.Summary;
            StatusText.Text = calendar ? $"{s.VisibleMonth:yyyy-MM} {s.Calendar.TimeZoneLabel} · {s.Status}" : s.Status;
            CancelButton.IsEnabled = s.IsBusy;
            PreviousButton.Content = calendar ? "上个月" : "上一页";
            NextButton.Content = calendar ? "下个月" : "下一页";
            PreviousButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanPrevious;
            NextButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanNext;
            RenderImportState();
            UpdateCommandAvailability();
        }
        finally { rendering = false; }
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        if (state is null) return;
        bool calendar = state.Snapshot.View == GachaReadOnlyView.Calendar;
        bool side = !calendar && ViewBody.ActualWidth >= 990;
        DetailColumn.Width = new GridLength(side ? 320 : 0);
        DetailPane.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        bool table = state.Snapshot.View == GachaReadOnlyView.Records && ViewBody.ActualWidth - (side ? 332 : 0) >= 640;
        TableHeader.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        var template = (DataTemplate)Resources[table ? "RecordTableTemplate" : "RecordListTemplate"];
        if (RecordsList.ItemTemplate != template) RecordsList.ItemTemplate = template;
    }

    private void RenderCalendar(GachaReadOnlySnapshot snapshot)
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();
        if (snapshot.IsBusy || snapshot.VisibleMonth is not DateOnly month) return;
        GachaCalendarCell[] days = GachaCalendarCell.Build(month, snapshot.Rows).ToArray();
        for (int row = 0; row <= days[^1].Week + 1; row++)
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int column = 0; column < 7; column++)
        {
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var heading = new TextBlock { Text = GachaCalendarCell.Weekdays[column], HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(heading, column);
            CalendarGrid.Children.Add(heading);
        }
        foreach (GachaCalendarCell cell in days)
        {
            var button = new Button
            {
                MinWidth = 0, MinHeight = 50, Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock { Text = $"{cell.Date.Day}\n{cell.Pulls} 抽", TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap }
            };
            AutomationProperties.SetName(button, cell.Label);
            button.Click += async (_, _) => { if (state is not null) await state.OpenCalendarDayAsync(cell.Date); };
            Grid.SetColumn(button, cell.Column);
            Grid.SetRow(button, cell.Week + 1);
            CalendarGrid.Children.Add(button);
        }
    }
    private async void OnViewChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!rendering && state is not null && ViewPicker.SelectedItem is GachaReadOnlyViewOption option)
            await state.SelectViewAsync(option.View);
    }

    private async void OnRange(object sender, RoutedEventArgs e)
    {
        if (state is null) return;
        GachaReadOnlySnapshot snapshot = state.Snapshot;
        var panel = new StackPanel { Spacing = 4 };
        var options = snapshot.Accounts.Select(account => new CheckBox
        {
            Content = new TextBlock { Text = account.Label, TextWrapping = TextWrapping.Wrap },
            IsChecked = snapshot.ArchiveAccountIds?.Contains(account.Id) ?? account.Id == snapshot.AccountId
        }).ToArray();
        var selectAll = new Button { Content = "全选" };
        selectAll.Click += (_, _) => { foreach (CheckBox option in options) option.IsChecked = true; };
        panel.Children.Add(selectAll);
        foreach (CheckBox option in options) panel.Children.Add(option);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "选择档案范围", PrimaryButtonText = "应用档案范围",
            SecondaryButtonText = "恢复单账号", CloseButtonText = "取消",
            Content = new ScrollViewer { Content = panel, MaxHeight = 320 }
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (state.Snapshot.ArchiveId != snapshot.ArchiveId) return;
        if (result == ContentDialogResult.Primary)
            await state.SelectArchiveAccountsAsync(snapshot.Accounts.Where((_, index) => options[index].IsChecked == true).Select(a => a.Id).ToArray());
        if (result == ContentDialogResult.Secondary && snapshot.AccountId is Guid accountId)
            await state.SelectAccountAsync(accountId);
    }

    private async void OnFilter(object sender, RoutedEventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar)
        {
            await ChooseCalendarAsync(state);
            return;
        }
        var filter = state.Snapshot.Filter;
        var rank = new ComboBox { Header = "星级", ItemsSource = GachaReadOnlyFilter.Ranks, SelectedIndex = filter.RankIndex };
        var pool = new ComboBox { Header = "卡池", ItemsSource = GachaReadOnlyFilter.Pools, SelectedIndex = filter.PoolIndex };
        var sort = new ComboBox { Header = "排序", ItemsSource = GachaReadOnlyFilter.SortOrders, SelectedIndex = (int)filter.SortOrder };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "明细筛选", PrimaryButtonText = "应用", SecondaryButtonText = "重置", CloseButtonText = "取消",
            Content = new StackPanel { Spacing = 8, Children = { rank, pool, sort } }
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
            await state.ApplyFilterAsync(GachaReadOnlyFilter.FromIndices(rank.SelectedIndex, pool.SelectedIndex, sort.SelectedIndex)
                with { Day = filter.Day, DayOffset = filter.DayOffset });
        if (result == ContentDialogResult.Secondary) await state.ApplyFilterAsync(new());
    }

    private async Task ChooseCalendarAsync(GachaReadOnlyState current)
    {
        GachaCalendarFilter calendar = current.Snapshot.Calendar;
        var month = new CalendarDatePicker { Header = "月份（选择该月任意日期）" };
        if (current.Snapshot.VisibleMonth is DateOnly date) month.Date = date.ToDateTime(TimeOnly.MinValue);
        var zone = new ComboBox { Header = "显示时区", ItemsSource = new[] { "UTC+08:00", "UTC" }, SelectedIndex = calendar.UseChinaTime ? 0 : 1 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "日历范围", PrimaryButtonText = "应用", SecondaryButtonText = "最近有记录月份", CloseButtonText = "取消",
            Content = new StackPanel { Spacing = 8, Children = { month, zone } }
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result is ContentDialogResult.Primary or ContentDialogResult.Secondary)
            await current.ApplyCalendarAsync(new(result == ContentDialogResult.Primary && month.Date is DateTimeOffset selected
                ? DateOnly.FromDateTime(selected.Date) : null, zone.SelectedIndex == 0));
    }

    private async void OnOpenDetail(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GachaReadOnlyRow row || source is null) return;
        selectedRow = row;
        UpdateCommandAvailability();
        detailLoad?.Cancel();
        using var load = new CancellationTokenSource();
        detailLoad = load;
        bool side = DetailPane.Visibility == Visibility.Visible;
        var text = side ? DetailBody : new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        text.Text = "正在读取详情…";
        if (side) DetailTitle.Text = row.Name;
        else
        {
            ContentDialog? dialog = null;
            var panel = new StackPanel { Spacing = 12 };
            panel.Children.Add(text);
            if (row.Reference is not null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = "记录操作会先关闭详情，再打开独立的确认框。",
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = row.Name,
                CloseButtonText = "关闭",
                Content = new ScrollViewer { MaxHeight = 520, Content = panel }
            };
            if (row.Reference is not null)
            {
                panel.Children.Add(CreateRecordOperationButtons(row, dialog));
            }

            dialog.Closed += (_, _) => load.Cancel();
            await Task.WhenAll(dialog.ShowAsync().AsTask(), LoadDetailAsync());
        }
        if (side) await LoadDetailAsync();
        if (detailLoad == load) detailLoad = null;

        async Task LoadDetailAsync()
        {
            try
            {
                string detail = await source.GetDetailAsync(row, load.Token);
                if (!load.IsCancellationRequested) text.Text = detail;
            }
            catch (OperationCanceledException) when (load.IsCancellationRequested) { }
            catch (Exception) { if (!load.IsCancellationRequested) text.Text = row.Detail + "\n详情读取失败。"; }
        }
    }

    private async void OnArchiveChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!rendering && state is not null && ArchivePicker.SelectedItem is GachaScopeOption item)
            await state.SelectArchiveAsync(item.Id);
    }
    private async void OnAccountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!rendering && state is not null && AccountPicker.SelectedItem is GachaScopeOption item)
            await state.SelectAccountAsync(item.Id);
    }
    private async void OnReload(object sender, RoutedEventArgs e) => await EnterAsync();
    private void OnCancel(object sender, RoutedEventArgs e) => state?.Cancel();
    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar) await state.ShiftCalendarAsync(-1);
        else await state.ChangePageAsync(state.Snapshot.PageNumber - 1);
    }
    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar) await state.ShiftCalendarAsync(1);
        else await state.ChangePageAsync(state.Snapshot.PageNumber + 1);
    }
}

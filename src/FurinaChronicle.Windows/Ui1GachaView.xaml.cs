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
    private WindowsHostEnvironment? environment;
    private ServiceGachaReadOnlySource? source;
    private IUiDispatcher? dispatcher;
    private IFilePickerService? filePicker;
    private GachaReadOnlyState? state;
    private GachaSelectionPreferences? selectionPreferences;
    private CancellationTokenSource? detailLoad;
    private bool rendering;

    public Ui1GachaView()
    {
        InitializeComponent();
        ViewPicker.ItemsSource = GachaReadOnlyViewOption.Options;
        PageSizePicker.ItemsSource = GachaReadOnlyState.PageSizeOptions;
        ActualThemeChanged += OnCalendarThemeChanged;
        AddHandler(PointerWheelChangedEvent,
            new Microsoft.UI.Xaml.Input.PointerEventHandler(OnOverviewWheel), true);
    }

    // Called once by the shell after the shared composition root resolves the isolated
    // environment and all stateless gacha coordinators.
    public void Initialize(GachaPageServices services)
    {
        environment = services.Environment;
        source = services.Source;
        selectionPreferences = services.SelectionPreferences;
        Artwork.Configure(services.Environment, services.ItemMetadata);
        exportMetadata = services.ExportMetadata;
        bannerPreloader = services.BannerPreloader;
        dispatcher = services.Dispatcher;
        filePicker = services.FilePicker;
        importCoordinator = services.Import;
        portableImporter = services.PortableImport;
        portableExporter = services.PortableExport;
        recordOperations = services.RecordOperations;
        manualRefresher = services.ManualRefresh;
        windowsCacheRefresh = services.WindowsCacheRefresh;
        teyvatImport = services.TeyvatImport;
        stokenRefresh = services.STokenRefresh;
        unselectedRefresh = services.UnselectedRefresh;
        importMode ??= new GachaImportModeState();
        notifications = services.Notifications;
        diagnostics = services.Diagnostics;
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
                    ReportFailure(
                        "gacha.page",
                        "gacha-page-enter",
                        "抽卡页不可用",
                        "共享隔离环境尚未初始化；请重新启动应用。");
                    return;
                }

                state = new(source, dispatcher, GachaReadOnlyView.Overview, new(null, true, YearView: true), selectionPreferences);
                state.PropertyChanged += (_, _) => Render();
                EnsureImportComposition(environment);
                EnsureDataOperationComposition(environment);
            }
            entered = true;
            await state.EnterAsync();
            ClearInlineValidation();
        }
        catch (Exception exception)
        {
            entered = false;
            ReportFailure(
                "gacha.page",
                "gacha-page-enter",
                "无法打开抽卡数据",
                "无法打开抽卡数据，请检查目录访问权限并重试。",
                exception);
        }
    }

    public void Leave()
    {
        entered = false;
        state?.Leave();
        bannerCacheCancellation?.Cancel();
        Artwork.CancelPending();
    }

    public async Task CloseAsync()
    {
        CloseRefreshForShutdown();
        Leave();
        if (bannerCacheTask is Task cachingBanners)
        {
            try { await cachingBanners; } catch (Exception) { }
        }
        if (state is not null) await state.FlushSelectionAsync();
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
            UpdateArtworkScope(s.Rows);
            UpdateScopePicker(ArchivePicker, s.Archives, s.ArchiveId);
            UpdateScopePicker(AccountPicker, s.Accounts, s.AccountId);
            ViewPicker.SelectedItem = GachaReadOnlyViewOption.Options.First(option => option.View == s.View);
            RangeButton.IsEnabled = s.Accounts.Count > 0 && !s.IsBusy;
            UpdateExportAvailability();
            RangeButton.Content = s.ArchiveAccountIds is { } ids ? $"档案范围（{ids.Count}）" : "档案多选";
            FilterButton.Visibility = s.View is GachaReadOnlyView.Records or GachaReadOnlyView.Calendar ? Visibility.Visible : Visibility.Collapsed;
            FilterButton.Content = s.View == GachaReadOnlyView.Calendar ? "年份与时区" :
                s.Filter.Day is DateOnly day ? $"筛选 · {day:MM-dd}" : s.Filter == new GachaReadOnlyFilter() ? "筛选" : "筛选 · 已应用";
            detailLoad?.Cancel();
            selectedRow = null;
            DetailTitle.Text = "请选择条目";
            DetailBody.Text = "";
            DetailBody.Visibility = Visibility.Visible;
            bool calendar = s.View == GachaReadOnlyView.Calendar;
            bool items = s.View == GachaReadOnlyView.Items;
            bool overview = s.View == GachaReadOnlyView.Overview;
            bool history = s.View == GachaReadOnlyView.History;
            RenderHistory(s);
            OverviewScroll.Visibility = overview ? Visibility.Visible : Visibility.Collapsed;
            OverviewCards.ItemsSource = overview ? s.Rows.Select(row => row.Overview).OfType<GachaOverviewCard>().ToArray() : null;
            if (overview) SizeOverviewCards();
            RecordsList.ItemsSource = calendar || items || overview || history ? null : s.Rows;
            RecordsList.Visibility = calendar || items || overview || history ? Visibility.Collapsed : Visibility.Visible;
            RenderItems(s);
            if (items) DetailTitle.Text = "点击物品查看获取信息";
            CalendarScroll.Visibility = calendar ? Visibility.Visible : Visibility.Collapsed;
            if (calendar) RenderCalendar(s);
            else CalendarGrid.Children.Clear();
            UpdateLayoutMode();
            string summary = !s.IsBusy && history ? $"卡池历史 {s.TotalCount} 个"
                : !s.IsBusy && overview ? $"{s.TotalCount} 个卡池分组" : s.Summary;
            SummaryText.Text = summary + (state.SelectionWarning is string warning ? " · " + warning : "");
            PageSizePicker.SelectedItem = s.PageSize;
            PageSizePicker.Visibility = s.View == GachaReadOnlyView.Records ? Visibility.Visible : Visibility.Collapsed;
            PageSizePicker.IsEnabled = !s.IsBusy;
            ReportReadFailureOnce(s.Status);
            CancelButton.IsEnabled = s.IsBusy;
            bool paged = s.View is GachaReadOnlyView.Records or GachaReadOnlyView.Items or GachaReadOnlyView.Calendar;
            PreviousButton.Visibility = NextButton.Visibility = paged ? Visibility.Visible : Visibility.Collapsed;
            PreviousButton.Content = calendar ? "上一年" : "上一页";
            NextButton.Content = calendar ? "下一年" : "下一页";
            PreviousButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanPrevious;
            NextButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanNext;
            RenderImportState();
            UpdateCommandAvailability();
        }
        finally { rendering = false; }
    }

    private static void UpdateScopePicker(
        ComboBox picker, IReadOnlyList<GachaScopeOption> options, Guid? selectedId)
    {
        // Preserve native popup/focus objects when a reload returns the same choices.
        // A real rename, insertion or deletion still replaces the options.
        if (picker.ItemsSource is not IReadOnlyList<GachaScopeOption> current ||
            !current.SequenceEqual(options))
            picker.ItemsSource = options;
        picker.SelectedItem = picker.Items.OfType<GachaScopeOption>()
            .FirstOrDefault(option => option.Id == selectedId);
    }

    private void OnBodySizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayoutMode();

    private void UpdateLayoutMode()
    {
        if (state is null) return;
        bool items = state.Snapshot.View == GachaReadOnlyView.Items;
        bool side = items || (state.Snapshot.View == GachaReadOnlyView.Records && ViewBody.ActualWidth >= 990);
        double detailWidth = items ? Math.Clamp(ViewBody.ActualWidth * 0.38, 260, 320) : 320;
        DetailColumn.Width = new GridLength(side ? detailWidth : 0);
        DetailPane.Visibility = side ? Visibility.Visible : Visibility.Collapsed;
        bool table = state.Snapshot.View == GachaReadOnlyView.Records && ViewBody.ActualWidth - (side ? 332 : 0) >= 640;
        TableHeader.Visibility = table ? Visibility.Visible : Visibility.Collapsed;
        string templateName = table ? "RecordTableTemplate" : "RecordListTemplate";
        var template = (DataTemplate)Resources[templateName];
        if (RecordsList.ItemTemplate != template) RecordsList.ItemTemplate = template;
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
        var startDate = new CalendarDatePicker { Header = "开始日期" };
        var endDate = new CalendarDatePicker { Header = "结束日期" };
        if ((filter.Day ?? filter.StartDay) is DateOnly from)
            startDate.Date = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue));
        if ((filter.Day ?? filter.EndDay) is DateOnly to)
            endDate.Date = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue));
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        TimeSpan? offset = filter.Day.HasValue ? filter.DayOffset : filter.RangeOffset;
        string zone = offset is TimeSpan known
            ? $"日期按 UTC{(known < TimeSpan.Zero ? "-" : "+")}{known.Duration():hh\\:mm} 筛选。"
            : "日期按本机时区筛选。";
        AutomationProperties.SetAutomationId(startDate, "GachaFilterStart");
        AutomationProperties.SetAutomationId(endDate, "GachaFilterEnd");
        AutomationProperties.SetAutomationId(rank, "GachaFilterRank");
        AutomationProperties.SetAutomationId(pool, "GachaFilterPool");
        AutomationProperties.SetAutomationId(sort, "GachaFilterSort");
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "明细筛选", PrimaryButtonText = "应用", SecondaryButtonText = "重置", CloseButtonText = "取消",
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new StackPanel
                {
                    Spacing = 8,
                    Children = { rank, pool, sort, new TextBlock { Text = zone }, startDate, endDate, error }
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "GachaFilterDialog");
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (startDate.Date?.Date > endDate.Date?.Date)
            {
                error.Text = "结束日期不能早于开始日期。";
                args.Cancel = true;
            }
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await state.ApplyFilterAsync(GachaReadOnlyFilter.FromIndices(rank.SelectedIndex, pool.SelectedIndex, sort.SelectedIndex)
                with
                {
                    StartDay = startDate.Date is { } start ? DateOnly.FromDateTime(start.Date) : null,
                    EndDay = endDate.Date is { } end ? DateOnly.FromDateTime(end.Date) : null,
                    RangeOffset = offset
                });
        }
        if (result == ContentDialogResult.Secondary) await state.ApplyFilterAsync(new());
    }

    private async void OnPageSizeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!rendering && state is not null && PageSizePicker.SelectedItem is int size)
            await state.ChangePageSizeAsync(size);
    }

    private async Task ChooseCalendarAsync(GachaReadOnlyState current)
    {
        GachaCalendarFilter calendar = current.Snapshot.Calendar;
        var year = new NumberBox
        {
            Header = "年份", Minimum = 1, Maximum = 9998, SmallChange = 1, LargeChange = 1,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            Value = current.Snapshot.VisibleMonth?.Year ?? DateTime.Today.Year,
        };
        var zone = new ComboBox { Header = "显示时区", ItemsSource = new[] { "UTC+08:00", "UTC" }, SelectedIndex = calendar.UseChinaTime ? 0 : 1 };
        AutomationProperties.SetAutomationId(year, "GachaCalendarYear");
        AutomationProperties.SetAutomationId(zone, "GachaCalendarZone");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "日历范围", PrimaryButtonText = "应用", SecondaryButtonText = "最近有记录年份", CloseButtonText = "取消",
            Content = new StackPanel { Spacing = 8, Children = { year, zone, error } }
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!double.IsFinite(year.Value) || year.Value < 1 || year.Value > 9998 || year.Value != Math.Truncate(year.Value))
            {
                error.Text = "请输入 1～9998 之间的整数年份。";
                args.Cancel = true;
            }
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result is ContentDialogResult.Primary or ContentDialogResult.Secondary)
            await current.ApplyCalendarAsync(new(result == ContentDialogResult.Primary
                ? new DateOnly((int)year.Value, 1, 1) : null, zone.SelectedIndex == 0, YearView: true));
    }

    private async void OnOpenDetail(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not GachaReadOnlyRow row || source is null) return;
        if (row.Item is not null) { OpenItemDetails(row); return; }
        ItemDetails.Visibility = Visibility.Collapsed;
        DetailBody.Visibility = Visibility.Visible;
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

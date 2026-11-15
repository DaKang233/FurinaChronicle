// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Persistence;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.App.UI.Pages;

public partial class Ui1GachaPage : ContentPage
{
    private Ui1PreviewEnvironment? environment;
    private ServiceGachaReadOnlySource? source;
    private GachaReadOnlyState? state;
    private readonly DataTemplate recordTemplate;
    private readonly GridItemsLayout itemLayout = new(2, ItemsLayoutOrientation.Vertical);
    private readonly LinearItemsLayout recordLayout = new(ItemsLayoutOrientation.Vertical);
    private bool rendering;

    public Ui1GachaPage()
    {
        InitializeComponent();
        recordTemplate = RecordsList.ItemTemplate;
        SizeChanged += (_, _) => UpdateLayoutMode();
        ViewPicker.ItemsSource = GachaReadOnlyViewOption.Options.ToList();
#if ANDROID
        Title = "抽卡分析 · 隔离预览";
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            if (state is null)
            {
                environment = new(PlatformStoragePaths.AppDataDirectory, "Maui");
                source = new ServiceGachaReadOnlySource(new GetPlayerArchives(environment.Archives),
                    new GetGameAccounts(environment.Archives, environment.Accounts), new GetGachaRecordPage(environment.Records),
                    environment.InitializeAsync, environment.GetCachedIconPath,
                    new BuildGachaAnalytics(environment.Records, new EmptyGachaItemMetadataProvider(), new EmbeddedGachaEventCatalog()),
                    environment.Changes.GetRecordRevisionsAsync);
                state = new(source, new MauiUiDispatcher());
                state.PropertyChanged += (_, _) => Render();
            }
            await state.EnterAsync();
        }
        catch (Exception)
        {
            StatusLabel.Text = "隔离环境无法打开；请关闭其他预览实例后重试。";
        }
    }

    protected override void OnDisappearing()
    {
        state?.Leave();
        base.OnDisappearing();
    }

    private void Render()
    {
        if (state is null) return;
        rendering = true;
        try
        {
            GachaReadOnlySnapshot s = state.Snapshot;
            string? archive = s.Archives.FirstOrDefault(a => a.Id == s.ArchiveId)?.Label;
            string? account = s.Accounts.FirstOrDefault(a => a.Id == s.AccountId)?.Label;
            RangeButton.Text = s.ArchiveAccountIds is { } ids ? $"{archive} · {ids.Count} 个账号" : account ?? archive ?? "数据范围";
            SemanticProperties.SetDescription(RangeButton, $"数据范围：{archive}，{RangeButton.Text}");
            RangeButton.IsEnabled = s.Archives.Count > 0 && !s.IsBusy;
            ViewPicker.SelectedItem = GachaReadOnlyViewOption.Options.First(option => option.View == s.View);
            FilterButton.IsVisible = s.View is GachaReadOnlyView.Records or GachaReadOnlyView.Calendar;
            FilterButton.Text = s.View == GachaReadOnlyView.Calendar ? "月份" : s.Filter.Day is DateOnly day ? $"{day:MM-dd}" : "筛选";
            bool calendar = s.View == GachaReadOnlyView.Calendar;
            SemanticProperties.SetDescription(FilterButton, calendar ? "月份和显示时区" :
                s.Filter == new GachaReadOnlyFilter() ? "明细筛选" : "明细筛选，已应用");
            RecordsList.IsVisible = !calendar;
            CalendarScroll.IsVisible = calendar;
            UpdateLayoutMode();
            RecordsList.ItemsSource = calendar ? null : s.Rows;
            if (calendar) RenderCalendar(s);
            SummaryLabel.Text = s.Summary;
            StatusLabel.Text = s.Status.StartsWith("本地读取完成", StringComparison.Ordinal) ? "" : s.Status;
            if (calendar)
                StatusLabel.Text = $"{s.VisibleMonth:yyyy-MM} {s.Calendar.TimeZoneLabel} {StatusLabel.Text}".Trim();
            StatusLabel.IsVisible = StatusLabel.Text.Length != 0;
            CancelButton.IsVisible = s.IsBusy;
            PreviousButton.IsVisible = NextButton.IsVisible = SummaryLabel.IsVisible = !s.IsBusy;
            PreviousButton.Text = calendar ? "上个月" : "上一页";
            NextButton.Text = calendar ? "下个月" : "下一页";
            PreviousButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanPrevious;
            NextButton.IsEnabled = calendar ? !s.IsBusy && s.VisibleMonth.HasValue : s.CanNext;
        }
        finally { rendering = false; }
    }

    private void UpdateLayoutMode()
    {
        if (Width > 0) itemLayout.Span = Math.Clamp((int)(Width / 164), 1, 6);
        bool items = state?.Snapshot.View == GachaReadOnlyView.Items;
        IItemsLayout layout = items ? itemLayout : recordLayout;
        if (RecordsList.ItemsLayout == layout) return;
        RecordsList.ItemTemplate = items ? (DataTemplate)Resources["ItemCardTemplate"] : recordTemplate;
        RecordsList.ItemsLayout = layout;
    }

    private void RenderCalendar(GachaReadOnlySnapshot snapshot)
    {
        CalendarGrid.Children.Clear();
        CalendarGrid.ColumnDefinitions.Clear();
        CalendarGrid.RowDefinitions.Clear();
        if (snapshot.IsBusy || snapshot.VisibleMonth is not DateOnly month) return;
        GachaCalendarCell[] days = GachaCalendarCell.Build(month, snapshot.Rows).ToArray();
        for (int row = 0; row <= days[^1].Week + 1; row++) CalendarGrid.RowDefinitions.Add(new(GridLength.Auto));
        for (int column = 0; column < 7; column++)
        {
            CalendarGrid.ColumnDefinitions.Add(new(GridLength.Star));
            CalendarGrid.Add(new Label { Text = GachaCalendarCell.Weekdays[column], HorizontalTextAlignment = TextAlignment.Center }, column);
        }
        foreach (GachaCalendarCell cell in days)
        {
            var button = new Button
            {
                Text = $"{cell.Date.Day}\n{cell.Pulls}", MinimumHeightRequest = 48, MinimumWidthRequest = 0,
                Padding = 0, FontSize = 13, LineBreakMode = LineBreakMode.WordWrap
            };
            if (cell.Pulls == 0)
            {
                button.SetAppThemeColor(Button.BackgroundColorProperty, Color.FromArgb("#E9E9EF"), Color.FromArgb("#303037"));
                button.SetAppThemeColor(Button.TextColorProperty, Colors.Black, Colors.White);
            }
            SemanticProperties.SetDescription(button, cell.Label);
            button.Clicked += async (_, _) => { if (state is not null) await state.OpenCalendarDayAsync(cell.Date); };
            CalendarGrid.Add(button, cell.Column, cell.Week + 1);
        }
    }

    private async void OnViewChanged(object? sender, EventArgs e)
    {
        if (!rendering && state is not null && ViewPicker.SelectedItem is GachaReadOnlyViewOption option)
            await state.SelectViewAsync(option.View);
    }

    private async void OnRange(object? sender, EventArgs e)
    {
        if (state is null || source is null) return;
        GachaReadOnlySnapshot snapshot = state.Snapshot;
        var archive = new Picker { Title = "档案", ItemsSource = snapshot.Archives.ToList(), ItemDisplayBinding = new Binding(nameof(GachaScopeOption.Label)) };
        archive.SelectedItem = snapshot.Archives.FirstOrDefault(a => a.Id == snapshot.ArchiveId);
        var mode = new Picker { Title = "范围模式", ItemsSource = new[] { "单账号", "档案多选" }, SelectedIndex = snapshot.ArchiveAccountIds is null ? 0 : 1 };
        var accounts = new Picker { Title = "账号", ItemDisplayBinding = new Binding(nameof(GachaScopeOption.Label)) };
        var selectedLabel = new Label();
        accounts.SelectedIndexChanged += (_, _) => selectedLabel.Text = (accounts.SelectedItem as GachaScopeOption)?.Label;
        var checkList = new VerticalStackLayout { Spacing = 4 };
        var boxes = new List<(Guid Id, CheckBox CheckBox)>();
        var selectAll = new Button { Text = "全选", MinimumHeightRequest = 48 };
        selectAll.Clicked += (_, _) => { foreach (var option in boxes) option.CheckBox.IsChecked = true; };
        var apply = new Button { Text = "应用范围", MinimumHeightRequest = 48 };
        var status = new Label();
        void ShowMode()
        {
            accounts.IsVisible = selectedLabel.IsVisible = mode.SelectedIndex == 0;
            selectAll.IsVisible = checkList.IsVisible = mode.SelectedIndex == 1;
        }
        void ShowAccounts(IReadOnlyList<GachaScopeOption> options, bool originalArchive)
        {
            accounts.ItemsSource = options.ToList();
            accounts.SelectedItem = options.FirstOrDefault(a => a.Id == snapshot.AccountId) ?? options.FirstOrDefault();
            boxes.Clear();
            checkList.Children.Clear();
            foreach (GachaScopeOption option in options)
            {
                var box = new CheckBox
                {
                    IsChecked = originalArchive && (snapshot.ArchiveAccountIds?.Contains(option.Id) ?? option.Id == snapshot.AccountId)
                };
                SemanticProperties.SetDescription(box, option.Label);
                var row = new Grid { ColumnDefinitions = { new ColumnDefinition(new GridLength(48)), new ColumnDefinition(GridLength.Star) } };
                row.Add(box);
                row.Add(new Label { Text = option.Label, VerticalOptions = LayoutOptions.Center }, 1);
                boxes.Add((option.Id, box));
                checkList.Children.Add(row);
            }
            status.Text = options.Count == 0 ? "此档案没有游戏账号" : "";
        }
        ShowAccounts(snapshot.Accounts, true);
        ShowMode();
        mode.SelectedIndexChanged += (_, _) => ShowMode();
        var panel = CreatePanel("数据范围", new VerticalStackLayout
        {
            Spacing = 6, Children = { archive, mode, accounts, selectedLabel, selectAll, checkList, status, apply }
        });
        long generation = 0;
        panel.Disappearing += (_, _) => generation++;
        archive.SelectedIndexChanged += async (_, _) =>
        {
            if (archive.SelectedItem is not GachaScopeOption option) return;
            long request = ++generation;
            apply.IsEnabled = false;
            status.Text = "正在读取账号…";
            try
            {
                var result = await source.GetAccountsAsync(option.Id, CancellationToken.None);
                if (request != generation) return;
                ShowAccounts(result, option.Id == snapshot.ArchiveId);
                apply.IsEnabled = true;
            }
            catch (Exception)
            {
                if (request == generation) status.Text = "无法载入账号，请重新选择档案。";
            }
        };
        apply.Clicked += async (_, _) =>
        {
            if (archive.SelectedItem is not GachaScopeOption option) return;
            apply.IsEnabled = false;
            Guid[]? ids = mode.SelectedIndex == 1 ? boxes.Where(box => box.CheckBox.IsChecked).Select(box => box.Id).ToArray() : null;
            Guid? accountId = (accounts.SelectedItem as GachaScopeOption)?.Id;
            await Navigation.PopModalAsync();
            await state.SelectScopeAsync(option.Id, accountId, ids);
        };
        await Navigation.PushModalAsync(panel);
    }

    private async void OnFilter(object? sender, EventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar)
        {
            await ChooseCalendarAsync(state);
            return;
        }
        var filter = state.Snapshot.Filter;
        var rank = new Picker { Title = "星级", ItemsSource = GachaReadOnlyFilter.Ranks.ToList(), SelectedIndex = filter.RankIndex };
        var pool = new Picker { Title = "卡池", ItemsSource = GachaReadOnlyFilter.Pools.ToList(), SelectedIndex = filter.PoolIndex };
        var sort = new Picker { Title = "排序", ItemsSource = GachaReadOnlyFilter.SortOrders.ToList(), SelectedIndex = (int)filter.SortOrder };
        var apply = new Button { Text = "应用筛选", MinimumHeightRequest = 48 };
        var reset = new Button { Text = "重置筛选", MinimumHeightRequest = 48 };
        apply.Clicked += async (_, _) =>
        {
            apply.IsEnabled = reset.IsEnabled = false;
            await Navigation.PopModalAsync();
            await state.ApplyFilterAsync(GachaReadOnlyFilter.FromIndices(rank.SelectedIndex, pool.SelectedIndex, sort.SelectedIndex)
                with { Day = filter.Day, DayOffset = filter.DayOffset });
        };
        reset.Clicked += async (_, _) =>
        {
            apply.IsEnabled = reset.IsEnabled = false;
            await Navigation.PopModalAsync();
            await state.ApplyFilterAsync(new());
        };
        await Navigation.PushModalAsync(CreatePanel("明细筛选", new VerticalStackLayout
        {
            Spacing = 6, Children = { rank, pool, sort, apply, reset }
        }));
    }

    private async Task ChooseCalendarAsync(GachaReadOnlyState current)
    {
        GachaCalendarFilter calendar = current.Snapshot.Calendar;
        var month = new DatePicker { Date = current.Snapshot.VisibleMonth?.ToDateTime(TimeOnly.MinValue), Format = "yyyy 年 MM 月", MinimumHeightRequest = 48 };
        var zone = new Picker { Title = "显示时区", ItemsSource = new[] { "UTC+08:00", "UTC" }, SelectedIndex = calendar.UseChinaTime ? 0 : 1 };
        var apply = new Button { Text = "应用月份", MinimumHeightRequest = 48 };
        var all = new Button { Text = "最近有记录月份", MinimumHeightRequest = 48 };
        async Task ApplyAsync(DateOnly? date)
        {
            apply.IsEnabled = all.IsEnabled = false;
            await Navigation.PopModalAsync();
            await current.ApplyCalendarAsync(new(date, zone.SelectedIndex == 0));
        }
        apply.Clicked += async (_, _) => await ApplyAsync(month.Date is DateTime selected ? DateOnly.FromDateTime(selected) : null);
        all.Clicked += async (_, _) => await ApplyAsync(null);
        await Navigation.PushModalAsync(CreatePanel("日历范围", new VerticalStackLayout
        {
            Spacing = 6, Children = { new Label { Text = "选择该月任意日期" }, month, zone, apply, all }
        }));
    }

    private async void OnOpenDetail(object? sender, SelectionChangedEventArgs e)
    {
        if (rendering || source is null || e.CurrentSelection.FirstOrDefault() is not GachaReadOnlyRow row) return;
        RecordsList.SelectedItem = null;
        using var load = new CancellationTokenSource();
        var label = new Label { Text = row.Detail + "\n\n正在读取详情…" };
        ContentPage panel = CreatePanel(row.Name, label);
        panel.Disappearing += CancelDetail;
        try
        {
            await Navigation.PushModalAsync(panel);
            string detail = await source.GetDetailAsync(row, load.Token);
            if (!load.IsCancellationRequested) label.Text = detail;
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested) { }
        catch (Exception) { if (!load.IsCancellationRequested) label.Text = row.Detail + "\n详情读取失败。"; }
        finally { panel.Disappearing -= CancelDetail; }

        void CancelDetail(object? _, EventArgs args) => load.Cancel();
    }

    private ContentPage CreatePanel(string title, View content)
    {
        var close = new Button { Text = "关闭", MinimumHeightRequest = 48 };
        close.Clicked += async (_, _) => { close.IsEnabled = false; await Navigation.PopModalAsync(); };
        return new ContentPage
        {
            SafeAreaEdges = SafeAreaEdges.All,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 16, Spacing = 8,
                    Children = { new Label { Text = title, FontSize = 20, FontAttributes = FontAttributes.Bold }, content, close }
                }
            }
        };
    }
    private async void OnReload(object? sender, EventArgs e)
    {
        if (state is not null) await state.EnterAsync();
        else OnAppearing();
    }
    private void OnCancel(object? sender, EventArgs e) => state?.Cancel();
    private async void OnPrevious(object? sender, EventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar) await state.ShiftCalendarAsync(-1);
        else await state.ChangePageAsync(state.Snapshot.PageNumber - 1);
    }
    private async void OnNext(object? sender, EventArgs e)
    {
        if (state is null) return;
        if (state.Snapshot.View == GachaReadOnlyView.Calendar) await state.ShiftCalendarAsync(1);
        else await state.ChangePageAsync(state.Snapshot.PageNumber + 1);
    }
    private async void OnNavigateAway(object? sender, EventArgs e)
    {
        var back = new Button { Text = "返回抽卡预览", MinimumHeightRequest = 48 };
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        await Navigation.PushAsync(new ContentPage
        {
            Title = "页面往返验证",
            Content = new VerticalStackLayout
            {
                Padding = 24, Spacing = 16,
                Children = { new Label { Text = "返回时按稳定 ID 重新查询；此页面不改变抽卡范围。" }, back }
            }
        });
    }

    private sealed class MauiUiDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) => MainThread.InvokeOnMainThreadAsync(action);
    }
}

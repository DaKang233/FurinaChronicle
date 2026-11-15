// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ChroniclePage : UserControl
{
    private readonly IDiagnosticService diagnostics;
    private readonly ChronicleWorkspaceState state;
    private bool rendering;
    private string? lastReportedFailure;

    public ChroniclePage(
        ChronicleWorkspaceState state,
        IDiagnosticService diagnostics)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.diagnostics = diagnostics ??
            throw new ArgumentNullException(nameof(diagnostics));
        InitializeComponent();
        TimelinePicker.ItemsSource = state.TimelineOptions;
        GranularityPicker.ItemsSource = state.GranularityOptions;
        TimeRangePicker.ItemsSource = state.TimeRangeOptions;
        DomainPicker.ItemsSource = state.DomainOptions;
        RankPicker.ItemsSource = state.RankOptions;
        AutomationProperties.SetName(this, "游戏历程");

        // Subscribe only after XAML has connected all controls and set their initial values.
        ArchivePicker.SelectionChanged += OnArchiveSelectionChanged;
        AccountPicker.SelectionChanged += OnAccountSelectionChanged;
        TimelinePicker.SelectionChanged += OnTimelineSelectionChanged;
        GranularityPicker.SelectionChanged += OnGranularitySelectionChanged;
        TimeRangePicker.SelectionChanged += OnTimeRangeSelectionChanged;
        DomainPicker.SelectionChanged += OnDomainSelectionChanged;
        RankPicker.SelectionChanged += OnRankSelectionChanged;
        StateChangesSwitch.Toggled += OnStateChangesToggled;
        UndatedSwitch.Toggled += OnUndatedToggled;
        EntryList.SelectionChanged += OnEntrySelectionChanged;
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        SetLoading(true);
        try
        {
            await state.EnterAsync(cancellationToken);
        }
        finally
        {
            SetLoading(false);
            Render();
        }
    }

    public void Leave() => state.Cancel();

    private void Render()
    {
        ChronicleWorkspaceSnapshot snapshot = state.Snapshot;
        rendering = true;
        try
        {
            ArchivePicker.ItemsSource = snapshot.Archives;
            ArchivePicker.SelectedIndex = IndexOf(
                snapshot.Archives,
                snapshot.SelectedArchiveId);
            IReadOnlyList<HistoryChoice> accountChoices =
                snapshot.AccountChoices;
            AccountPicker.ItemsSource = accountChoices;
            AccountPicker.SelectedIndex = snapshot.SelectedAccountId is Guid accountId
                ? IndexOf(accountChoices, accountId)
                : 0;
            TimelinePicker.SelectedIndex = IndexOfOption(
                state.TimelineOptions,
                snapshot.Timeline);
            GranularityPicker.SelectedIndex = IndexOfOption(
                state.GranularityOptions,
                snapshot.Granularity);
            TimeRangePicker.SelectedIndex = IndexOfOption(
                state.TimeRangeOptions,
                snapshot.TimeRange);
            DomainPicker.SelectedIndex = IndexOfOption(
                state.DomainOptions,
                snapshot.Domain);
            RankPicker.SelectedIndex = IndexOfOption(
                state.RankOptions,
                snapshot.Rank);
            StateChangesSwitch.IsOn = snapshot.IncludeStateChanges;
            UndatedSwitch.IsOn = snapshot.IncludeUndated;

            EntryList.ItemsSource = snapshot.Entries;
            EntryList.SelectedItem = snapshot.SelectedEntry;
            bool stale = !string.IsNullOrWhiteSpace(snapshot.StaleMessage);
            StalePanel.Visibility = stale ? Visibility.Visible : Visibility.Collapsed;
            StaleText.Text = stale ? ShortStale(snapshot.StaleMessage!) : string.Empty;
            EmptyText.Text = snapshot.EmptyMessage ?? string.Empty;
            EmptyText.Visibility = !stale &&
                string.IsNullOrWhiteSpace(snapshot.Error) &&
                !string.IsNullOrWhiteSpace(snapshot.EmptyMessage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            ReloadButton.IsEnabled = !snapshot.IsBusy;
            LoadMoreButton.IsEnabled = snapshot.HasMore && !snapshot.IsBusy;
            PageText.Text = $"{snapshot.Entries.Count} 条已载入";
            ReportFailureState(snapshot);
            RenderDetail();
        }
        finally
        {
            rendering = false;
        }
    }

    private void RenderDetail()
    {
        ChronicleEntryDisplay? entry = state.Snapshot.SelectedEntry;
        DetailTitleText.Text = entry?.Title ?? "选择一条历程查看详情";
        DetailBodyText.Text = entry?.DetailsText ?? string.Empty;
        AutomationProperties.SetName(
            DetailBodyText,
            entry is null ? "历程详情" : $"历程详情：{entry.Title}");
    }

    private void ReportFailureState(ChronicleWorkspaceSnapshot snapshot)
    {
        if (snapshot.IsBusy)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.StaleMessage))
        {
            ReportOnce(
                $"stale|{snapshot.StaleMessage}",
                "读取游戏历程",
                "游戏历程视图已失效",
                ShortStale(snapshot.StaleMessage),
                DiagnosticSeverity.Warning);
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            ReportOnce(
                $"error|{snapshot.Error}",
                "读取游戏历程",
                "游戏历程读取失败",
                "记录可能未完整显示，请稍后刷新。",
                DiagnosticSeverity.Error);
            return;
        }

        lastReportedFailure = null;
    }

    private void ReportOnce(
        string key,
        string operation,
        string title,
        string message,
        DiagnosticSeverity severity)
    {
        if (string.Equals(lastReportedFailure, key, StringComparison.Ordinal))
        {
            return;
        }
        lastReportedFailure = key;
        SafeReport(new DiagnosticEvent(
            NotificationCategory.Chronicle,
            operation,
            title,
            message,
            severity));
    }

    private void SafeReport(DiagnosticEvent diagnostic)
    {
        try
        {
            diagnostics.Report(diagnostic);
        }
        catch (Exception)
        {
            // A failing notification/log path must never break the page.
        }
    }

    private static string ShortStale(string message)
    {
        if (message.Contains("过期", StringComparison.Ordinal))
        {
            return "浏览快照已过期，请重新载入。";
        }
        if (message.Contains("纠正", StringComparison.Ordinal) ||
            message.Contains("删除", StringComparison.Ordinal) ||
            message.Contains("撤销", StringComparison.Ordinal))
        {
            return "时间线数据已变化，请重新载入。";
        }
        return "当前视图已失效，请重新载入。";
    }

    private async Task RunAsync(Func<Task> operation)
    {
        SetLoading(true);
        try
        {
            await operation();
        }
        finally
        {
            SetLoading(false);
            Render();
        }
    }

    private void SetLoading(bool loading)
    {
        LoadingRing.IsActive = loading;
        LoadingRing.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ReloadButton.IsEnabled = !loading;
        LoadMoreButton.IsEnabled = !loading && state.Snapshot.HasMore;
        StaleReloadButton.IsEnabled = !loading;
    }

    private async void OnArchiveSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        int index = ArchivePicker.SelectedIndex;
        if (index < 0 || index >= state.Snapshot.Archives.Count)
        {
            return;
        }
        await RunAsync(() => state.SelectArchiveAsync(
            state.Snapshot.Archives[index].Id));
    }

    private async void OnAccountSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        if (AccountPicker.SelectedItem is not HistoryChoice choice)
        {
            return;
        }
        Guid? accountId = choice.Id == Guid.Empty ? null : choice.Id;
        await RunAsync(() => state.SelectAccountAsync(accountId));
    }

    private async void OnTimelineSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering || TimelinePicker.SelectedIndex < 0)
        {
            return;
        }
        await RunAsync(() => state.SetTimelineAsync(
            state.TimelineOptions[TimelinePicker.SelectedIndex].Value));
    }

    private async void OnGranularitySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering || GranularityPicker.SelectedIndex < 0)
        {
            return;
        }
        await RunAsync(() => state.SetGranularityAsync(
            state.GranularityOptions[GranularityPicker.SelectedIndex].Value));
    }

    private async void OnTimeRangeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering || TimeRangePicker.SelectedIndex < 0)
        {
            return;
        }
        await RunAsync(() => state.SetTimeRangeAsync(
            state.TimeRangeOptions[TimeRangePicker.SelectedIndex].Value));
    }

    private async void OnDomainSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering || DomainPicker.SelectedIndex < 0)
        {
            return;
        }
        await RunAsync(() => state.SetDomainAsync(
            state.DomainOptions[DomainPicker.SelectedIndex].Value));
    }

    private async void OnRankSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering || RankPicker.SelectedIndex < 0)
        {
            return;
        }
        await RunAsync(() => state.SetRankAsync(
            state.RankOptions[RankPicker.SelectedIndex].Value));
    }

    private async void OnStateChangesToggled(
        object sender,
        RoutedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await RunAsync(() => state.SetIncludeStateChangesAsync(
            StateChangesSwitch.IsOn));
    }

    private async void OnUndatedToggled(object sender, RoutedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await RunAsync(() => state.SetIncludeUndatedAsync(
            UndatedSwitch.IsOn));
    }

    private void OnEntrySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        state.SelectEntry(
            (EntryList.SelectedItem as ChronicleEntryDisplay)?.EntryId);
        RenderDetail();
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => state.ReloadAsync());
    }

    private async void OnLoadMore(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => state.LoadMoreAsync());
    }

    private void OnGenerateReport(object sender, RoutedEventArgs e)
    {
        ReportBox.Text = state.BuildMarkdownReport();
    }

    private void OnCopyReport(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(ReportBox.Text ?? string.Empty);
            Clipboard.SetContent(package);
        }
        catch (Exception exception)
        {
            ReportBox.SelectAll();
            SafeReport(new DiagnosticEvent(
                NotificationCategory.Chronicle,
                "复制报告",
                "复制失败",
                "无法写入剪贴板，请手动选择报告文本。",
                DiagnosticSeverity.Warning,
                exception));
        }
    }

    private static int IndexOf(
        IReadOnlyList<HistoryChoice> choices,
        Guid? id)
    {
        if (id is not Guid wanted)
        {
            return -1;
        }
        for (int index = 0; index < choices.Count; index++)
        {
            if (choices[index].Id == wanted)
            {
                return index;
            }
        }
        return -1;
    }

    private static int IndexOfOption<T>(
        IReadOnlyList<HistoryOption<T>> options,
        T value)
    {
        for (int index = 0; index < options.Count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(options[index].Value, value))
            {
                return index;
            }
        }
        return -1;
    }
}

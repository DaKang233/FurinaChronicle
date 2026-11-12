// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Presentation.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ChroniclePage : UserControl
{
    private readonly ChronicleWorkspaceState state;
    private bool rendering;

    public ChroniclePage(ChronicleWorkspaceState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        InitializeComponent();
        TimelinePicker.ItemsSource = state.TimelineOptions;
        GranularityPicker.ItemsSource = state.GranularityOptions;
        TimeRangePicker.ItemsSource = state.TimeRangeOptions;
        DomainPicker.ItemsSource = state.DomainOptions;
        RankPicker.ItemsSource = state.RankOptions;
        AutomationProperties.SetName(this, "游戏历程");
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
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
            StatusText.Text = snapshot.Status;
            StaleText.Text = snapshot.StaleMessage ?? string.Empty;
            StaleText.Visibility = string.IsNullOrWhiteSpace(snapshot.StaleMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            EmptyText.Text = snapshot.EmptyMessage ?? string.Empty;
            EmptyText.Visibility = string.IsNullOrWhiteSpace(snapshot.EmptyMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ReloadButton.IsEnabled = !snapshot.IsBusy;
            LoadMoreButton.IsEnabled = snapshot.HasMore && !snapshot.IsBusy;
            PageText.Text = $"{snapshot.Entries.Count} 条已载入";
            AutomationProperties.SetName(
                StatusText,
                snapshot.IsBusy ? "游戏历程正在载入" : "游戏历程状态");
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
        await state.SelectArchiveAsync(state.Snapshot.Archives[index].Id);
        Render();
    }

    private async void OnAccountSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        int index = AccountPicker.SelectedIndex;
        if (index < 0)
        {
            return;
        }
        Guid? accountId = index == 0
            ? null
            : state.Snapshot.AccountChoices[index].Id;
        await state.SelectAccountAsync(accountId);
        Render();
    }

    private async void OnTimelineSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetTimelineAsync(
            state.TimelineOptions[TimelinePicker.SelectedIndex].Value);
        Render();
    }

    private async void OnGranularitySelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetGranularityAsync(
            state.GranularityOptions[GranularityPicker.SelectedIndex].Value);
        Render();
    }

    private async void OnTimeRangeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetTimeRangeAsync(
            state.TimeRangeOptions[TimeRangePicker.SelectedIndex].Value);
        Render();
    }

    private async void OnDomainSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetDomainAsync(
            state.DomainOptions[DomainPicker.SelectedIndex].Value);
        Render();
    }

    private async void OnRankSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetRankAsync(
            state.RankOptions[RankPicker.SelectedIndex].Value);
        Render();
    }

    private async void OnStateChangesToggled(
        object sender,
        RoutedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetIncludeStateChangesAsync(StateChangesSwitch.IsOn);
        Render();
    }

    private async void OnUndatedToggled(object sender, RoutedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        await state.SetIncludeUndatedAsync(UndatedSwitch.IsOn);
        Render();
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
        await state.ReloadAsync();
        Render();
    }

    private async void OnLoadMore(object sender, RoutedEventArgs e)
    {
        await state.LoadMoreAsync();
        Render();
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
        catch (Exception)
        {
            ReportBox.Text += Environment.NewLine + "（复制到剪贴板失败；请手动选择文本。）";
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

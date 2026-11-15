// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ChallengePage : UserControl
{
    private static readonly ChallengeMode[] Modes =
    [
        ChallengeMode.SpiralAbyss,
        ChallengeMode.ImaginariumTheater,
        ChallengeMode.StygianOnslaught,
    ];

    private readonly IDiagnosticService diagnostics;
    private readonly ChallengeHistoryState state;
    private readonly WindowsHistoryCollection collection;
    private bool rendering;
    private string? lastReportedFailure;
    private CancellationTokenSource? activeCollection;

    public ChallengePage(
        ChallengeHistoryState state,
        WindowsHistoryCollection collection,
        IDiagnosticService diagnostics)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        this.collection = collection;
        this.diagnostics = diagnostics ??
            throw new ArgumentNullException(nameof(diagnostics));
        InitializeComponent();
        ModePicker.ItemsSource = Modes
            .Select(HistoryLabels.Mode)
            .ToArray();
        AutomationProperties.SetName(this, "周期挑战");
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

    public void Leave()
    {
        activeCollection?.Cancel();
        state.Cancel();
    }

    private async void OnCollect(object sender, RoutedEventArgs e)
    {
        if (activeCollection is not null ||
            state.Snapshot.SelectedArchiveId is not Guid archive ||
            state.Snapshot.SelectedAccountId is not Guid account)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        activeCollection = cancellation;
        CollectButton.IsEnabled = false;
        SetLoading(true);
        try
        {
            string? result = await collection.RunAsync(
                XamlRoot,
                archive,
                account,
                state.Snapshot.Mode,
                cancellation.Token);
            if (result is not null)
            {
                ReportStartRejection(result);
                await state.ReloadAsync(cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // User cancelled the confirmation or the page went away.
        }
        catch (Exception exception)
        {
            SafeReport(new DiagnosticEvent(
                NotificationCategory.Collection,
                "采集最新记录",
                "挑战采集失败",
                "采集未完成，请检查登录和网络后重试。",
                DiagnosticSeverity.Error,
                exception));
        }
        finally
        {
            activeCollection = null;
            SetLoading(false);
            Render();
        }
    }

    private void ReportStartRejection(string result)
    {
        if (result.StartsWith("请选择身份已确认", StringComparison.Ordinal) ||
            result.StartsWith("请在“登录账号”", StringComparison.Ordinal))
        {
            SafeReport(new DiagnosticEvent(
                NotificationCategory.Collection,
                "采集最新记录",
                "无法开始采集",
                result,
                DiagnosticSeverity.Warning));
        }
    }

    private void Render()
    {
        ChallengeHistorySnapshot snapshot = state.Snapshot;
        rendering = true;
        try
        {
            ArchivePicker.ItemsSource = snapshot.Archives;
            ArchivePicker.SelectedIndex = IndexOf(
                snapshot.Archives,
                snapshot.SelectedArchiveId);
            AccountPicker.ItemsSource = snapshot.Accounts;
            AccountPicker.SelectedIndex = IndexOf(
                snapshot.Accounts,
                snapshot.SelectedAccountId);
            ModePicker.SelectedIndex = Array.IndexOf(Modes, snapshot.Mode);
            SeasonPicker.ItemsSource = snapshot.Seasons;
            SeasonPicker.SelectedIndex = snapshot.SelectedSeasonId is string season
                ? IndexOfSeason(snapshot.Seasons, season)
                : -1;
            ObservationList.ItemsSource = snapshot.Observations;
            ObservationList.SelectedItem = snapshot.SelectedObservation;
            EmptyText.Text = snapshot.EmptyMessage;
            EmptyText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error) &&
                !string.IsNullOrWhiteSpace(snapshot.EmptyMessage)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            PreviousButton.IsEnabled = snapshot.HasPrevious && !snapshot.IsBusy;
            NextButton.IsEnabled = snapshot.HasNext && !snapshot.IsBusy;
            ReloadButton.IsEnabled = !snapshot.IsBusy;
            CollectButton.IsEnabled = !snapshot.IsBusy &&
                activeCollection is null &&
                snapshot.SelectedAccountId is not null;
            PageText.Text = snapshot.PageText;
            ReportFailureState(snapshot);
            RenderDetail();
        }
        finally
        {
            rendering = false;
        }
    }

    private void ReportFailureState(ChallengeHistorySnapshot snapshot)
    {
        if (snapshot.IsBusy)
        {
            return;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Error))
        {
            if (!string.Equals(lastReportedFailure, snapshot.Error, StringComparison.Ordinal))
            {
                lastReportedFailure = snapshot.Error;
                SafeReport(new DiagnosticEvent(
                    NotificationCategory.Collection,
                    "读取挑战观察",
                    "挑战观察读取失败",
                    "观察记录可能未完整显示，请稍后刷新。",
                    DiagnosticSeverity.Error));
            }
            return;
        }
        lastReportedFailure = null;
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

    private void RenderDetail()
    {
        ChallengeObservationView? view = state.Snapshot.SelectedObservation;
        DetailTitleText.Text = view?.Title ?? "选择一次观察查看详情";
        DetailBodyText.Text = view?.DetailText ?? string.Empty;
        AutomationProperties.SetName(
            DetailBodyText,
            view is null
                ? "挑战观察详情"
                : $"挑战观察详情：{view.Title}");
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
        PreviousButton.IsEnabled = !loading && state.Snapshot.HasPrevious;
        NextButton.IsEnabled = !loading && state.Snapshot.HasNext;
        CollectButton.IsEnabled = !loading &&
            activeCollection is null &&
            state.Snapshot.SelectedAccountId is not null;
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
        int index = AccountPicker.SelectedIndex;
        if (index < 0 || index >= state.Snapshot.Accounts.Count)
        {
            return;
        }
        await RunAsync(() => state.SelectAccountAsync(
            state.Snapshot.Accounts[index].Id));
    }

    private async void OnModeSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        int index = ModePicker.SelectedIndex;
        if (index < 0 || index >= Modes.Length)
        {
            return;
        }
        await RunAsync(() => state.SetModeAsync(Modes[index]));
    }

    private async void OnSeasonSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        if (SeasonPicker.SelectedItem is not string season)
        {
            return;
        }
        await RunAsync(() => state.SetSeasonAsync(season));
    }

    private void OnObservationSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (rendering)
        {
            return;
        }
        state.SelectObservation(
            (ObservationList.SelectedItem as ChallengeObservationView)?
                .ObservationId);
        RenderDetail();
    }

    private async void OnReload(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => state.ReloadAsync());
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => state.PreviousPageAsync());
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        await RunAsync(() => state.NextPageAsync());
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

    private static int IndexOfSeason(
        IReadOnlyList<string> seasons,
        string season)
    {
        for (int index = 0; index < seasons.Count; index++)
        {
            if (string.Equals(seasons[index], season, StringComparison.Ordinal))
            {
                return index;
            }
        }
        return -1;
    }
}

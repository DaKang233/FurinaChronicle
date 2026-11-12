// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Presentation.History;
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

    private readonly ChallengeHistoryState state;
    private bool rendering;

    public ChallengePage(ChallengeHistoryState state)
    {
        this.state = state ?? throw new ArgumentNullException(nameof(state));
        InitializeComponent();
        ModePicker.ItemsSource = Modes
            .Select(HistoryLabels.Mode)
            .ToArray();
        AutomationProperties.SetName(this, "周期挑战");
    }

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    public void Leave() => state.Cancel();

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
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            EmptyText.Text = snapshot.EmptyMessage;
            EmptyText.Visibility = string.IsNullOrWhiteSpace(snapshot.EmptyMessage)
                ? Visibility.Collapsed
                : Visibility.Visible;
            PreviousButton.IsEnabled = snapshot.HasPrevious && !snapshot.IsBusy;
            NextButton.IsEnabled = snapshot.HasNext && !snapshot.IsBusy;
            ReloadButton.IsEnabled = !snapshot.IsBusy;
            PageText.Text = snapshot.PageText;
            AutomationProperties.SetName(
                StatusText,
                snapshot.IsBusy ? "周期挑战正在载入" : "周期挑战状态");
            RenderDetail();
        }
        finally
        {
            rendering = false;
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
        if (index < 0 || index >= state.Snapshot.Accounts.Count)
        {
            return;
        }
        await state.SelectAccountAsync(state.Snapshot.Accounts[index].Id);
        Render();
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
        await state.SetModeAsync(Modes[index]);
        Render();
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
        await state.SetSeasonAsync(season);
        Render();
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
        await state.ReloadAsync();
        Render();
    }

    private async void OnPrevious(object sender, RoutedEventArgs e)
    {
        await state.PreviousPageAsync();
        Render();
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        await state.NextPageAsync();
        Render();
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

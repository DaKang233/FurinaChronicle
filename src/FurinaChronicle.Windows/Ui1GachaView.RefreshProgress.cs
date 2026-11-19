// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;
using Microsoft.UI.Xaml;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private GachaRefreshDisplayState? refreshDisplay;
    private TaskCompletionSource<bool>? refreshDismissed;
    private bool refreshHostClosing;

    public bool IsRefreshInteractionLocked => refreshDisplay is not null;
    public bool IsRefreshRunning => refreshDisplay?.IsRunning == true;
    public event EventHandler? RefreshInteractionChanged;

    private async Task<T> WithRefreshProgressAsync<T>(
        string title, Func<IProgress<GachaRefreshProgress>, Task<T>> operation,
        Func<T, (bool Succeeded, GachaRefreshResult? Result)> result)
    {
        if (!entered || refreshDisplay is not null || dispatcher is null || environment is null)
            throw new InvalidOperationException("The refresh display is unavailable.");
        var display = new GachaRefreshDisplayState(title,
            id => id is null ? null : environment.GachaIconCache.GetCachedPath(FurinaChronicle.Core.Gacha.GachaGame.GenshinImpact, id));
        refreshDisplay = display;
        refreshDismissed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        display.PropertyChanged += OnRefreshDisplayChanged;
        using var progress = new GachaRefreshProgressReporter(dispatcher, display.Apply);
        RefreshItems.ItemsSource = display.Items;
        GachaWorkspace.IsEnabled = false;
        RefreshOverlay.Visibility = Visibility.Visible;
        RenderRefreshDisplay();
        UpdateCommandAvailability();
        RefreshInteractionChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            // Give the overlay a frame before credential discovery or network work.
            await Task.Yield();
            T outcome;
            try
            {
                outcome = await operation(progress);
            }
            catch
            {
                await progress.DrainAsync();
                display.Complete(false, null);
                if (!refreshHostClosing) await refreshDismissed.Task;
                throw;
            }
            await progress.DrainAsync();
            var completed = result(outcome);
            display.Complete(completed.Succeeded, completed.Result);
            if (!completed.Succeeded && !refreshHostClosing) await refreshDismissed.Task;
            return outcome;
        }
        finally
        {
            display.PropertyChanged -= OnRefreshDisplayChanged;
            refreshDisplay = null;
            refreshDismissed = null;
            if (!refreshHostClosing)
            {
                RefreshItems.ItemsSource = null;
                RefreshOverlay.Visibility = Visibility.Collapsed;
                GachaWorkspace.IsEnabled = true;
                RefreshInteractionChanged?.Invoke(this, EventArgs.Empty);
                UpdateCommandAvailability();
            }
        }
    }

    private void OnRefreshDisplayChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
        RenderRefreshDisplay();

    private void RenderRefreshDisplay()
    {
        if (refreshHostClosing || refreshDisplay is not { } display) return;
        RefreshTitle.Text = display.Title;
        RefreshPool.Text = display.CurrentPool;
        RefreshDetail.Text = display.Detail;
        RefreshTotals.Text = display.Totals;
        RefreshRing.IsActive = display.IsRunning;
        RefreshClose.IsEnabled = !display.IsRunning;
    }

    private void OnCloseRefresh(object sender, RoutedEventArgs args)
    {
        if (!IsRefreshRunning) refreshDismissed?.TrySetResult(true);
    }

    private void CloseRefreshForShutdown()
    {
        refreshHostClosing = true;
        refreshDismissed?.TrySetResult(true);
    }
}

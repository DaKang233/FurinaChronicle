// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Pages;

public partial class GachaRefreshProgressPage : ContentPage
{
    private readonly TaskCompletionSource<string?> closeRequested =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private GachaPageViewModel ViewModel =>
        (GachaPageViewModel)BindingContext;

    public GachaRefreshProgressPage(GachaPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    /// <summary>
    /// Completes when the user dismisses the progress page after a failure.
    /// </summary>
    public Task<string?> WaitForCloseAsync() => closeRequested.Task;

    protected override bool OnBackButtonPressed()
    {
        if (ViewModel.IsRefreshRunning)
        {
            return true;
        }

        closeRequested.TrySetResult(null);
        return true;
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        closeRequested.TrySetResult(null);
    }
}

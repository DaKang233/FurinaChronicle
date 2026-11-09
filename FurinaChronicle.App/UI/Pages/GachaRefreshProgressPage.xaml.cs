// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Pages;

public partial class GachaRefreshProgressPage : ContentPage
{
    private GachaPageViewModel ViewModel =>
        (GachaPageViewModel)BindingContext;

    public GachaRefreshProgressPage(GachaPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override bool OnBackButtonPressed()
    {
        return ViewModel.IsRefreshRunning || base.OnBackButtonPressed();
    }
}

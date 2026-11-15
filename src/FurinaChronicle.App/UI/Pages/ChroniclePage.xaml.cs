// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Pages;

public partial class ChroniclePage : ContentPage
{
    private readonly ChroniclePageViewModel viewModel;

    public ChroniclePage(ChroniclePageViewModel viewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ??
            throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelActiveLoad();
        base.OnDisappearing();
    }

    private void OnToggleFiltersClicked(object? sender, EventArgs e)
    {
        AdvancedFilters.IsVisible = !AdvancedFilters.IsVisible;
        if (sender is Button button)
        {
            button.Text = AdvancedFilters.IsVisible ? "收起筛选" : "更多筛选";
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await viewModel.ReloadAsync();
    }
}

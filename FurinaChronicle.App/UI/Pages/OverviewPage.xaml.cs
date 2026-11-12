// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Pages;

public partial class OverviewPage : ContentPage
{
    private readonly OverviewPageViewModel viewModel;

    public OverviewPage(
        OverviewPageViewModel viewModel,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.viewModel = viewModel ??
            throw new ArgumentNullException(nameof(viewModel));
        BindingContext = viewModel;
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }

    // UI-0/UI-1 are technical previews; keep them reachable only in the isolated
    // validation build so the product destinations stay focused.
    public bool ShowValidationTools =>
#if PHASE8C_VALIDATION
        true;
#else
        false;
#endif

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ValidationToolsSection.IsVisible = ShowValidationTools;
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        viewModel.CancelActiveLoad();
        base.OnDisappearing();
    }

    private async void OnContinueGachaClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("gacha-page");
    }

    private async void OnOpenChronicleClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("//main/chronicle");
    }

    private async void OnOpenDataHubClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("//main/data");
    }

    private async void OnOpenArchiveClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("//main/archive");
    }

    private async void OnOpenUi0PreviewClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("ui0-preview");
    }

    private async void OnOpenUi1PreviewClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("ui1-preview");
    }

    private static async Task NavigateAsync(string route)
    {
        if (Shell.Current is Shell shell)
        {
            await shell.GoToAsync(route, animate: true);
        }
    }
}

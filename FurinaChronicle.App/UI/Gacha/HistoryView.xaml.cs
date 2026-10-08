// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Gacha;

public partial class HistoryView : ContentView
{
    public HistoryView()
    {
        InitializeComponent();
        bool isAndroid = OperatingSystem.IsAndroid();
        DesktopLayout.IsVisible = !isAndroid;
        MobileLayout.IsVisible = isAndroid;
        ShowMobileList();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        ShowMobileList();
    }

    private void OnHistoryPoolTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not TapGestureRecognizer gesture ||
            gesture.CommandParameter is not GachaHistoryDisplayItem item ||
            BindingContext is not GachaAnalysisViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedHistoryItem = item;
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        MobileListLayout.IsVisible = false;
        MobileDetailLayout.IsVisible = true;
    }

    private void OnMobileBackClicked(object? sender, EventArgs e) =>
        ShowMobileList();

    private void ShowMobileList()
    {
        if (!OperatingSystem.IsAndroid())
        {
            return;
        }

        MobileListLayout.IsVisible = true;
        MobileDetailLayout.IsVisible = false;
    }
}

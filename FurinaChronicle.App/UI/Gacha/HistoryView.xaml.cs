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

    private void OnMobileHistoryItemClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button ||
            button.CommandParameter is not GachaHistoryDisplayItem item ||
            BindingContext is not GachaAnalysisViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedHistoryItem = item;
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

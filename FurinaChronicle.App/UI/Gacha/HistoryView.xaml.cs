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

    private void OnMobileHistorySelectionChanged(
        object? sender,
        SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not
                GachaHistoryDisplayItem item ||
            BindingContext is not GachaAnalysisViewModel viewModel)
        {
            return;
        }

        viewModel.SelectedHistoryItem = item;
        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
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
        MobileHistoryCollection.SelectedItem = null;
    }
}

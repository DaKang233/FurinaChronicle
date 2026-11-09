// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

public partial class ArchiveOverviewView : ContentView
{
    public static readonly BindableProperty OverviewCardHeightProperty =
        BindableProperty.Create(
            nameof(OverviewCardHeight),
            typeof(double),
            typeof(ArchiveOverviewView),
            -1D);

    public double OverviewCardHeight
    {
        get => (double)GetValue(OverviewCardHeightProperty);
        private set => SetValue(OverviewCardHeightProperty, value);
    }

    public ArchiveOverviewView()
    {
        InitializeComponent();
#if WINDOWS
        OverviewCollection.ItemsLayout =
            new LinearItemsLayout(ItemsLayoutOrientation.Horizontal)
            {
                ItemSpacing = 10D
            };
#endif
    }

    private void OnOverviewSizeChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        double availableHeight = Height - OverviewHeader.Height - 8D;
        OverviewCardHeight = double.IsFinite(availableHeight) &&
            availableHeight > 0D
                ? availableHeight
                : -1D;
#endif
    }
}

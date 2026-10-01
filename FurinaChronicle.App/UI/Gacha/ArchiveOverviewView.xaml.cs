// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

public partial class ArchiveOverviewView : ContentView
{
    public ArchiveOverviewView()
    {
        InitializeComponent();
    }

    private void OnOverviewSizeChanged(object? sender, EventArgs e)
    {
        ResponsiveOverviewLayout.Update(OverviewItemsLayout, Width);
    }
}

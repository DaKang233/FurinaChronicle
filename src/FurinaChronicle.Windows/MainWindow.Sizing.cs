// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FurinaChronicle.Windows;

public sealed partial class MainWindow
{
    private XamlRoot? sizingRoot;

    private void InitializeWindowSizing()
    {
        if (!ReferenceEquals(sizingRoot, RootGrid.XamlRoot))
        {
            DetachWindowSizing();
            sizingRoot = RootGrid.XamlRoot;
            if (sizingRoot is not null) sizingRoot.Changed += OnSizingRootChanged;
        }
        ApplyMinimumWindowSize();
    }

    private void DetachWindowSizing()
    {
        if (sizingRoot is not null) sizingRoot.Changed -= OnSizingRootChanged;
        sizingRoot = null;
    }

    private void OnSizingRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) =>
        ApplyMinimumWindowSize();

    private void ApplyMinimumWindowSize()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        // MAUI's existing minimum is expressed in logical pixels. AppWindow sizes are physical pixels.
        double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1;
        int width = (int)Math.Ceiling(1000 * scale);
        int height = (int)Math.Ceiling(600 * scale);
        if (presenter.PreferredMinimumWidth != width) presenter.PreferredMinimumWidth = width;
        if (presenter.PreferredMinimumHeight != height) presenter.PreferredMinimumHeight = height;
        SizeInt32 size = AppWindow.Size;
        if (presenter.State == OverlappedPresenterState.Restored && (size.Width < width || size.Height < height))
            AppWindow.Resize(new SizeInt32(Math.Max(width, size.Width), Math.Max(height, size.Height)));
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private readonly HashSet<FrameworkElement> overviewCards = [];

    private void OnOverviewCardLoaded(object sender, RoutedEventArgs args)
    {
        var card = (FrameworkElement)sender;
        overviewCards.Add(card);
        SizeOverviewCard(card);
    }

    private void OnOverviewCardUnloaded(object sender, RoutedEventArgs args) =>
        overviewCards.Remove((FrameworkElement)sender);

    private void OnOverviewViewportSizeChanged(object sender, SizeChangedEventArgs args)
    {
        foreach (FrameworkElement card in overviewCards) SizeOverviewCard(card);
    }

    private void SizeOverviewCard(FrameworkElement card)
    {
        double height = OverviewScroll.ViewportHeight > 0
            ? OverviewScroll.ViewportHeight
            : OverviewScroll.ActualHeight;
        if (height > 0) card.Height = height;
    }

    private void OnOverviewWheel(object sender, PointerRoutedEventArgs args)
    {
        if (IsRefreshInteractionLocked || state?.Snapshot.View != GachaReadOnlyView.Overview) return;
        for (DependencyObject? element = args.OriginalSource as DependencyObject;
             element is not null && element != this;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is ListViewBase or ComboBox) return;
        }
        var properties = args.GetCurrentPoint(OverviewScroll).Properties;
        if (properties.IsHorizontalMouseWheel) return;
        OverviewScroll.ChangeView(
            OverviewScroll.HorizontalOffset - properties.MouseWheelDelta,
            null, null);
        args.Handled = true;
    }

    private void OnOverviewTruePulls(object sender, RoutedEventArgs args) =>
        ((sender as FrameworkElement)?.DataContext as GachaOverviewCard)?.ToggleTruePulls();

    private void OnOverviewLayout(object sender, RoutedEventArgs args) =>
        ((sender as FrameworkElement)?.DataContext as GachaOverviewCard)?.ToggleLayout();

    private void OnOverviewMore(object sender, RoutedEventArgs args) =>
        ((sender as FrameworkElement)?.DataContext as GachaOverviewCard)?.LoadMore();
}

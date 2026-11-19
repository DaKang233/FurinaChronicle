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
    private void OnOverviewViewportSizeChanged(object sender, SizeChangedEventArgs args) => SizeOverviewCards();

    private void SizeOverviewCards()
    {
        double height = OverviewScroll.ViewportHeight > 0 ? OverviewScroll.ViewportHeight : OverviewScroll.ActualHeight;
        if (OverviewCards.ItemsSource is IEnumerable<GachaOverviewCard> cards)
            foreach (GachaOverviewCard card in cards) card.SetViewportHeight(height);
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

}

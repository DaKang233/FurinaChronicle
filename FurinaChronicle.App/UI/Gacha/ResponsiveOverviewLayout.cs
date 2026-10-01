// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Gacha;

internal static class ResponsiveOverviewLayout
{
    private const double MinimumCardWidth = 360D;
    private const double CardSpacing = 10D;

    public static void Update(GridItemsLayout layout, double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(layout);

        int span = 1;
#if WINDOWS
        if (double.IsFinite(availableWidth) && availableWidth > 0D)
        {
            span = Math.Max(
                1,
                (int)Math.Floor(
                    (availableWidth + CardSpacing) /
                    (MinimumCardWidth + CardSpacing)));
        }
#endif
        if (layout.Span != span)
        {
            layout.Span = span;
        }
    }
}

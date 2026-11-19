// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace FurinaChronicle.Windows;

public sealed class GachaPullBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        double ratio = value is double known && double.IsFinite(known) ? Math.Clamp(known, 0, 1) : 0;
        return new SolidColorBrush(Color.FromArgb(255,
            (byte)(55 + 190 * ratio), (byte)(190 - 100 * ratio), (byte)(120 - 55 * ratio)));
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

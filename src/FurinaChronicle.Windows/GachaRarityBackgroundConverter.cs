// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows;

public sealed class GachaRarityBackgroundConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is int rarity and >= 1 and <= 5
            ? new BitmapImage(new Uri($"ms-appx:///Assets/ItemRarity/item_background_{rarity}.png"))
            : null;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

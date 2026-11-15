// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows;

public sealed class CachedImageConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language) =>
        value is string path && Path.IsPathFullyQualified(path) && File.Exists(path)
            ? new BitmapImage(new Uri(path)) : null;
    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

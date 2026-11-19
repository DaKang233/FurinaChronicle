// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows;

/// <summary>Downloaded cache files keep their path when their contents are refreshed.</summary>
internal static class CachedArtworkBitmap
{
    public static BitmapImage FromFile(string path, int decodePixelWidth) => new()
    {
        DecodePixelWidth = decodePixelWidth,
        // Set the options before UriSource starts loading. The disk cache owns freshness;
        // WinUI must not reuse decoded pixels from a previous version of the same file.
        CreateOptions = BitmapCreateOptions.IgnoreImageCache,
        UriSource = new Uri(path),
    };
}

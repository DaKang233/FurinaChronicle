// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows;

internal static class CachedArtworkBitmap
{
    private const int MaximumEntries = 512;
    private readonly record struct Key(string Path, int Width, long Modified, long Length, int Thread);
    private static readonly Dictionary<Key, WeakReference<BitmapImage>> Images = [];

    public static BitmapImage FromFile(string path, int decodePixelWidth)
    {
        var file = new FileInfo(path);
        var key = new Key(file.FullName, decodePixelWidth, file.LastWriteTimeUtc.Ticks,
            file.Length, Environment.CurrentManagedThreadId);
        lock (Images)
        {
            if (Images.TryGetValue(key, out var reference) && reference.TryGetTarget(out var cached))
                return cached;
            if (Images.Count >= MaximumEntries)
            {
                foreach (var expired in Images.Where(pair => !pair.Value.TryGetTarget(out _) ||
                    (pair.Key.Path == key.Path && pair.Key != key)).Select(pair => pair.Key).ToArray())
                    Images.Remove(expired);
                while (Images.Count >= MaximumEntries) Images.Remove(Images.Keys.First());
            }
            var bitmap = new BitmapImage
            {
                DecodePixelWidth = decodePixelWidth,
                CreateOptions = BitmapCreateOptions.IgnoreImageCache,
                UriSource = new Uri(file.FullName),
            };
            Images[key] = new(bitmap);
            return bitmap;
        }
    }
}

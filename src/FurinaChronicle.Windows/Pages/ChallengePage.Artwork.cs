// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.History;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ChallengePage
{
    private readonly Dictionary<Image, CancellationTokenSource> artworkLoads = [];

    private void OnArtworkLoaded(object sender, RoutedEventArgs args) => LoadArtwork((Image)sender);
    private void OnArtworkUnloaded(object sender, RoutedEventArgs args) => CancelArtwork((Image)sender);
    private void OnArtworkContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image { IsLoaded: true } image) LoadArtwork(image);
    }

    private void CancelArtwork(Image image)
    {
        if (artworkLoads.Remove(image, out var pending)) pending.Cancel();
        image.Source = null; // Native bitmaps must not outlive recycled or hidden portrait controls.
    }

    private void CancelAllArtwork()
    {
        foreach (Image image in artworkLoads.Keys.ToArray()) CancelArtwork(image);
    }

    private async void LoadArtwork(Image image)
    {
        CancelArtwork(image);
        if (image.DataContext is not ChallengePortrait portrait) return;
        AutomationProperties.SetName(image, "挑战图片：" + portrait.Label);
        if (StygianDifficultyArtwork.IsBundledKey(portrait.ResourceKey))
        {
            try
            {
                image.Source = new BitmapImage(new Uri(
                    $"ms-appx:///Assets/ChallengeDifficulty/{portrait.ResourceKey!.ToLowerInvariant()}.png"))
                { DecodePixelWidth = 96 };
            }
            catch (System.Runtime.InteropServices.COMException) { }
            return;
        }
        if (portrait.Icon is not { } source) return;
        using var load = new CancellationTokenSource();
        artworkLoads[image] = load;
        try
        {
            // Show stale artwork immediately; refresh uses one shared file and keeps it on failure.
            string? cached = artworkCache.GetCachedPath(source);
            if (cached is not null)
                image.Source = CachedArtworkBitmap.FromFile(cached, 96);
            string? path = await artworkCache.GetOrRefreshAsync(source, load.Token);
            if (path is not null && !load.IsCancellationRequested && image.IsLoaded &&
                ReferenceEquals(image.DataContext, portrait))
                image.Source = CachedArtworkBitmap.FromFile(path, 96);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // An unavailable picture never hides the name or breaks the saved challenge record.
        }
        finally
        {
            if (artworkLoads.TryGetValue(image, out var current) && ReferenceEquals(current, load))
                artworkLoads.Remove(image);
        }
    }
}

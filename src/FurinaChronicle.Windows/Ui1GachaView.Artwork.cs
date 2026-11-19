// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Metadata;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private IGachaItemMetadataProvider? itemMetadata;
    private readonly Dictionary<Image, CancellationTokenSource> artworkLoads = [];

    private void OnArtworkLoaded(object sender, RoutedEventArgs args) =>
        LoadArtwork((Image)sender);

    private void OnArtworkUnloaded(object sender, RoutedEventArgs args) =>
        CancelArtwork((Image)sender);

    private void OnArtworkContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is Image { IsLoaded: true } image)
        {
            LoadArtwork(image);
        }
    }

    private void CancelArtwork(Image image)
    {
        if (artworkLoads.Remove(image, out var pending))
        {
            pending.Cancel();
        }
        image.Source = null;
    }

    private async void LoadArtwork(Image image)
    {
        CancelArtwork(image);
        object? data = image.DataContext;
        if (data is GachaHistoryBannerVisual) image.Visibility = Visibility.Collapsed;
        if (environment is null || data is not (GachaReadOnlyRow or GachaRefreshDisplayItem or GachaHistoryBannerVisual))
        {
            return;
        }

        using var load = new CancellationTokenSource();
        artworkLoads.Add(image, load);
        try
        {
            string? initialPath = data switch
            {
                GachaReadOnlyRow row => row.Image.CachedPath,
                GachaRefreshDisplayItem item => item.Image.CachedPath,
                _ => null,
            };
            if (initialPath is not null && File.Exists(initialPath))
            {
                image.Source = CachedArtworkBitmap.FromFile(initialPath, 96);
            }

            string? path = data switch
            {
                GachaReadOnlyRow row => await FindItemIconAsync(row.Image, load.Token),
                GachaRefreshDisplayItem item => await FindItemIconAsync(item.Image, load.Token),
                GachaHistoryBannerVisual banner =>
                    await environment.GachaBannerCache.GetCachedPathAsync(GachaGame.GenshinImpact, banner.Id, load.Token),
                _ => null,
            };
            if (!load.IsCancellationRequested && ReferenceEquals(image.DataContext, data) &&
                path is not null && File.Exists(path))
            {
                image.Source = CachedArtworkBitmap.FromFile(path, data is GachaHistoryBannerVisual ? 640 : 96);
                image.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException) when (load.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or GachaMetadataUnavailableException or
                System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // Artwork is optional and the item or banner text remains available.
        }
        finally
        {
            if (artworkLoads.TryGetValue(image, out var current) && ReferenceEquals(current, load))
            {
                artworkLoads.Remove(image);
            }
        }
    }

    private async Task<string?> FindItemIconAsync(GachaImageReference icon, CancellationToken token)
    {
        if (environment is null || string.IsNullOrWhiteSpace(icon.ResourceKey))
        {
            return null;
        }

        const GachaGame game = GachaGame.GenshinImpact;
        string? cached = environment.GachaIconCache.GetCachedPath(game, icon.ResourceKey);
        if (environment.IsSynthetic)
        {
            return cached;
        }

        string? freshPath = environment.GachaIconCache.GetCachedPath(game, icon.ResourceKey, freshOnly: true);
        if (freshPath is not null)
        {
            return freshPath;
        }

        string? url = icon.SourceUrl;
        if (url is null && itemMetadata is not null)
        {
            try
            {
                url = (await itemMetadata.FindByIdAsync(game, icon.ResourceKey, token))?.IconUrl;
            }
            catch (GachaMetadataUnavailableException)
            {
                return cached;
            }
        }
        return await environment.GachaIconCache.GetOrRefreshAsync(game, icon.ResourceKey, url, token);
    }
}

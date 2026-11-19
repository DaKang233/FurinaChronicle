// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace FurinaChronicle.Windows;

/// <summary>Binding-driven artwork: no native event subscriptions in recycled item templates.</summary>
public sealed class GachaArtworkConverter : IValueConverter
{
    private WindowsHostEnvironment? environment;
    private IGachaItemMetadataProvider? metadata;
    private CancellationTokenSource generation = new();
    private readonly SemaphoreSlim downloads = new(4, 4);
    private readonly Dictionary<string, WeakReference<GachaArtworkView>> views = [];

    public void Configure(WindowsHostEnvironment host, IGachaItemMetadataProvider? provider)
    {
        environment = host;
        metadata = provider;
        BeginScope();
    }

    public void BeginScope()
    {
        generation.Cancel();
        generation.Dispose();
        generation = new();
        views.Clear();
    }

    public void CancelPending()
    {
        generation.Cancel();
        views.Clear();
    }

    public void InvalidateBanners()
    {
        foreach (string key in views.Keys.Where(key => key.StartsWith("banner:", StringComparison.Ordinal)).ToArray())
            views.Remove(key);
    }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        string name = value switch
        {
            GachaReadOnlyRow row => row.Name,
            GachaRefreshDisplayItem item => item.Name,
            GachaHistoryBannerVisual banner => banner.Name,
            _ => string.Empty,
        };
        string? key = value switch
        {
            GachaReadOnlyRow row => "item:" + (row.Image.ResourceKey ?? "name:" + row.Name),
            GachaRefreshDisplayItem item => "item:" + (item.Image.ResourceKey ?? "name:" + item.Name),
            GachaHistoryBannerVisual banner => "banner:" + banner.Id,
            _ => null,
        };
        if (key is not null && views.TryGetValue(key, out var weak) && weak.TryGetTarget(out var existing))
            return existing;
        var view = new GachaArtworkView(name);
        if (key is null || environment is null || generation.IsCancellationRequested) return view;
        if (views.Count >= 512)
        {
            foreach (string expired in views.Where(pair => !pair.Value.TryGetTarget(out _)).Select(pair => pair.Key).ToArray())
                views.Remove(expired);
            while (views.Count >= 512) views.Remove(views.Keys.First());
        }
        views[key] = new(view);
        _ = LoadAsync(view, value, generation.Token);
        return view;
    }

    private async Task LoadAsync(GachaArtworkView view, object data, CancellationToken token)
    {
        bool acquired = false;
        try
        {
            string? initial = data switch
            {
                GachaReadOnlyRow row => row.Image.CachedPath,
                GachaRefreshDisplayItem item => item.Image.CachedPath,
                _ => null,
            };
            if (initial is not null && File.Exists(initial))
                view.SetSource(CachedArtworkBitmap.FromFile(initial, 96));
            await downloads.WaitAsync(token);
            acquired = true;
            string? path = data switch
            {
                GachaReadOnlyRow row => await FindItemAsync(row.Image, row.Name, token),
                GachaRefreshDisplayItem item => await FindItemAsync(item.Image, item.Name, token),
                GachaHistoryBannerVisual banner => await environment!.GachaBannerCache.GetCachedPathAsync(
                    GachaGame.GenshinImpact, banner.Id, token),
                _ => null,
            };
            if (!token.IsCancellationRequested && path is not null && File.Exists(path))
                view.SetSource(CachedArtworkBitmap.FromFile(path, data is GachaHistoryBannerVisual ? 640 : 96));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            // Optional public artwork must not affect facts or expose request details in diagnostics.
        }
        finally
        {
            if (acquired) downloads.Release();
        }
    }

    private async Task<string?> FindItemAsync(GachaImageReference icon, string? name, CancellationToken token)
    {
        if (environment is null) return null;
        if (string.IsNullOrWhiteSpace(icon.ResourceKey) && !environment.IsSynthetic)
            icon = await GachaArtworkResolver.ResolveAsync(icon, name, metadata, token);
        if (string.IsNullOrWhiteSpace(icon.ResourceKey)) return null;
        string itemId = icon.ResourceKey;
        const GachaGame game = GachaGame.GenshinImpact;
        string? cached = environment.GachaIconCache.GetCachedPath(game, itemId);
        if (environment.IsSynthetic) return cached;
        if (environment.GachaIconCache.GetCachedPath(game, itemId, freshOnly: true) is { } fresh) return fresh;
        if (icon.SourceUrl is null)
            icon = await GachaArtworkResolver.ResolveAsync(icon, name, metadata, token);
        return await environment.GachaIconCache.GetOrRefreshAsync(game, itemId, icon.SourceUrl, token);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}

public sealed class GachaArtworkView(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public ImageSource? Source { get; private set; }
    public bool HasImage => Source is not null;
    public event PropertyChangedEventHandler? PropertyChanged;

    internal void SetSource(ImageSource source)
    {
        if (ReferenceEquals(Source, source)) return;
        Source = source;
        PropertyChanged?.Invoke(this, new(nameof(Source)));
        PropertyChanged?.Invoke(this, new(nameof(HasImage)));
    }
}

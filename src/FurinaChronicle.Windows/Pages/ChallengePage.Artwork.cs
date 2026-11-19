// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace FurinaChronicle.Windows.Pages;

internal sealed class ChallengeArtworkSource(IChallengeArtworkCache cache)
{
    private readonly Dictionary<string, WeakReference<ChallengeArtworkView>> views = [];
    private readonly SemaphoreSlim gate = new(4, 4);
    private CancellationTokenSource pending = new();

    public void Reset()
    {
        pending.Cancel(); pending.Dispose(); pending = new(); views.Clear();
    }

    public void Cancel() { pending.Cancel(); views.Clear(); }

    public ChallengeArtworkView Get(ChallengePortrait portrait)
    {
        string key = portrait.Icon?.AbsoluteUri ?? portrait.ResourceKey ?? portrait.Label;
        if (views.TryGetValue(key, out var weak) && weak.TryGetTarget(out var existing)) return existing;
        var view = new ChallengeArtworkView();
        if (views.Count >= 512)
        {
            foreach (var expired in views.Where(item => !item.Value.TryGetTarget(out _)).Select(item => item.Key).ToArray()) views.Remove(expired);
            while (views.Count >= 512) views.Remove(views.Keys.First());
        }
        views[key] = new(view);
        _ = LoadAsync(view, portrait, pending.Token);
        return view;
    }

    private async Task LoadAsync(ChallengeArtworkView view, ChallengePortrait portrait, CancellationToken token)
    {
        bool acquired = false;
        try
        {
            if (StygianDifficultyArtwork.IsBundledKey(portrait.ResourceKey))
            {
                view.Set(new BitmapImage(new Uri($"ms-appx:///Assets/ChallengeDifficulty/{portrait.ResourceKey!.ToLowerInvariant()}.png")) { DecodePixelWidth = 96 });
                return;
            }
            if (portrait.Icon is not { } uri || token.IsCancellationRequested) return;
            if (cache.GetCachedPath(uri) is { } initial) view.Set(CachedArtworkBitmap.FromFile(initial, 96));
            await gate.WaitAsync(token); acquired = true;
            string? path = await cache.GetOrRefreshAsync(uri, token);
            if (!token.IsCancellationRequested && path is not null) view.Set(CachedArtworkBitmap.FromFile(path, 96));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception) { /* Optional public artwork never invalidates a saved observation. */ }
        finally { if (acquired) gate.Release(); }
    }
}

internal sealed class ChallengeArtworkView : INotifyPropertyChanged
{
    public ImageSource? Source { get; private set; }
    public Microsoft.UI.Xaml.Visibility MissingVisibility => Source is null ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Set(ImageSource source)
    {
        if (ReferenceEquals(source, Source)) return;
        Source = source;
        PropertyChanged?.Invoke(this, new(nameof(Source)));
        PropertyChanged?.Invoke(this, new(nameof(MissingVisibility)));
    }
}

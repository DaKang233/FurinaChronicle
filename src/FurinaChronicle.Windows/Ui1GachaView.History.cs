// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace FurinaChronicle.Windows;

public sealed partial class Ui1GachaView
{
    private PreloadGachaBannerImages? bannerPreloader;
    private CancellationTokenSource? bannerCacheCancellation;
    private Task? bannerCacheTask;
    private bool automaticBannerCacheStarted;
    private string? selectedHistoryId;
    private string? selectedHistoryVersion;

    private void RenderHistory(GachaReadOnlySnapshot snapshot)
    {
        bool visible = snapshot.View == GachaReadOnlyView.History;
        HistoryPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        bool previousRendering = rendering;
        rendering = true;
        try
        {
            string[] versions = visible
                ? snapshot.Rows.Select(row => row.History?.Version).OfType<string>().Distinct().ToArray()
                : [];
            if (versions.Length > 0 && !versions.Contains(selectedHistoryVersion))
                selectedHistoryVersion = versions[0];
            HistoryVersionPicker.ItemsSource = versions;
            HistoryVersionPicker.SelectedItem = selectedHistoryVersion;
            HistoryVersionPicker.IsEnabled = versions.Length > 0 && !snapshot.IsBusy;
            HistoryList.IsEnabled = !snapshot.IsBusy;
            RenderHistorySelection(visible ? snapshot.Rows : []);
            UpdateBannerCacheAvailability();
        }
        finally
        {
            rendering = previousRendering;
        }
        if (visible && !snapshot.IsBusy && !automaticBannerCacheStarted &&
            environment is { IsSynthetic: false })
        {
            automaticBannerCacheStarted = true;
            DispatcherQueue.TryEnqueue(async () =>
            {
                if (entered) await StartBannerCacheAsync();
                else automaticBannerCacheStarted = false;
            });
        }
    }

    private void OnHistoryVersionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (rendering || state is null) return;
        selectedHistoryVersion = HistoryVersionPicker.SelectedItem as string;
        RenderHistorySelection(state.Snapshot.Rows);
    }

    private void RenderHistorySelection(IReadOnlyList<GachaReadOnlyRow> rows)
    {
        bool previousRendering = rendering;
        rendering = true;
        try
        {
            GachaReadOnlyRow[] shown = rows.Where(row => row.History?.Version == selectedHistoryVersion).ToArray();
            HistoryList.ItemsSource = shown;
            HistoryList.SelectedItem = shown.FirstOrDefault(row =>
                row.History?.Id == selectedHistoryId) ?? shown.FirstOrDefault();
            RenderHistoryDetail();
        }
        finally
        {
            rendering = previousRendering;
        }
    }

    private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!rendering) RenderHistoryDetail();
    }

    private void RenderHistoryDetail()
    {
        GachaHistoryCard? card = (HistoryList.SelectedItem as GachaReadOnlyRow)?.History;
        if (card is not null) selectedHistoryId = card.Id;
        HistoryContent.ContentTemplate = (DataTemplate)Resources[
            HistoryIconToggle.IsOn ? "SelectedHistoryTemplate" : "SelectedHistoryTextTemplate"];
        HistoryContent.Content = card;
        HistoryText.Text = card is null ? "选择一个卡池查看本期记录。" : string.Empty;
        HistoryText.Visibility = card is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnHistoryDisplayChanged(object sender, RoutedEventArgs args)
    {
        if (HistoryText is null || HistoryContent is null) return;
        RenderHistoryDetail();
    }

    private void OnHistoryMore(object sender, RoutedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is GachaHistoryCard card) card.LoadMore();
    }

    private async void OnCacheBanners(object sender, RoutedEventArgs args) => await StartBannerCacheAsync();

    private async Task StartBannerCacheAsync()
    {
        if (bannerCacheTask is not null || bannerPreloader is null) return;
        using var cancellation = new CancellationTokenSource();
        bannerCacheCancellation = cancellation;
        Task work = CacheBannersCoreAsync(cancellation.Token);
        bannerCacheTask = work;
        UpdateBannerCacheAvailability();
        try
        {
            await work;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            automaticBannerCacheStarted = false;
            ClearProgress("gacha.cache.banners");
        }
        catch (Exception exception)
        {
            automaticBannerCacheStarted = false;
            ReportFailure("gacha.cache.banners", "gacha.cache.banners", "横幅缓存失败",
                "横幅缓存未完成，可点击缓存按钮重试。", exception);
        }
        finally
        {
            bannerCacheTask = null;
            bannerCacheCancellation = null;
            UpdateBannerCacheAvailability();
        }
    }

    private async Task CacheBannersCoreAsync(CancellationToken token)
    {
        PublishProgress("gacha.cache.banners", "缓存卡池横幅", "正在批量缓存横幅，完成后重新加载图片。");
        GachaBannerImagePreloadResult result = await bannerPreloader!.ExecuteAsync(GachaGame.GenshinImpact, token);
        if (result.Failed > 0)
        {
            PublishWarning("gacha.cache.banners", "横幅缓存完成",
                $"已缓存 {result.Succeeded} / {result.Total} 张，{result.Failed} 张未缓存，可稍后重试。");
        }
        else
        {
            PublishSuccess("gacha.cache.banners", "横幅缓存完成", $"已缓存 {result.Succeeded} 张横幅。");
        }
        if (entered && state?.Snapshot is { View: GachaReadOnlyView.History, IsBusy: false })
        {
            await state.SelectViewAsync(GachaReadOnlyView.History);
        }
    }

    private void UpdateBannerCacheAvailability()
    {
        bool ready = bannerCacheTask is null && state?.Snapshot.IsBusy != true;
        CacheBannersButton.IsEnabled = ready;
        ClearBannersButton.IsEnabled = ready;
    }

    private async void OnClearBanners(object sender, RoutedEventArgs args)
    {
        if (environment is null || bannerCacheTask is not null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除横幅缓存",
            Content = "将清除本机下载的卡池横幅，可通过缓存按钮重新下载。",
            PrimaryButtonText = "清除缓存",
            CloseButtonText = "取消"
        };
        AutomationProperties.SetAutomationId(dialog, "GachaClearBannerCacheDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        bannerCacheCancellation?.Cancel();
        if (bannerCacheTask is Task runningCache)
        {
            try { await runningCache; } catch (Exception) { }
        }
        automaticBannerCacheStarted = true;
        try
        {
            HistoryContent.Content = null;
            await environment.GachaBannerCache.ClearAsync();
            PublishSuccess("gacha.cache.banners", "横幅缓存已清除", "可点击缓存全部横幅重新下载。");
            RenderHistoryDetail();
        }
        catch (Exception exception)
        {
            ReportFailure("gacha.cache.banners", "gacha.cache.banners", "清除横幅缓存失败",
                "请稍后重试。", exception);
        }
    }
}

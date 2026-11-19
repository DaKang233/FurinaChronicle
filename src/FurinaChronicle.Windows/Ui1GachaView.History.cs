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

    private void RenderHistory(GachaReadOnlySnapshot snapshot)
    {
        bool visible = snapshot.View == GachaReadOnlyView.History;
        HistoryPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        bool previousRendering = rendering;
        rendering = true;
        try
        {
            if (!visible || snapshot.IsBusy) automaticBannerCacheStarted = false;
            HistoryList.IsEnabled = !snapshot.IsBusy;
            RenderHistorySelection(visible ? snapshot.Rows : []);
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

    private void RenderHistorySelection(IReadOnlyList<GachaReadOnlyRow> rows)
    {
        bool previousRendering = rendering;
        rendering = true;
        try
        {
            GachaReadOnlyRow[] shown = rows.Where(row => row.History is not null).ToArray();
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

    private async Task StartBannerCacheAsync()
    {
        if (bannerCacheTask is not null || bannerPreloader is null) return;
        using var cancellation = new CancellationTokenSource();
        bannerCacheCancellation = cancellation;
        Task work = CacheBannersCoreAsync(cancellation.Token);
        bannerCacheTask = work;
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
                "部分横幅暂不可用；下次重新加载历史页时会自动重试。", exception);
        }
        finally
        {
            bannerCacheTask = null;
            bannerCacheCancellation = null;
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
            Artwork.InvalidateBanners();
            HistoryContent.Content = null;
            RenderHistoryDetail();
        }
    }

}

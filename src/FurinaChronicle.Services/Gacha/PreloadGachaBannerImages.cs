// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Services.Gacha;

public sealed class PreloadGachaBannerImages(
    IGachaEventCatalog eventCatalog,
    IGachaBannerImageCache bannerImageCache)
{
    public async Task<GachaBannerImagePreloadResult> ExecuteAsync(
        GachaGame game,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GachaEventPeriod> periods =
            await eventCatalog.GetAllAsync(game, cancellationToken);
        GachaEventBanner[] banners = periods
            .Where(period => IsLimitedPool(period.PoolGroup))
            .SelectMany(period => period.Banners)
            .DistinctBy(banner => banner.Id, StringComparer.Ordinal)
            .ToArray();
        int succeeded = 0;
        int failed = 0;
        await Parallel.ForEachAsync(
            banners,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = 4
            },
            async (banner, itemCancellationToken) =>
            {
                try
                {
                    string? path = await bannerImageCache.GetOrRefreshAsync(
                        game,
                        banner.Id,
                        banner.ImageUrl,
                        banner.BackupImageUrl,
                        itemCancellationToken);
                    if (path is null)
                    {
                        Interlocked.Increment(ref failed);
                    }
                    else
                    {
                        Interlocked.Increment(ref succeeded);
                    }
                }
                catch (OperationCanceledException) when (
                    itemCancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    Interlocked.Increment(ref failed);
                }
            });
        return new GachaBannerImagePreloadResult(
            banners.Length,
            succeeded,
            failed);
    }

    private static bool IsLimitedPool(GachaPoolGroup pool) =>
        pool is GachaPoolGroup.CharacterEvent or
            GachaPoolGroup.WeaponEvent or
            GachaPoolGroup.Chronicled;
}

public sealed record GachaBannerImagePreloadResult(
    int Total,
    int Succeeded,
    int Failed);

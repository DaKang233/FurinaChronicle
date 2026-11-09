// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Tests.Services.Gacha;

public sealed class PreloadGachaBannerImagesTests
{
    [Fact]
    public async Task ExecuteAsync_ContinuesWhenIndividualBannersFail()
    {
        GachaEventPeriod period = new(
            "period",
            GachaGame.GenshinImpact,
            "7.1",
            1,
            GachaPoolGroup.CharacterEvent,
            DateTimeOffset.Parse("2026-09-23T06:00:00+08:00"),
            DateTimeOffset.Parse("2026-10-13T17:59:00+08:00"),
            new HashSet<GameServerRegion> { GameServerRegion.ChinaOfficial },
            [
                Banner("success"),
                Banner("missing"),
                Banner("throws")
            ],
            "test",
            "revision");
        var service = new PreloadGachaBannerImages(
            new StubCatalog([
                period,
                period with
                {
                    Id = "standard-period",
                    PoolGroup = GachaPoolGroup.Standard,
                    Banners = [Banner("ignored-standard")]
                }
            ]),
            new StubCache());

        GachaBannerImagePreloadResult result = await service.ExecuteAsync(
            GachaGame.GenshinImpact);

        Assert.Equal(3, result.Total);
        Assert.Equal(1, result.Succeeded);
        Assert.Equal(2, result.Failed);
    }

    private static GachaEventBanner Banner(string id) => new(
        id,
        id,
        301,
        $"https://example.test/{id}.png",
        null,
        [],
        []);

    private sealed class StubCatalog(IReadOnlyList<GachaEventPeriod> periods)
        : IGachaEventCatalog
    {
        public ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
            GachaGame game,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(periods);
    }

    private sealed class StubCache : IGachaBannerImageCache
    {
        public Task<string?> GetCachedPathAsync(
            GachaGame game,
            string bannerId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> GetOrRefreshAsync(
            GachaGame game,
            string bannerId,
            string? sourceUrl,
            string? backupSourceUrl,
            CancellationToken cancellationToken = default)
        {
            return bannerId switch
            {
                "success" => Task.FromResult<string?>("cached.png"),
                "missing" => Task.FromResult<string?>(null),
                _ => throw new HttpRequestException("failed")
            };
        }

        public Task ClearAsync(
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}

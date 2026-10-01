// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class EmbeddedGachaEventCatalogTests
{
    [Fact]
    public async Task GetAllAsync_LoadsCompleteVersionedSnapMetadataSnapshot()
    {
        var catalog = new EmbeddedGachaEventCatalog();

        IReadOnlyList<GachaEventPeriod> periods =
            await catalog.GetAllAsync(GachaGame.GenshinImpact);

        Assert.Equal(218, periods.Count);
        Assert.Equal(298, periods.Sum(period => period.Banners.Count));
        Assert.Equal(
            new DateTimeOffset(2020, 9, 28, 6, 0, 0, TimeSpan.FromHours(8)),
            periods.Min(period => period.StartsAt));
        Assert.Equal(
            new DateTimeOffset(2026, 10, 13, 17, 59, 0, TimeSpan.FromHours(8)),
            periods.Max(period => period.EndsAt));
        Assert.All(periods, period =>
        {
            Assert.Equal("Snap.Metadata", period.Source);
            Assert.Equal(
                "b3aef3dbe0299e512654bb296930b3b6571fc87f",
                period.SourceRevision);
        });
        Assert.Equal(
            periods.Count,
            periods.Select(period => period.Id).Distinct().Count());
    }

    [Fact]
    public async Task GetAllAsync_CombinesConcurrentCharacterBanners()
    {
        var catalog = new EmbeddedGachaEventCatalog();
        IReadOnlyList<GachaEventPeriod> periods =
            await catalog.GetAllAsync(GachaGame.GenshinImpact);

        GachaEventPeriod period = Assert.Single(periods, period =>
            period.Version == "7.1" &&
            period.PoolGroup == GachaPoolGroup.CharacterEvent);

        Assert.Equal(2, period.Banners.Count);
        Assert.Equal([301, 400], period.Banners.Select(banner => banner.GachaType));
        Assert.Equal(
            ["煦风欢舞时", "涌浪叙歌"],
            period.Banners.Select(banner => banner.Name));
        Assert.True(period.Contains(
            new DateTimeOffset(
                2026,
                10,
                13,
                17,
                59,
                59,
                TimeSpan.FromHours(8))));
        Assert.False(period.Contains(
            new DateTimeOffset(
                2026,
                10,
                13,
                18,
                0,
                0,
                TimeSpan.FromHours(8))));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEmptyForOtherGames()
    {
        var catalog = new EmbeddedGachaEventCatalog();

        IReadOnlyList<GachaEventPeriod> periods =
            await catalog.GetAllAsync(GachaGame.HonkaiStarRail);

        Assert.Empty(periods);
    }
}

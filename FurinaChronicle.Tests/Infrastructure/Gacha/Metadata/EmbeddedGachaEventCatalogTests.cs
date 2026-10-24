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

        Assert.Equal(298, periods.Count);
        Assert.All(periods, period => Assert.Single(period.Banners));
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
    public async Task GetAllAsync_ReturnsConcurrentCharacterBannersAsIndependentPools()
    {
        var catalog = new EmbeddedGachaEventCatalog();
        IReadOnlyList<GachaEventPeriod> periods =
            await catalog.GetAllAsync(GachaGame.GenshinImpact);

        GachaEventPeriod[] matching = periods.Where(period =>
            period.Version == "7.1" &&
            period.PoolGroup == GachaPoolGroup.CharacterEvent)
            .ToArray();

        Assert.Equal(2, matching.Length);
        Assert.Equal(
            [301, 400],
            matching.Select(period => period.Banners[0].GachaType));
        Assert.Equal(
            ["煦风欢舞时", "涌浪叙歌"],
            matching.Select(period => period.Banners[0].Name));
        Assert.All(matching, period => Assert.True(period.Contains(
            new DateTimeOffset(
                2026,
                10,
                13,
                17,
                59,
                59,
                TimeSpan.FromHours(8)))));
        Assert.All(matching, period => Assert.False(period.Contains(
            new DateTimeOffset(
                2026,
                10,
                13,
                18,
                0,
                0,
                TimeSpan.FromHours(8)))));
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

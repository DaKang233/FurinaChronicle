// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaOverviewCardTests
{
    private static readonly Guid AccountId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-03T15:16:17+08:00");

    [Fact]
    public void TruePullsUsesLimitedHistoryAndRetainsIntervalsAboveNinety()
    {
        GachaPoolStatistics stats = Pool(
            [new(AccountId, "300", "限定 B", "2", null, Time, 75),
             new(AccountId, "200", "常驻角色", "3", null, Time.AddDays(-1), 75),
             new(AccountId, "100", "限定 A", "1", null, Time.AddDays(-2), null)],
            [new(AccountId, "300", "限定 B", "2", null, Time, 150),
             new(AccountId, "100", "限定 A", "1", null, Time.AddDays(-2), null)]);
        var card = new GachaOverviewCard(stats, false, _ => null);

        Assert.Equal(new[] { "限定 B", "常驻角色", "限定 A" }, card.VisibleItems.Select(item => item.Name));
        Assert.Equal("75 抽", card.VisibleItems[0].Subtitle);
        card.ToggleTruePullsCommand.Execute(null);
        Assert.Equal(new[] { "限定 B", "限定 A" }, card.VisibleItems.Select(item => item.Name));
        Assert.Equal("150 抽", card.VisibleItems[0].Subtitle);
        Assert.Equal(150, card.VisibleItems[0].IntervalPulls);
        Assert.Equal(180, card.VisibleItems[0].IntervalMaximum);
        Assert.Equal(150D / 180, card.VisibleItems[0].IntervalRatio, 8);
        Assert.Null(card.VisibleItems[1].IntervalPulls);
        Assert.Equal("间隔未知", card.VisibleItems[1].Subtitle);
        card.ToggleLayoutCommand.Execute(null);
        Assert.True(card.IsListMode);
        Assert.Equal("150 抽", card.VisibleItems[0].Subtitle);
        card.ToggleTruePulls();
        Assert.Equal(3, card.VisibleItems.Count);
    }

    [Fact]
    public void ArchiveItemsUseQuantityOrderAndRequireExplicitLoadMore()
    {
        var counts = Enumerable.Range(1, 29)
            .Select(index => new GachaPoolItemCount($"物品 {index}", index.ToString(), null, 5, 30 - index)).ToArray();
        var card = new GachaOverviewCard(Pool([], []) with { ItemCounts = counts }, true, _ => null);

        Assert.Equal(12, card.VisibleItems.Count);
        Assert.True(card.CanLoadMore);
        Assert.False(card.CanShowTruePulls);
        Assert.Equal("29", card.VisibleItems[0].CountText);
        card.ToggleTruePulls();
        card.ToggleLayout();
        Assert.True(card.IsGridMode);
        Assert.Equal(12, card.VisibleItems.Count);
        card.LoadMore();
        Assert.Equal(24, card.VisibleItems.Count);
        card.LoadMore();
        Assert.Equal(29, card.VisibleItems.Count);
        Assert.False(card.CanLoadMore);
        Assert.Equal(counts.Select(item => item.ItemName), card.VisibleItems.Select(item => item.Name));
    }

    [Theory]
    [InlineData(GachaPoolGroup.WeaponEvent)]
    [InlineData(GachaPoolGroup.Standard)]
    public void OtherPoolsKeepNormalFiveStarHistory(GachaPoolGroup group)
    {
        var normal = new FiveStarGacha(AccountId, "1", "物品", "2", null, Time, 60);
        var card = new GachaOverviewCard(Pool([normal], []) with { PoolGroup = group }, false, _ => null);
        Assert.False(card.CanShowTruePulls);
        card.ToggleTruePulls();
        Assert.Equal("60 抽", Assert.Single(card.VisibleItems).Subtitle);
    }

    [Theory]
    [InlineData(GachaPoolGroup.CharacterEvent, 90)]
    [InlineData(GachaPoolGroup.WeaponEvent, 80)]
    public void IntervalBarsKeepSourceCountsAndTheirPoolScale(GachaPoolGroup group, int scale)
    {
        var item = new FiveStarGacha(AccountId, "1", "合成", "10000089", null, Time, 70);
        var card = new GachaOverviewCard(Pool([item], []) with { PoolGroup = group }, false, _ => null);
        var row = Assert.Single(card.VisibleItems);
        Assert.Equal(70, row.IntervalPulls);
        Assert.Equal(scale, row.IntervalMaximum);
        Assert.Equal(70D / scale, row.IntervalRatio, 8);
    }

    [Fact]
    public void ViewportHeightOnlyAcceptsFinitePositiveHostMeasurements()
    {
        var card = new GachaOverviewCard(Pool([], []), false, _ => null);
        Assert.True(double.IsNaN(card.ViewportHeight));
        card.SetViewportHeight(600);
        card.SetViewportHeight(double.NaN);
        card.SetViewportHeight(0);
        Assert.Equal(600, card.ViewportHeight);
    }

    private static GachaPoolStatistics Pool(
        IReadOnlyList<FiveStarGacha> history, IReadOnlyList<LimitedFiveStarGacha> limited) =>
        new(GachaPoolGroup.CharacterEvent, "角色活动祈愿", 300, Time.AddDays(-2), Time,
            history.Count, 27, 270, 1, 9, 90, 75, 150, 75, 75, 0, 1, history, limited, []);
}

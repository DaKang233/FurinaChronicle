// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaItemPresentationTests
{
    [Theory]
    [InlineData(5, false, true, false)]
    [InlineData(4, false, false, true)]
    [InlineData(3, false, false, false)]
    [InlineData(0, false, false, false)]
    [InlineData(5, true, false, false)]
    [InlineData(4, true, false, false)]
    public void TimesRespectRankAndArchiveScope(int rank, bool archive, bool visible, bool expandable)
    {
        var times = Enumerable.Range(0, 151).Select(i => Time.AddSeconds(i)).ToArray();
        var card = Card(rank, archive, times);
        Assert.Equal(visible, card.IsTimesVisible);
        Assert.Equal(expandable, card.CanExpandTimes);
        Assert.Equal("151", card.Icon.CountText);
        if (expandable) card.ToggleTimes();
        if (visible || expandable)
        {
            Assert.Equal(50, card.TimesText.Split('\n').Length);
            Assert.StartsWith(times[^1].ToString("yyyy-MM-dd HH:mm:ss zzz"), card.TimesText);
            Assert.Contains("+08:00", card.TimesText);
            Assert.True(card.CanLoadMoreTimes);
            card.LoadMoreTimes();
            Assert.Equal(100, card.TimesText.Split('\n').Length);
            card.LoadMoreTimes();
            card.LoadMoreTimes();
            Assert.Equal(151, card.TimesText.Split('\n').Length);
            Assert.False(card.CanLoadMoreTimes);
        }
        else
        {
            card.ToggleTimes();
            card.LoadMoreTimes();
            Assert.False(card.IsTimesVisible);
            Assert.Empty(card.TimesText);
            Assert.Empty(card.TimeCountText);
            Assert.False(card.CanLoadMoreTimes);
        }
    }

    [Fact]
    public void FourStarCollapseResetsItsBoundedHistoryAndNotifiesBindings()
    {
        var card = Card(4, false, Enumerable.Range(0, 120).Select(i => Time.AddSeconds(i)).ToArray());
        int notifications = 0;
        card.PropertyChanged += (_, _) => notifications++;
        card.ToggleTimes();
        card.LoadMoreTimes();
        Assert.Equal(100, card.TimesText.Split('\n').Length);
        card.ToggleTimes();
        Assert.Empty(card.TimesText);
        card.ToggleTimes();
        Assert.Equal(50, card.TimesText.Split('\n').Length);
        Assert.Equal(4, notifications);
    }

    [Fact]
    public void RarityGroupsKeepAnalyticsOrderWithinEachGroupAndUnknownLast()
    {
        var four = Card(4, false, []).Icon with { Name = "高计数四星" };
        var moreFour = four with { Name = "低计数四星" };
        var groups = GachaItemGroup.Build([four, Card(0, false, []).Icon with { Rarity = null },
            Card(3, false, []).Icon, moreFour, Card(5, false, []).Icon]);
        Assert.Equal(new[] { "五星", "四星", "三星", "星级未知" }, groups.Select(group => group.Title));
        Assert.Equal(new[] { "高计数四星", "低计数四星" }, groups[1].Items.Select(row => row.Name));
    }

    [Fact]
    public void CalendarPreservesHighRankCountsAndHeatAndFillsOnlyTheSelectedMonth()
    {
        var day = new DateOnly(2026, 2, 3);
        GachaReadOnlyRow row = Card(5, false, []).Icon with
        {
            CalendarDay = day, CalendarPulls = 41, CalendarFiveStarCount = 2,
            CalendarFourStarCount = 5, CalendarIntensity = 4,
        };
        var cells = GachaCalendarCell.Build(new(2026, 2, 1), [row, row with { CalendarDay = day.AddMonths(1) }]).ToArray();
        Assert.Equal(28, cells.Length);
        Assert.Equal(6, cells[0].Column); // 2026-02-01 is Sunday; the grid begins Monday.
        Assert.Equal(0, cells[0].Week);
        var selected = Assert.Single(cells, cell => cell.Date == day);
        Assert.Equal(41, selected.Pulls);
        Assert.Equal(4, selected.IntensityLevel);
        Assert.Equal("五星 2 · 四星 5", selected.HighRanks);
        Assert.All(cells.Where(cell => cell.Date != day), cell =>
        {
            Assert.Equal(0, cell.Pulls);
            Assert.Equal(0, cell.IntensityLevel);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualItemsSourceAggregatesSelectedCopiesAndSuppressesArchiveTimes(bool archiveScope)
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(context.Archives).ExecuteAsync("合成物品档案");
        var add = new AddGameAccount(context.Archives, context.Accounts);
        var a = await add.ExecuteAsync(archive.Id, "100000001", GameServerRegion.ChinaOfficial, "合成甲");
        var b = await add.ExecuteAsync(archive.Id, "100000002", GameServerRegion.ChinaOfficial, "合成乙");
        GachaRecord[] records =
        [
            Record(a.Id, "1", "10000089", 5), Record(a.Id, "2", "10000089", 5),
            Record(a.Id, "3", "10000016", 4), Record(a.Id, "4", "11101", 3),
            Record(b.Id, "1", "10000089", 5), Record(b.Id, "2", "10000016", 4),
        ];
        var result = await new CommitGachaRecords(context.AtomicGacha).ExecuteAsync(new(
            OperationId.New(), DataChangeOperationKind.Import, DataOrigin.UserEntered, "合成输入",
            Time, Time, records, GachaRecordConflictPolicy.PreserveExisting, true, CaptureUndo: false));
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        var source = new ServiceGachaReadOnlySource(
            new GetPlayerArchives(context.Archives), new GetGameAccounts(context.Archives, context.Accounts),
            new GetGachaRecordPage(context.Gacha), context.Database.InitializeAsync, _ => null,
            new BuildGachaAnalytics(context.Gacha, new EmptyGachaItemMetadataProvider()));
        var page = await source.GetAnalysisAsync(new GachaRecordQuery(archiveScope ? [a.Id, b.Id] : [a.Id]),
            GachaReadOnlyView.Items, [new(a.Id, "合成甲"), new(b.Id, "合成乙")],
            1, 25, new(null, true), archiveScope, CancellationToken.None);
        Assert.Equal(3, page.TotalCount);
        var five = Assert.Single(page.Rows, row => row.Rarity == 5);
        Assert.Equal(archiveScope ? "3" : "2", five.CountText);
        Assert.Equal(!archiveScope, five.Item!.IsTimesVisible);
        Assert.Equal(archiveScope, five.Item.IsArchiveMode);
        Assert.Equal(archiveScope ? 0 : 2, string.IsNullOrEmpty(five.Item.TimesText) ? 0 : five.Item.TimesText.Split('\n').Length);
        Assert.DoesNotContain("2026-", await source.GetDetailAsync(five, CancellationToken.None));
        Assert.Empty(Assert.Single(page.Rows, row => row.Rarity == 3).Item!.TimesText);
    }

    private static readonly DateTimeOffset Time = DateTimeOffset.Parse("2026-10-01T12:00:00+08:00");

    private static GachaItemCard Card(int rank, bool archive, IReadOnlyList<DateTimeOffset> times) =>
        new(new("id:10000089", "合成物品", "10000089", "角色", null, rank, times.Count, times), archive, _ => null);

    private static GachaRecord Record(Guid account, string id, string itemId, int rank) =>
        new(account, id, "合成物品", rank, Time) { ItemId = itemId, ItemType = rank == 3 ? "武器" : "角色", GachaType = "301" };
}

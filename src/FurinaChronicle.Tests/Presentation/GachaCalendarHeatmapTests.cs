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

public sealed class GachaCalendarHeatmapTests
{
    [Theory]
    [InlineData(2026, 365, 53, 3)]
    [InlineData(2024, 366, 53, 0)]
    [InlineData(2012, 366, 54, 6)]
    public void YearContainsEveryDayExactlyOnceInWeekColumns(int year, int count, int weeks, int firstWeekday)
    {
        var cells = GachaCalendarCell.BuildYear(year, []).ToArray();
        Assert.Equal(count, cells.Length);
        Assert.Equal(count, cells.Select(cell => (cell.Week, cell.Column)).Distinct().Count());
        Assert.Equal(weeks, cells[^1].Week + 1);
        Assert.Equal(firstWeekday, cells[0].Column);
        Assert.Equal(new DateOnly(year, 1, 1), cells[0].Date);
        Assert.Equal(new DateOnly(year, 12, 31), cells[^1].Date);
        Assert.All(cells, cell => Assert.InRange(cell.Column, 0, 6));
        for (int index = 1; index < cells.Length; index++)
        {
            Assert.Equal(cells[index - 1].Date.AddDays(1), cells[index].Date);
            if (cells[index].Column == 0) Assert.Equal(cells[index - 1].Week + 1, cells[index].Week);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void YearBoundsPreserveDisplayZoneAndCoverTheEntireYear(bool chinaTime)
    {
        var filter = new GachaCalendarFilter(new(2026, 6, 17), chinaTime, YearView: true);
        TimeSpan offset = chinaTime ? TimeSpan.FromHours(8) : TimeSpan.Zero;
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, offset), filter.Start);
        Assert.Equal(new DateTimeOffset(2027, 1, 1, 0, 0, 0, offset).AddTicks(-1), filter.End);
        var month = filter with { YearView = false };
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, offset), month.Start);
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 0, 0, 0, offset).AddTicks(-1), month.End);
    }

    [Fact]
    public async Task ActualSourceSupportsYearChangesArchiveCountsTimeZonesAndDayDrilldown()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(context.Archives).ExecuteAsync("合成热力图");
        var add = new AddGameAccount(context.Archives, context.Accounts);
        var a = await add.ExecuteAsync(archive.Id, "100000001", GameServerRegion.ChinaOfficial, "合成甲");
        var b = await add.ExecuteAsync(archive.Id, "100000002", GameServerRegion.ChinaOfficial, "合成乙");
        GachaRecord[] records =
        [
            Record(a.Id, "1", "2025-12-31T16:30:00Z", 5),
            Record(a.Id, "2", "2026-12-31T16:30:00Z", 5),
            Record(a.Id, "3", "2026-06-01T00:00:00Z", 4),
            Record(b.Id, "1", "2026-06-01T00:00:00Z", 5),
        ];
        DateTimeOffset now = DateTimeOffset.Parse("2026-11-01T00:00:00Z");
        var result = await new CommitGachaRecords(context.AtomicGacha).ExecuteAsync(new(
            OperationId.New(), DataChangeOperationKind.Import, DataOrigin.UserEntered, "合成输入",
            now, now, records, GachaRecordConflictPolicy.PreserveExisting, true, CaptureUndo: false));
        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        var source = new ServiceGachaReadOnlySource(
            new GetPlayerArchives(context.Archives), new GetGameAccounts(context.Archives, context.Accounts),
            new GetGachaRecordPage(context.Gacha), context.Database.InitializeAsync, _ => null,
            new BuildGachaAnalytics(context.Gacha, new EmptyGachaItemMetadataProvider()));
        var state = new GachaReadOnlyState(source, new Dispatcher(), GachaReadOnlyView.Calendar,
            new(new(2026, 1, 1), true, YearView: true));
        await state.EnterAsync();
        await state.SelectAccountAsync(a.Id);
        Assert.Equal(2, state.Snapshot.Rows.Sum(row => row.CalendarPulls));
        Assert.Equal(new DateOnly(2026, 1, 1), state.Snapshot.VisibleMonth);
        Assert.Contains(state.Snapshot.Rows, row => row.CalendarDay == new DateOnly(2026, 1, 1) && row.CalendarFiveStarCount == 1);
        await state.ShiftCalendarAsync(1);
        Assert.Equal(new DateOnly(2027, 1, 1), state.Snapshot.VisibleMonth);
        Assert.Single(state.Snapshot.Rows);
        await state.ShiftCalendarAsync(-1);
        await state.SelectArchiveAccountsAsync([a.Id, b.Id]);
        Assert.Equal(3, state.Snapshot.Rows.Sum(row => row.CalendarPulls));
        var june = Assert.Single(state.Snapshot.Rows, row => row.CalendarDay == new DateOnly(2026, 6, 1));
        Assert.Equal(2, june.CalendarPulls);
        Assert.Equal(1, june.CalendarFiveStarCount);
        Assert.Equal(1, june.CalendarFourStarCount);
        Assert.Equal(5, june.CalendarIntensity);
        await state.ApplyCalendarAsync(new(new(2026, 1, 1), false, YearView: true));
        Assert.DoesNotContain(state.Snapshot.Rows, row => row.CalendarDay == new DateOnly(2026, 1, 1));
        Assert.Contains(state.Snapshot.Rows, row => row.CalendarDay == new DateOnly(2026, 12, 31));
        await state.OpenCalendarDayAsync(new(2026, 12, 31));
        Assert.Equal(GachaReadOnlyView.Records, state.Snapshot.View);
        Assert.Equal("2", Assert.Single(state.Snapshot.Rows).ExternalRecordId);
        Assert.Equal(TimeSpan.Zero, state.Snapshot.Filter.DayOffset);
        Assert.Equal(1, state.Snapshot.TotalCount);
    }

    [Fact]
    public void YearScalingIgnoresOtherYearsAndFillsMissingDays()
    {
        GachaCalendarDay[] days =
        [
            new(new(2025, 12, 31), 10000, 100, 100, 5),
            new(new(2026, 1, 2), 10, 1, 2, 1),
            new(new(2026, 1, 5), 2, 0, 1, 5),
        ];
        var map = GachaCalendarHeatmap.FromDailyCounts(2026, days);
        Assert.Equal(365, map.Days.Count);
        Assert.Equal(12, map.TotalPulls);
        Assert.Equal(2, map.ActiveDays);
        Assert.Equal(53, map.Weeks);
        Assert.Equal(5, map.Days.Single(day => day.Date == new DateOnly(2026, 1, 2)).IntensityLevel);
        Assert.Equal(1, map.Days.Single(day => day.Date == new DateOnly(2026, 1, 5)).IntensityLevel);
        Assert.Equal(0, map.Days[0].Pulls);
        Assert.Equal(0, map.Days[0].IntensityLevel);
        Assert.Equal(0, map.Days[^1].Pulls);
        var jan2 = map.Days.Single(day => day.Date == new DateOnly(2026, 1, 2));
        Assert.Equal(1, jan2.FiveStarCount);
        Assert.Equal(2, jan2.FourStarCount);
    }

    [Fact]
    public void EmptyYearAndThemeColorsRemainDeterministic()
    {
        var map = GachaCalendarHeatmap.FromDailyCounts(2024, []);
        Assert.Equal(366, map.Days.Count);
        Assert.Equal(0, map.TotalPulls);
        Assert.Equal(0, map.ActiveDays);
        Assert.All(map.Days, day => Assert.Equal(0, day.IntensityLevel));
        Assert.NotEqual(GachaCalendarHeatmap.ColorHex(0, true), GachaCalendarHeatmap.ColorHex(0, false));
        for (int level = 1; level <= 5; level++)
            Assert.Equal(GachaCalendarHeatmap.ColorHex(level, true), GachaCalendarHeatmap.ColorHex(level, false));
    }

    private static GachaRecord Record(Guid account, string id, string time, int rank) =>
        new(account, id, "合成物品", rank, DateTimeOffset.Parse(time))
        {
            ItemId = rank == 5 ? "10000089" : "10000016", ItemType = "角色", GachaType = "301",
        };

    private sealed class Dispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }
}

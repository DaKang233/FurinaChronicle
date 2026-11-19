// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Presentation;

public sealed class GachaHistoryPresentationTests
{
    [Fact]
    public async Task HistoryShowsIndependentZeroPullPeriodsAndMatchesFeaturedItemsById()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var archive = await new CreatePlayerArchive(context.Archives).ExecuteAsync("fixture");
        var account = await new AddGameAccount(context.Archives, context.Accounts)
            .ExecuteAsync(archive.Id, "100000001", GameServerRegion.ChinaOfficial, "fixture");
        DateTimeOffset time = DateTimeOffset.Parse("2020-01-10T12:00:00+08:00");
        var first = Period("first", "10000089", 301);
        var second = Period("second", "10000087", 400);
        var catalog = new Catalog([
            first, second,
            first with { Id = "future", StartsAt = time.AddYears(900), EndsAt = time.AddYears(901) },
            first with { Id = "standard", PoolGroup = GachaPoolGroup.Standard },
            first with { Id = "other-region", ServerRegions = new HashSet<GameServerRegion>() }
        ]);
        GachaRecord[] records = [
            new(account.Id, "1", "同名物品", 5, time) { ItemId = "10000089", GachaType = "301" },
            new(account.Id, "2", "同名物品", 5, time.AddSeconds(1)) { ItemId = "99999999", GachaType = "301" }
        ];
        var committed = await new CommitGachaRecords(context.AtomicGacha).ExecuteAsync(new(
            OperationId.New(), DataChangeOperationKind.Import, DataOrigin.UserEntered, "fixture",
            time, time, records, GachaRecordConflictPolicy.PreserveExisting, true, CaptureUndo: false));
        Assert.Equal(ChangeExecutionStatus.Applied, committed.Status);
        var source = new ServiceGachaReadOnlySource(
            new GetPlayerArchives(context.Archives),
            new GetGameAccounts(context.Archives, context.Accounts),
            new GetGachaRecordPage(context.Gacha),
            context.Database.InitializeAsync,
            _ => null,
            new BuildGachaAnalytics(context.Gacha, new EmptyGachaItemMetadataProvider(), catalog),
            eventCatalog: catalog);
        GachaReadOnlyPage page = await source.GetAnalysisAsync(
            new GachaRecordQuery([account.Id]), GachaReadOnlyView.History,
            [new GachaScopeOption(account.Id, "fixture") { Region = GameServerRegion.ChinaOfficial }],
            1, 25, new(null, true), false, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        var firstCard = Assert.Single(page.Rows, row => row.History?.Id == "first").History!;
        Assert.Equal("2 抽", firstCard.Total);
        Assert.Equal("1", Assert.Single(firstCard.FeaturedItems).CountText);
        Assert.Equal(2, firstCard.VisibleItems.Count);
        var secondCard = Assert.Single(page.Rows, row => row.History?.Id == "second").History!;
        Assert.Equal("0 抽", secondCard.Total);
        Assert.Equal("0", Assert.Single(secondCard.FeaturedItems).CountText);
        Assert.Empty(secondCard.VisibleItems);
        var unpaged = await source.GetAnalysisAsync(
            new GachaRecordQuery([account.Id]), GachaReadOnlyView.History,
            [new GachaScopeOption(account.Id, "fixture") { Region = GameServerRegion.ChinaOfficial }],
            99, 1, new(null, true), false, CancellationToken.None);
        Assert.Equal(2, unpaged.Rows.Count);
        Assert.Equal(1, unpaged.PageNumber);
        Assert.Equal(1, unpaged.TotalPages);
        var state = new GachaReadOnlyState(source, new Dispatcher(), GachaReadOnlyView.History);
        await state.EnterAsync();
        var snapshot = state.Snapshot;
        await state.ChangePageAsync(2);
        Assert.Same(snapshot, state.Snapshot);
        Assert.Equal(2, state.Snapshot.Rows.Count);
    }

    private sealed class Dispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
    }

    [Fact]
    public void DateRangePreservesCalendarOffsetAndRejectsReverseBounds()
    {
        var day = new DateOnly(2026, 10, 1);
        var calendarDay = new GachaReadOnlyFilter(Day: day, DayOffset: TimeSpan.FromHours(8));
        var sameRange = new GachaReadOnlyFilter(StartDay: day, EndDay: day, RangeOffset: TimeSpan.FromHours(8));
        Assert.Equal(calendarDay.StartTime, sameRange.StartTime);
        Assert.Equal(calendarDay.EndTime, sameRange.EndTime);
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T16:00:00Z"), sameRange.StartTime);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T16:00:00Z").AddTicks(-1), sameRange.EndTime);
        Assert.Throws<ArgumentException>(() =>
            new GachaReadOnlyFilter(StartDay: day.AddDays(1), EndDay: day).Validate());
    }

    private static GachaEventPeriod Period(string id, string upId, int type) =>
        new(id, GachaGame.GenshinImpact, "1.0", 1, GachaPoolGroup.CharacterEvent,
            DateTimeOffset.Parse("2020-01-01T00:00:00+08:00"),
            DateTimeOffset.Parse("2020-01-31T23:59:59+08:00"),
            new HashSet<GameServerRegion> { GameServerRegion.ChinaOfficial },
            [new GachaEventBanner(id, id, type, null, null, [upId], [])],
            "fixture", "fixture");

    private sealed class Catalog(IReadOnlyList<GachaEventPeriod> periods) : IGachaEventCatalog
    {
        public ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
            GachaGame game, CancellationToken cancellationToken = default) => ValueTask.FromResult(periods);
    }
}

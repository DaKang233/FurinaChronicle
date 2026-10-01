// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Tests.Services.Gacha.Analytics;

public sealed class BuildGachaAnalyticsTests
{
    [Fact]
    public async Task ExecuteAsync_BuildsPerAccountPityAndAggregateViews()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        DateTimeOffset day =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        GachaRecord[] records =
        [
            Record(first, "1", "三星武器", "weapon-3", 3, day, "301"),
            Record(first, "2", "四星角色", "avatar-4", 4, day.AddMinutes(1), "301"),
            Record(first, "3", "五星甲", "avatar-5a", 5, day.AddMinutes(2), "301"),
            Record(first, "4", "三星武器", "weapon-3", 3, day.AddDays(1), "400"),
            Record(second, "5", "五星乙", "avatar-5b", 5, day.AddMinutes(3), "400"),
            Record(second, "6", "四星角色", "avatar-4", 4, day.AddDays(1), "301"),
            Record(second, "7", "三星武器", "weapon-3", 3, day.AddDays(1).AddMinutes(1), "301"),
            Record(second, "8", "五星武器", "weapon-5", 5, day.AddDays(1).AddMinutes(2), "302")
        ];
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(records),
            new StubMetadataProvider());
        var query = new GachaRecordQuery([first, second]);

        GachaAnalyticsReport report = await service.ExecuteAsync(query);

        Assert.Equal(8, report.TotalPulls);
        GachaPoolStatistics character = Assert.Single(
            report.Pools,
            pool => pool.PoolGroup == GachaPoolGroup.CharacterEvent);
        Assert.Equal(7, character.TotalPulls);
        Assert.Equal(2, character.FiveStarCount);
        Assert.Null(character.AverageFiveStarPulls);
        Assert.Null(character.AverageUpFiveStarPulls);
        Assert.Null(character.MinimumFiveStarPulls);
        Assert.Null(character.MaximumFiveStarPulls);
        Assert.Equal(3, character.PullsSinceLastFiveStar);
        Assert.Equal(3, character.PullsSinceLastFourStar);
        Assert.Equal(["五星乙", "五星甲"], character.FiveStarHistory.Select(item => item.ItemName));
        Assert.All(character.FiveStarHistory, item => Assert.NotNull(item.IconUrl));
        GachaPoolItemCount threeStarCount = Assert.Single(
            character.ItemCounts,
            item => item.ItemId == "weapon-3");
        Assert.Equal(3, threeStarCount.Count);

        Assert.Equal(3, report.History.Count);
        Assert.Equal(2, report.Calendar.Count);
        Assert.Equal(5, report.Calendar.Max(dayItem => dayItem.IntensityLevel));
        GachaItemStatistics threeStar = Assert.Single(
            report.Items,
            item => item.ItemId == "weapon-3");
        Assert.Equal(3, threeStar.Count);
        Assert.Equal(3, threeStar.AcquisitionTimes.Count);
    }

    [Fact]
    public async Task ExecuteAsync_FiveStarHistory_MatchesNewestFirstDetailOrder()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset time =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        GachaRecord[] records =
        [
            Record(accountId, "100", "五星甲", "avatar-a", 5, time, "301"),
            Record(accountId, "102", "五星丙", "avatar-c", 5, time, "301"),
            Record(accountId, "101", "五星乙", "avatar-b", 5, time, "301")
        ];
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(records),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.Pools);

        GachaPoolStatistics pool = Assert.Single(report.Pools);
        Assert.Equal(
            ["102", "101", "100"],
            pool.FiveStarHistory.Select(item => item.ExternalRecordId));
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesCompletedFiveStarIntervals()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset start =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        GachaRecord[] records =
        [
            Record(accountId, "1", "限定五星甲", "10000089", 5, start, "301"),
            Record(accountId, "2", "三星武器", "weapon-3", 3, start.AddMinutes(1), "301"),
            Record(accountId, "3", "迪卢克", "10000016", 5, start.AddMinutes(2), "301"),
            Record(accountId, "4", "三星武器", "weapon-3", 3, start.AddMinutes(3), "400"),
            Record(accountId, "5", "四星角色", "avatar-4", 4, start.AddMinutes(4), "400"),
            Record(accountId, "6", "限定五星乙", "10000087", 5, start.AddMinutes(5), "400"),
            Record(accountId, "7", "三星武器", "weapon-3", 3, start.AddMinutes(6), "301"),
            Record(accountId, "8", "限定五星丙", "10000078", 5, start.AddMinutes(7), "301")
        ];
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(records),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.Pools);

        GachaPoolStatistics character = Assert.Single(report.Pools);
        Assert.NotNull(character.AverageFiveStarPulls);
        Assert.Equal(
            7D / 3D,
            character.AverageFiveStarPulls.Value,
            precision: 6);
        Assert.Equal(3.5D, character.AverageUpFiveStarPulls);
        Assert.Equal(2, character.MinimumFiveStarPulls);
        Assert.Equal(3, character.MaximumFiveStarPulls);
        Assert.Empty(report.History);
        Assert.Empty(report.Calendar);
        Assert.Empty(report.Items);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesWeaponLimitedFiveStarIntervals()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset start =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        GachaRecord[] records =
        [
            Record(accountId, "1", "限定五星武器甲", "15514", 5, start, "302"),
            Record(accountId, "2", "三星武器", "weapon-3", 3, start.AddMinutes(1), "302"),
            Record(accountId, "3", "天空之翼", "15501", 5, start.AddMinutes(2), "302"),
            Record(accountId, "4", "三星武器", "weapon-3", 3, start.AddMinutes(3), "302"),
            Record(accountId, "5", "限定五星武器乙", "15512", 5, start.AddMinutes(4), "302")
        ];
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(records),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.Pools);

        GachaPoolStatistics weapon = Assert.Single(report.Pools);
        Assert.Equal(2D, weapon.AverageFiveStarPulls);
        Assert.Equal(4D, weapon.AverageUpFiveStarPulls);
    }

    [Fact]
    public async Task ExecuteAsync_RequestedComponentOnlyBuildsThatProjection()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset time =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(
            [
                Record(accountId, "1", "五星甲", "10000089", 5, time, "301")
            ]),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.Calendar);

        Assert.Empty(report.Pools);
        Assert.Empty(report.History);
        Assert.Single(report.Calendar);
        Assert.Empty(report.Items);
        Assert.Equal(1, report.TotalPulls);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAndPresentItemIdForSameNameAreMerged()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset start =
            new(2026, 1, 1, 8, 0, 0, TimeSpan.FromHours(8));
        GachaRecord withId = Record(
            accountId,
            "1",
            "三星武器",
            "weapon-3",
            3,
            start,
            "301");
        GachaRecord withoutId = Record(
            accountId,
            "2",
            "三星武器",
            "temporary",
            3,
            start.AddMinutes(1),
            "301") with
        {
            ItemId = null
        };
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository([withId, withoutId]),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]));

        GachaItemStatistics item = Assert.Single(report.Items);
        Assert.Equal("weapon-3", item.ItemId);
        Assert.Equal(2, item.Count);
        Assert.NotNull(item.IconUrl);
        GachaPoolItemCount poolItem = Assert.Single(
            Assert.Single(report.Pools).ItemCounts);
        Assert.Equal("weapon-3", poolItem.ItemId);
        Assert.Equal(2, poolItem.Count);
    }

    [Fact]
    public async Task ExecuteAsync_HistoryGroupsRecordsByRealEventPeriod()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset startsAt =
            new(2026, 1, 1, 6, 0, 0, TimeSpan.FromHours(8));
        var eventPeriod = new GachaEventPeriod(
            "event-period",
            GachaGame.GenshinImpact,
            "6.0",
            1,
            GachaPoolGroup.CharacterEvent,
            startsAt,
            startsAt.AddDays(20),
            new HashSet<GameServerRegion>
            {
                GameServerRegion.ChinaOfficial,
                GameServerRegion.ChinaBilibili
            },
            [
                new GachaEventBanner(
                    "event-banner-301",
                    "测试祈愿一",
                    301,
                    null,
                    null,
                    ["10000001"],
                    ["10000002"]),
                new GachaEventBanner(
                    "event-banner-400",
                    "测试祈愿二",
                    400,
                    null,
                    null,
                    ["10000003"],
                    ["10000002"])
            ],
            "Test",
            "revision");
        GachaRecord[] records =
        [
            Record(
                accountId,
                "1",
                "三星武器",
                "weapon-3",
                3,
                startsAt.AddDays(1),
                "301"),
            Record(
                accountId,
                "2",
                "五星甲",
                "avatar-5",
                5,
                startsAt.AddDays(10),
                "400")
        ];
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(records),
            new StubMetadataProvider(),
            new StubEventCatalog([eventPeriod]));

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.History,
            new Dictionary<Guid, GameServerRegion>
            {
                [accountId] = GameServerRegion.ChinaOfficial
            });

        GachaHistoryPeriod history = Assert.Single(report.History);
        Assert.Equal(eventPeriod, history.EventPeriod);
        Assert.Equal(GachaEventMatchQuality.Verified, history.MatchQuality);
        Assert.Equal(2, history.TotalPulls);
        Assert.Equal(startsAt, history.StartTime);
        Assert.Equal(startsAt.AddDays(20), history.EndTime);
    }

    [Fact]
    public async Task ExecuteAsync_HistoryDoesNotApplyChinaEventsToGlobalAccount()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset startsAt =
            new(2026, 1, 1, 6, 0, 0, TimeSpan.FromHours(8));
        var eventPeriod = new GachaEventPeriod(
            "event-period",
            GachaGame.GenshinImpact,
            "6.0",
            1,
            GachaPoolGroup.CharacterEvent,
            startsAt,
            startsAt.AddDays(20),
            new HashSet<GameServerRegion>
            {
                GameServerRegion.ChinaOfficial,
                GameServerRegion.ChinaBilibili
            },
            [new GachaEventBanner(
                "event-banner",
                "测试祈愿",
                301,
                null,
                null,
                [],
                [])],
            "Test",
            "revision");
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository(
            [
                Record(
                    accountId,
                    "1",
                    "三星武器",
                    "weapon-3",
                    3,
                    startsAt.AddDays(1),
                    "301")
            ]),
            new StubMetadataProvider(),
            new StubEventCatalog([eventPeriod]));

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]),
            GachaAnalyticsComponents.History,
            new Dictionary<Guid, GameServerRegion>
            {
                [accountId] = GameServerRegion.Europe
            });

        GachaHistoryPeriod history = Assert.Single(report.History);
        Assert.Null(history.EventPeriod);
        Assert.Equal(GachaEventMatchQuality.Unmatched, history.MatchQuality);
    }

    [Fact]
    public async Task ExecuteAsync_EmptyScopeReturnsEmptyReport()
    {
        Guid accountId = Guid.NewGuid();
        var service = new BuildGachaAnalytics(
            new InMemoryGachaRecordRepository([]),
            new StubMetadataProvider());

        GachaAnalyticsReport report = await service.ExecuteAsync(
            new GachaRecordQuery([accountId]));

        Assert.Equal(0, report.TotalPulls);
        Assert.Empty(report.Pools);
        Assert.Empty(report.History);
        Assert.Empty(report.Calendar);
        Assert.Empty(report.Items);
    }

    private static GachaRecord Record(
        Guid accountId,
        string externalId,
        string name,
        string itemId,
        int rank,
        DateTimeOffset time,
        string type)
    {
        return new GachaRecord(accountId, externalId, name, rank, time)
        {
            ItemId = itemId,
            UigfGachaType = type
        };
    }

    private sealed class StubMetadataProvider : IGachaItemMetadataProvider
    {
        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<GachaItemMetadata?>(
                new(
                    game,
                    itemId,
                    itemId,
                    itemId.StartsWith("avatar", StringComparison.Ordinal)
                        ? "Avatar"
                        : "Weapon",
                    null,
                    $"https://example.test/{itemId}.png"));
        }

        public ValueTask<GachaItemMetadata?> FindByNameAsync(
            GachaGame game,
            string itemName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaItemMetadata? result = itemName == "三星武器"
                ? new(
                    game,
                    "weapon-3",
                    itemName,
                    "Weapon",
                    3,
                    "https://example.test/weapon-3.png")
                : null;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class StubEventCatalog(
        IReadOnlyList<GachaEventPeriod> periods) : IGachaEventCatalog
    {
        public ValueTask<IReadOnlyList<GachaEventPeriod>> GetAllAsync(
            GachaGame game,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(periods);
        }
    }
}

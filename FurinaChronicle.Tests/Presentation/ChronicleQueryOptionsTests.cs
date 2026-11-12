// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChronicleQueryOptionsTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Last30Days_UsesHalfOpenFromBound()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        ChronicleTimeRange range = ChronicleTimeRangeOptions.Resolve(
            ChronicleTimeRangePreset.Last30Days,
            now,
            Utc);

        Assert.Equal(now.AddDays(-30), range.From);
        Assert.Null(range.To);
        Assert.False(range.IsUnbounded);
    }

    [Fact]
    public void AllRange_IsUnbounded()
    {
        ChronicleTimeRange range = ChronicleTimeRangeOptions.Resolve(
            ChronicleTimeRangePreset.All,
            DateTimeOffset.UtcNow,
            Utc);

        Assert.True(range.IsUnbounded);
    }

    [Fact]
    public void ThisYear_UsesLocalYearBoundariesConvertedToUtc()
    {
        TimeZoneInfo zone =
            TimeZoneInfo.CreateCustomTimeZone("test+8", TimeSpan.FromHours(8), "test+8", "test+8");
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.FromHours(8));

        ChronicleTimeRange range = ChronicleTimeRangeOptions.Resolve(
            ChronicleTimeRangePreset.ThisYear,
            now,
            zone);

        Assert.Equal(
            new DateTimeOffset(2025, 12, 31, 16, 0, 0, TimeSpan.Zero),
            range.From);
        Assert.Equal(
            new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero),
            range.To);
    }

    [Fact]
    public void FilterBuilder_CarriesDomainRankAndUndatedSelection()
    {
        var range = new ChronicleTimeRange(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));

        ChronicleFilter filter = ChronicleFilterOptions.Build(
            range,
            includeUndated: true,
            domain: ChronicleDomain.Challenge,
            rank: 5);

        Assert.Equal(range.From, filter.From);
        Assert.Equal(range.To, filter.To);
        Assert.True(filter.IncludeUndated);
        Assert.Equal(
            ChronicleDomain.Challenge,
            Assert.Single(filter.Domains!));
        Assert.Equal(5, Assert.Single(filter.Ranks!));
        filter.Validate();
    }

    [Fact]
    public void FilterBuilder_WithoutOptionalSelectorsLeavesThemUnset()
    {
        ChronicleFilter filter = ChronicleFilterOptions.Build(
            new ChronicleTimeRange(),
            includeUndated: false);

        Assert.Null(filter.Domains);
        Assert.Null(filter.Ranks);
        Assert.Null(filter.From);
        Assert.Null(filter.To);
        Assert.False(filter.IncludeUndated);
    }

    [Fact]
    public void DisplayOrder_PutsUndatedLastAndSortsTimedDescending()
    {
        ChronicleEntry older = ChronicleTestEntries.Create(
            seed: "older",
            time: ChronicleTestEntries.Instant(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        ChronicleEntry newer = ChronicleTestEntries.Create(
            seed: "newer",
            time: ChronicleTestEntries.Instant(
                new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        ChronicleEntry undated = ChronicleTestEntries.Create(
            seed: "undated",
            time: ChronicleTestEntries.Undated());

        IReadOnlyList<ChronicleEntry> sorted =
            ChronicleDisplayOrder.Sort([undated, older, newer]);

        Assert.Same(newer, sorted[0]);
        Assert.Same(older, sorted[1]);
        Assert.Same(undated, sorted[2]);
    }

    [Fact]
    public void DisplayOrder_TiesBreakByEntryIdDescending()
    {
        DateTimeOffset same =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        ChronicleEntry first = ChronicleTestEntries.Create(
            seed: "tie-a",
            time: ChronicleTestEntries.Instant(same));
        ChronicleEntry second = ChronicleTestEntries.Create(
            seed: "tie-b",
            time: ChronicleTestEntries.Instant(same));

        IReadOnlyList<ChronicleEntry> sorted =
            ChronicleDisplayOrder.Sort([first, second]);

        Assert.True(
            string.CompareOrdinal(
                sorted[0].EntryId.Value,
                sorted[1].EntryId.Value) > 0);
    }

    [Fact]
    public void ProfileOptions_ExposeProductTimelinesAndDefaults()
    {
        Assert.Equal(ChronicleTimeline.Main, ChronicleProfileOptions.DefaultTimeline);
        Assert.Equal(
            ChronicleGranularity.Default,
            ChronicleProfileOptions.DefaultGranularity);
        Assert.Equal(4, ChronicleProfileOptions.Timelines.Count);
        Assert.Contains(ChronicleTimeline.Main, ChronicleProfileOptions.Timelines);
        Assert.Contains(ChronicleTimeline.Character, ChronicleProfileOptions.Timelines);
        Assert.Contains(ChronicleTimeline.Weapon, ChronicleProfileOptions.Timelines);
        Assert.Contains(ChronicleTimeline.Challenge, ChronicleProfileOptions.Timelines);

        ChronicleProfileRequest request = ChronicleProfileOptions.Build(
            ChronicleTimeline.Character,
            ChronicleGranularity.Detailed,
            includeStateChanges: true);
        Assert.Equal(ChronicleTimeline.Character, request.Timeline);
        Assert.Equal(ChronicleGranularity.Detailed, request.Granularity);
        Assert.True(request.IncludeStateChanges);
    }

    [Fact]
    public void RangeNames_AreChinese()
    {
        Assert.Equal(
            "全部时间",
            ChronicleTimeRangeOptions.Name(ChronicleTimeRangePreset.All));
        Assert.Equal(
            "最近 30 天",
            ChronicleTimeRangeOptions.Name(ChronicleTimeRangePreset.Last30Days));
        Assert.Equal(
            "最近 90 天",
            ChronicleTimeRangeOptions.Name(ChronicleTimeRangePreset.Last90Days));
        Assert.Equal(
            "今年",
            ChronicleTimeRangeOptions.Name(ChronicleTimeRangePreset.ThisYear));
    }
}

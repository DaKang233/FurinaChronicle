// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChronicleTimeDisplayTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    [Fact]
    public void Instant_UsesOccurrenceBasisAndKeepsOriginalOffset()
    {
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(
                OccurrenceTime.At(
                    new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.FromHours(8)),
                    TimePrecision.Second),
                observedAt: null,
                fetchedAt: null),
            Utc);

        Assert.Equal("发生于 2025-12-31 16:00:05", display.PrimaryText);
        Assert.Equal("发生", display.BasisText);
        Assert.Equal("精确到秒", display.PrecisionText);
        Assert.Equal("原始偏移 UTC+08:00", display.OriginalOffsetText);
        Assert.Contains("排序依据：发生时间", display.DetailText);
        Assert.False(display.IsUndated);
        Assert.Equal(ChronicleSortBasis.Occurred, display.SortBasis);
    }

    [Fact]
    public void ObservationOnly_IsNeverPresentedAsAnOccurrence()
    {
        var observed =
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(8));
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(occurred: null, observed, fetchedAt: null),
            Utc);

        Assert.Equal("观察于 2026-01-01 04:00:00", display.PrimaryText);
        Assert.DoesNotContain("发生于", display.PrimaryText);
        Assert.Contains("没有可确认的业务发生时间", display.SecondaryText);
        Assert.Equal("观察", display.BasisText);
        Assert.Equal(ChronicleSortBasis.Observed, display.SortBasis);
    }

    [Fact]
    public void FetchOnly_IsLabelledAsCollectionTime()
    {
        var fetched =
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(8));
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(occurred: null, observedAt: null, fetched),
            Utc);

        Assert.Equal("采集于 2026-01-01 04:00:00", display.PrimaryText);
        Assert.DoesNotContain("发生于", display.PrimaryText);
        Assert.DoesNotContain("观察于", display.PrimaryText);
        Assert.Equal("采集", display.BasisText);
        Assert.Equal(ChronicleSortBasis.Fetched, display.SortBasis);
    }

    [Fact]
    public void Undated_IsExplicitAndNeverSubstitutesAnotherTimestamp()
    {
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(occurred: null, observedAt: null, fetchedAt: null),
            Utc);

        Assert.Equal("未注明时间", display.PrimaryText);
        Assert.True(display.IsUndated);
        Assert.Null(display.SortAtUtc);
        Assert.Contains("不会使用导入时间或当前时间替代", display.SecondaryText);
        Assert.Equal(ChronicleSortBasis.Unknown, display.SortBasis);
    }

    [Fact]
    public void Interval_KeepsDayPrecision()
    {
        OccurrenceTime interval = OccurrenceTime.ForInterval(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(8)),
            TimePrecision.Day);
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(interval, null, null),
            Utc);

        Assert.Equal(
            "发生于 2025-12-31 至 2026-01-01",
            display.PrimaryText);
        Assert.Equal("精确到日", display.PrecisionText);
        Assert.Equal("发生", display.BasisText);
    }

    [Fact]
    public void UncertainInterval_StatesTheRangeExplicitly()
    {
        OccurrenceTime uncertain = OccurrenceTime.Within(
            new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.FromHours(8)),
            new DateTimeOffset(2026, 1, 1, 0, 0, 10, TimeSpan.FromHours(8)),
            TimePrecision.Second);
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(uncertain, null, null),
            Utc);

        Assert.Contains("之间（不确定范围）", display.PrimaryText);
        Assert.Contains("不表示精确时刻", display.SecondaryText);
    }

    [Fact]
    public void OccurrenceWithObservation_KeepsBothSemantics()
    {
        var occurred =
            new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.FromHours(8));
        var observed =
            new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(8));
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(
                OccurrenceTime.At(occurred, TimePrecision.Second),
                observed,
                observed.AddMinutes(1)),
            Utc);

        Assert.StartsWith("发生于", display.PrimaryText, StringComparison.Ordinal);
        Assert.Contains("观察于", display.DetailText, StringComparison.Ordinal);
        Assert.Contains("采集于", display.DetailText, StringComparison.Ordinal);
    }

    [Fact]
    public void WeekRange_UsesWeekPrecisionWording()
    {
        OccurrenceTime week = ManualAcquisitionTime.ForWeek(
            new DateOnly(2026, 1, 7),
            TimeSpan.FromHours(8));
        ChronicleTimeDisplay display = ChronicleTimeDisplay.From(
            new ChronicleTime(week, null, null),
            Utc);

        Assert.Equal("精确到周", display.PrecisionText);
        Assert.Contains(" 至 ", display.PrimaryText, StringComparison.Ordinal);
        Assert.Contains("原始偏移 UTC+08:00", display.DetailText);
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Tests.Core.Records;

public sealed class SharedTimeAndProvenanceTests
{
    [Fact]
    public void OccurrenceTime_Unknown_DoesNotInventTimeOrPrecision()
    {
        Assert.Equal(OccurrenceTimeKind.Unknown, OccurrenceTime.Unknown.Kind);
        Assert.Null(OccurrenceTime.Unknown.Start);
        Assert.Null(OccurrenceTime.Unknown.End);
        Assert.Null(OccurrenceTime.Unknown.Precision);
    }

    [Fact]
    public void OccurrenceTime_Instant_PreservesOffsetAndComparesByUtc()
    {
        var source = new DateTimeOffset(
            2026,
            10,
            5,
            20,
            30,
            0,
            TimeSpan.FromHours(8));
        OccurrenceTime occurrence = OccurrenceTime.At(
            source,
            TimePrecision.Second);

        Assert.Equal(source.Offset, occurrence.Start?.Offset);
        Assert.Equal(
            new DateTimeOffset(2026, 10, 5, 12, 30, 0, TimeSpan.Zero),
            occurrence.UtcStart);
        Assert.Equal(
            OccurrenceTime.At(
                source.ToOffset(TimeSpan.FromHours(-4)),
                TimePrecision.Second).UtcStart,
            occurrence.UtcStart);
    }

    [Theory]
    [InlineData(TimePrecision.Second)]
    [InlineData(TimePrecision.Minute)]
    [InlineData(TimePrecision.Hour)]
    [InlineData(TimePrecision.Day)]
    [InlineData(TimePrecision.Week)]
    public void OccurrenceTime_Instant_AcceptsSharedPrecisions(
        TimePrecision precision)
    {
        OccurrenceTime occurrence = OccurrenceTime.At(
            DateTimeOffset.UnixEpoch,
            precision);

        Assert.Equal(precision, occurrence.Precision);
    }

    [Fact]
    public void OccurrenceTime_IntervalEndBeforeStart_IsRejected()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch.AddHours(1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OccurrenceTime.ForInterval(
                start,
                DateTimeOffset.UnixEpoch,
                TimePrecision.Hour));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            OccurrenceTime.Within(
                start,
                DateTimeOffset.UnixEpoch,
                TimePrecision.Hour));
    }

    [Fact]
    public void RecordTimestamps_KeepIndependentNullableMeanings()
    {
        DateTimeOffset fetchedAt = DateTimeOffset.UnixEpoch.AddMinutes(1);
        var timestamps = new RecordTimestamps(FetchedAt: fetchedAt);

        Assert.Null(timestamps.ObservedAt);
        Assert.Equal(fetchedAt, timestamps.FetchedAt);
        Assert.Null(timestamps.ImportedAt);
    }

    [Fact]
    public void DataSourceReference_NormalizesValuesAndRejectsEmptyReference()
    {
        var reference = new DataSourceReference(
            provider: " provider ",
            sourceRecordId: " record ");

        Assert.Equal("provider", reference.Provider);
        Assert.Equal("record", reference.SourceRecordId);
        Assert.Throws<ArgumentException>(
            () => new DataSourceReference(" ", null, ""));
    }

    [Fact]
    public void AcquisitionBatchId_EmptyGuid_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new AcquisitionBatchId(Guid.Empty));
    }

    [Fact]
    public void RecordProvenance_Unknown_DoesNotInventSourceOrTimes()
    {
        Assert.Equal(DataOrigin.Unknown, RecordProvenance.Unknown.Origin);
        Assert.Equal(
            RecordTimestamps.Unknown,
            RecordProvenance.Unknown.Timestamps);
        Assert.Null(RecordProvenance.Unknown.Source);
        Assert.Null(RecordProvenance.Unknown.AcquisitionBatchId);
    }

    [Fact]
    public void RecordProvenance_UndefinedOrigin_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordProvenance((DataOrigin)999));
    }
}

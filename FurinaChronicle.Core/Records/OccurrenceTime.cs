// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Records;

public sealed record OccurrenceTime
{
    private OccurrenceTime(
        OccurrenceTimeKind kind,
        DateTimeOffset? start,
        DateTimeOffset? end,
        TimePrecision? precision)
    {
        Kind = kind;
        Start = start;
        End = end;
        Precision = precision;
    }

    public static OccurrenceTime Unknown { get; } =
        new(OccurrenceTimeKind.Unknown, null, null, null);

    public OccurrenceTimeKind Kind { get; }

    public DateTimeOffset? Start { get; }

    public DateTimeOffset? End { get; }

    public TimePrecision? Precision { get; }

    public DateTimeOffset? UtcStart => Start?.ToUniversalTime();

    public DateTimeOffset? UtcEnd => End?.ToUniversalTime();

    public static OccurrenceTime At(
        DateTimeOffset occurredAt,
        TimePrecision precision)
    {
        ValidatePrecision(precision);
        return new OccurrenceTime(
            OccurrenceTimeKind.Instant,
            occurredAt,
            null,
            precision);
    }

    public static OccurrenceTime ForInterval(
        DateTimeOffset start,
        DateTimeOffset end,
        TimePrecision precision)
    {
        ValidateRange(start, end, precision);
        return new OccurrenceTime(
            OccurrenceTimeKind.Interval,
            start,
            end,
            precision);
    }

    public static OccurrenceTime Within(
        DateTimeOffset earliest,
        DateTimeOffset latest,
        TimePrecision precision)
    {
        ValidateRange(earliest, latest, precision);
        return new OccurrenceTime(
            OccurrenceTimeKind.UncertainInterval,
            earliest,
            latest,
            precision);
    }

    private static void ValidateRange(
        DateTimeOffset start,
        DateTimeOffset end,
        TimePrecision precision)
    {
        ValidatePrecision(precision);
        if (end.ToUniversalTime() < start.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(end),
                "The end of an occurrence range cannot be earlier than its start.");
        }
    }

    private static void ValidatePrecision(TimePrecision precision)
    {
        if (!Enum.IsDefined(precision))
        {
            throw new ArgumentOutOfRangeException(
                nameof(precision),
                precision,
                "The time precision is not defined.");
        }
    }
}

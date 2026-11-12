// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Renders Chronicle time semantics without substituting one basis for another.
/// An entry with only an observation or fetch time is never shown as an exact
/// occurrence time, and the original offset is always preserved in detail text.
/// </summary>
public sealed record ChronicleTimeDisplay
{
    private ChronicleTimeDisplay(
        string primaryText,
        string secondaryText,
        string basisText,
        string precisionText,
        string? originalOffsetText,
        string sortBasisText,
        bool isUndated,
        DateTimeOffset? sortAtUtc,
        DateTimeOffset? localSortAt,
        ChronicleSortBasis sortBasis,
        TimePrecision? precision)
    {
        PrimaryText = primaryText;
        SecondaryText = secondaryText;
        BasisText = basisText;
        PrecisionText = precisionText;
        OriginalOffsetText = originalOffsetText;
        SortBasisText = sortBasisText;
        IsUndated = isUndated;
        SortAtUtc = sortAtUtc;
        LocalSortAt = localSortAt;
        SortBasis = sortBasis;
        Precision = precision;
    }

    public string PrimaryText { get; }

    public string SecondaryText { get; }

    public string BasisText { get; }

    public string PrecisionText { get; }

    public string? OriginalOffsetText { get; }

    public string SortBasisText { get; }

    public bool IsUndated { get; }

    public DateTimeOffset? SortAtUtc { get; }

    /// <summary>Sort anchor converted to the configured display zone.</summary>
    public DateTimeOffset? LocalSortAt { get; }

    public ChronicleSortBasis SortBasis { get; }

    public TimePrecision? Precision { get; }

    public string DetailText => string.Join(
        " · ",
        new[] { PrecisionText, OriginalOffsetText, SortBasisText, SecondaryText }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public IReadOnlyList<string> Lines =>
        string.IsNullOrWhiteSpace(SecondaryText)
            ? [PrimaryText]
            : [PrimaryText, SecondaryText];

    public static ChronicleTimeDisplay From(
        ChronicleTime time,
        TimeZoneInfo? displayTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(time);
        TimeZoneInfo zone = displayTimeZone ?? TimeZoneInfo.Local;
        DateTimeOffset? localSort = time.SortAtUtc is { } anchor
            ? TimeZoneInfo.ConvertTime(anchor, zone)
            : null;

        if (time.Occurred is { } occurred)
        {
            return FromOccurrence(time, occurred, zone, localSort);
        }

        if (time.ObservedAt is { } observed)
        {
            string observedText = Format(observed, null, zone);
            string secondary = "该条目没有可确认的业务发生时间；此处为观察时间。";
            if (time.FetchedAt is { } fetched)
            {
                secondary += $"采集于 {Format(fetched, null, zone)}。";
            }
            return new ChronicleTimeDisplay(
                $"观察于 {observedText}",
                secondary,
                "观察",
                "不适用（观察时间不声明业务精度）",
                OffsetText(observed.Offset),
                $"排序依据：{ChronicleDisplayText.SortBasis(time.SortBasis)}",
                isUndated: false,
                time.SortAtUtc,
                localSort,
                time.SortBasis,
                precision: null);
        }

        if (time.FetchedAt is { } fetchOnly)
        {
            return new ChronicleTimeDisplay(
                $"采集于 {Format(fetchOnly, null, zone)}",
                "该条目没有发生时间或观察时间；此处为采集时间，不代表游戏内发生时刻。",
                "采集",
                "不适用（采集时间不声明业务精度）",
                OffsetText(fetchOnly.Offset),
                $"排序依据：{ChronicleDisplayText.SortBasis(time.SortBasis)}",
                isUndated: false,
                time.SortAtUtc,
                localSort,
                time.SortBasis,
                precision: null);
        }

        return new ChronicleTimeDisplay(
            "未注明时间",
            "该条目没有发生、观察或采集时间；不会使用导入时间或当前时间替代。",
            "未知",
            "精度未知",
            originalOffsetText: null,
            "排序依据：无排序锚点",
            isUndated: true,
            sortAtUtc: null,
            localSortAt: null,
            ChronicleSortBasis.Unknown,
            precision: null);
    }

    private static ChronicleTimeDisplay FromOccurrence(
        ChronicleTime time,
        OccurrenceTime occurred,
        TimeZoneInfo zone,
        DateTimeOffset? localSort)
    {
        TimePrecision? precision = occurred.Precision;
        string precisionText = PrecisionTextValue(precision);
        string observedNote = time.ObservedAt is { } observed
            ? $"观察于 {Format(observed, null, zone)}。"
            : string.Empty;
        string fetchedNote = time.FetchedAt is { } fetched
            ? $"采集于 {Format(fetched, null, zone)}。"
            : string.Empty;
        string extra = observedNote + fetchedNote;

        if (occurred.Kind == OccurrenceTimeKind.Instant)
        {
            DateTimeOffset start = occurred.Start!.Value;
            return new ChronicleTimeDisplay(
                $"发生于 {Format(start, precision, zone)}",
                "该时间是业务发生时间。" + extra,
                "发生",
                precisionText,
                OffsetText(start.Offset),
                $"排序依据：{ChronicleDisplayText.SortBasis(time.SortBasis)}",
                isUndated: false,
                time.SortAtUtc,
                localSort,
                time.SortBasis,
                precision);
        }

        DateTimeOffset rangeStart = occurred.Start!.Value;
        DateTimeOffset rangeEnd = occurred.End!.Value;
        string offsetText = rangeStart.Offset == rangeEnd.Offset
            ? OffsetText(rangeStart.Offset)
            : $"{OffsetText(rangeStart.Offset)} / {OffsetText(rangeEnd.Offset)}";
        if (occurred.Kind == OccurrenceTimeKind.UncertainInterval)
        {
            return new ChronicleTimeDisplay(
                $"发生于 {Format(rangeStart, precision, zone)} 至 {Format(rangeEnd, precision, zone)} 之间（不确定范围）",
                "只确认该事件发生在上述范围内，不表示精确时刻。" + extra,
                "发生",
                precisionText,
                offsetText,
                $"排序依据：{ChronicleDisplayText.SortBasis(time.SortBasis)}",
                isUndated: false,
                time.SortAtUtc,
                localSort,
                time.SortBasis,
                precision);
        }

        return new ChronicleTimeDisplay(
            $"发生于 {Format(rangeStart, precision, zone)} 至 {Format(rangeEnd, precision, zone)}",
            "该时间是业务发生的确定区间。" + extra,
            "发生",
            precisionText,
            offsetText,
            $"排序依据：{ChronicleDisplayText.SortBasis(time.SortBasis)}",
            isUndated: false,
            time.SortAtUtc,
            localSort,
            time.SortBasis,
            precision);
    }

    private static string Format(
        DateTimeOffset value,
        TimePrecision? precision,
        TimeZoneInfo zone)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(value, zone);
        string format = precision switch
        {
            TimePrecision.Second => "yyyy-MM-dd HH:mm:ss",
            TimePrecision.Minute => "yyyy-MM-dd HH:mm",
            TimePrecision.Hour => "yyyy-MM-dd HH:00",
            TimePrecision.Day => "yyyy-MM-dd",
            TimePrecision.Week => "yyyy-MM-dd",
            _ => "yyyy-MM-dd HH:mm:ss",
        };
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string PrecisionTextValue(TimePrecision? precision) =>
        precision switch
        {
            TimePrecision.Second => "精确到秒",
            TimePrecision.Minute => "精确到分钟",
            TimePrecision.Hour => "精确到小时",
            TimePrecision.Day => "精确到日",
            TimePrecision.Week => "精确到周",
            _ => "精度未知",
        };

    private static string OffsetText(TimeSpan offset)
    {
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        TimeSpan magnitude = offset.Duration();
        return $"原始偏移 UTC{sign}{magnitude.Hours:D2}:{magnitude.Minutes:D2}";
    }
}

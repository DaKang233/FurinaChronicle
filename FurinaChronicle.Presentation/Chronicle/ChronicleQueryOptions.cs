// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Predefined relative time windows offered by the Chronicle page. The
/// calculation takes an explicit "now" so tests and hosts agree without a
/// hidden clock read; the window is a half-open UTC range.
/// </summary>
public enum ChronicleTimeRangePreset
{
    All = 0,
    Last30Days = 1,
    Last90Days = 2,
    ThisYear = 3,
}

public sealed record ChronicleTimeRange(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null)
{
    public bool IsUnbounded => From is null && To is null;
}

public static class ChronicleTimeRangeOptions
{
    public static IReadOnlyList<ChronicleTimeRangePreset> Presets { get; } =
    [
        ChronicleTimeRangePreset.All,
        ChronicleTimeRangePreset.Last30Days,
        ChronicleTimeRangePreset.Last90Days,
        ChronicleTimeRangePreset.ThisYear,
    ];

    public static string Name(ChronicleTimeRangePreset preset) => preset switch
    {
        ChronicleTimeRangePreset.All => "全部时间",
        ChronicleTimeRangePreset.Last30Days => "最近 30 天",
        ChronicleTimeRangePreset.Last90Days => "最近 90 天",
        ChronicleTimeRangePreset.ThisYear => "今年",
        _ => "全部时间",
    };

    public static ChronicleTimeRange Resolve(
        ChronicleTimeRangePreset preset,
        DateTimeOffset now,
        TimeZoneInfo? displayTimeZone = null)
    {
        TimeZoneInfo zone = displayTimeZone ?? TimeZoneInfo.Local;
        return preset switch
        {
            ChronicleTimeRangePreset.Last30Days => new(now.AddDays(-30), null),
            ChronicleTimeRangePreset.Last90Days => new(now.AddDays(-90), null),
            ChronicleTimeRangePreset.ThisYear => new(
                StartOfYear(now.Year, zone),
                StartOfYear(now.Year + 1, zone)),
            _ => new ChronicleTimeRange(),
        };
    }

    private static DateTimeOffset StartOfYear(int year, TimeZoneInfo zone)
    {
        DateTime local = new(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}

/// <summary>
/// Builds a Chronicle filter from the page's compact filter bar. Filtering only
/// changes the query; it never rewrites domain facts.
/// </summary>
public static class ChronicleFilterOptions
{
    public static IReadOnlyList<ChronicleDomain> Domains { get; } =
    [
        ChronicleDomain.Gacha,
        ChronicleDomain.Acquisition,
        ChronicleDomain.Inventory,
        ChronicleDomain.Challenge,
        ChronicleDomain.Achievement,
    ];

    public static IReadOnlyList<int> Ranks { get; } = [5, 4, 3];

    public static ChronicleFilter Build(
        ChronicleTimeRange range,
        bool includeUndated,
        ChronicleDomain? domain = null,
        int? rank = null) => new(
        From: range.From,
        To: range.To,
        IncludeUndated: includeUndated,
        Domains: domain is null
            ? null
            : new HashSet<ChronicleDomain> { domain.Value },
        Ranks: rank is null ? null : new HashSet<int> { rank.Value });
}

public static class ChronicleProfileOptions
{
    public static IReadOnlyList<ChronicleTimeline> Timelines { get; } =
    [
        ChronicleTimeline.Main,
        ChronicleTimeline.Character,
        ChronicleTimeline.Weapon,
        ChronicleTimeline.Challenge,
    ];

    public static IReadOnlyList<ChronicleGranularity> Granularities { get; } =
    [
        ChronicleGranularity.Default,
        ChronicleGranularity.Detailed,
    ];

    public static ChronicleTimeline DefaultTimeline => ChronicleTimeline.Main;

    public static ChronicleGranularity DefaultGranularity =>
        ChronicleGranularity.Default;

    public static ChronicleProfileRequest Build(
        ChronicleTimeline timeline,
        ChronicleGranularity granularity,
        bool includeStateChanges) => new(timeline, granularity, includeStateChanges);
}

/// <summary>
/// Client-side display order that mirrors the Chronicle service contract:
/// timed entries first by sort anchor descending, undated entries last, ties by
/// entry id descending. Pages normally receive already ordered pages; this is
/// used for reports and defensive re-ordering when merging a single generation.
/// </summary>
public sealed class ChronicleDisplayOrder : IComparer<ChronicleEntry>
{
    public static ChronicleDisplayOrder Instance { get; } = new();

    public int Compare(ChronicleEntry? x, ChronicleEntry? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return 1;
        }
        if (y is null)
        {
            return -1;
        }

        int undated = (x.Time.IsUndated ? 1 : 0) - (y.Time.IsUndated ? 1 : 0);
        if (undated != 0)
        {
            return undated;
        }

        if (!x.Time.IsUndated)
        {
            int anchor = -Nullable.Compare(x.Time.SortAtUtc, y.Time.SortAtUtc);
            if (anchor != 0)
            {
                return anchor;
            }
        }

        return -string.CompareOrdinal(x.EntryId.Value, y.EntryId.Value);
    }

    public static IReadOnlyList<ChronicleEntry> Sort(
        IEnumerable<ChronicleEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var copy = entries.ToList();
        copy.Sort(Instance);
        return copy;
    }
}

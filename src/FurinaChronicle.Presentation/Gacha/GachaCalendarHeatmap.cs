// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.Analytics;

namespace FurinaChronicle.Presentation.Gacha;

/// <summary>A bounded, platform-independent year of daily cells. Empty days remain visible.</summary>
public sealed record GachaCalendarHeatmap(int Year, IReadOnlyList<GachaCalendarCell> Days)
{
    public long TotalPulls => Days.Sum(day => (long)day.Pulls);
    public int ActiveDays => Days.Count(day => day.Pulls > 0);
    public int Weeks => Days[^1].Week + 1;

    public static GachaCalendarHeatmap FromDailyCounts(int year, IReadOnlyList<GachaCalendarDay> days) =>
        FromRows(year, days.Select(day => new GachaReadOnlyRow("", "", "", "", "", "", new(null, null))
        {
            CalendarDay = day.Date, CalendarPulls = day.TotalPulls,
            CalendarFiveStarCount = day.FiveStarCount, CalendarFourStarCount = day.FourStarCount,
        }).ToArray());

    public static GachaCalendarHeatmap FromRows(int year, IReadOnlyList<GachaReadOnlyRow> rows)
    {
        // Scaling belongs to the displayed year, never to all years or the previously selected scope.
        GachaCalendarCell[] cells = GachaCalendarCell.BuildYear(year, rows).ToArray();
        int maximum = cells.Max(day => day.Pulls);
        return new(year, cells.Select(day => day with
        {
            IntensityLevel = day.Pulls <= 0 ? 0 :
                Math.Clamp((int)Math.Ceiling(day.Pulls * 5D / maximum), 1, 5),
        }).ToArray());
    }

    public static string ColorHex(int intensity, bool darkTheme) => intensity switch
    {
        5 => "#66B9F9",
        4 => "#2997F3",
        3 => "#0078D4",
        2 => "#005A9E",
        1 => "#0C3655",
        _ => darkTheme ? "#2E2E2E" : "#E5E5E5",
    };
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Presentation.Gacha;

// Hosts own dispatch, paths, image objects and navigation. No platform objects cross this boundary.
public interface IUiDispatcher
{
    Task InvokeAsync(Action action);
}

public sealed record GachaScopeOption(Guid Id, string Label)
{
    public GameServerRegion? Region { get; init; }
}
public sealed record GachaImageReference(string? ResourceKey, string? CachedPath);
public sealed record GachaReadOnlyRow(
    string ExternalRecordId, string Name, string Rank, string Time,
    string Pool, string Source, GachaImageReference Image)
{
    public string? AccountLabel { get; init; }
    public string? Subtitle { get; init; }
    public string? DetailText { get; init; }
    public DateOnly? CalendarDay { get; init; }
    public int CalendarPulls { get; init; }
    public GachaFactReference? Reference { get; init; }
    public string? ItemType { get; init; }
    public string Detail => (Subtitle ?? $"{Rank} · {Pool} · {Time} · {Source}") +
        (AccountLabel is null ? "" : $"\n{AccountLabel}");
}
public sealed record GachaReadOnlyPage(
    IReadOnlyList<GachaReadOnlyRow> Rows, int TotalCount, int PageNumber, int TotalPages)
{
    public DateOnly? VisibleMonth { get; init; }
}

public interface IGachaReadOnlySource
{
    Task<IReadOnlyList<GachaScopeOption>> GetArchivesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GachaScopeOption>> GetAccountsAsync(Guid archiveId, CancellationToken cancellationToken);
    Task<GachaReadOnlyPage> GetPageAsync(Guid accountId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}

public interface IGachaScopedReadOnlySource : IGachaReadOnlySource
{
    Task<GachaReadOnlyPage> GetPageAsync(GachaRecordQuery query, int pageNumber, int pageSize,
        IReadOnlyDictionary<Guid, string> accountLabels, CancellationToken cancellationToken);
    Task<GachaReadOnlyPage> GetAnalysisAsync(GachaRecordQuery query, GachaReadOnlyView view,
        IReadOnlyList<GachaScopeOption> accounts, int pageNumber, int pageSize, GachaCalendarFilter calendar,
        CancellationToken cancellationToken);
}

public enum GachaReadOnlyView { Records, Overview, History, Calendar, Items }

public sealed record GachaReadOnlyViewOption(GachaReadOnlyView View, string Label)
{
    public static IReadOnlyList<GachaReadOnlyViewOption> Options { get; } =
    [new(GachaReadOnlyView.Records, "明细"), new(GachaReadOnlyView.Overview, "总览"),
        new(GachaReadOnlyView.History, "历史"), new(GachaReadOnlyView.Calendar, "日历"), new(GachaReadOnlyView.Items, "物品")];
}

public sealed record GachaReadOnlyFilter(
    int? Rank = null,
    GachaPoolGroup? Pool = null,
    GachaRecordSortOrder SortOrder = GachaRecordSortOrder.NewestFirst,
    DateOnly? Day = null,
    TimeSpan DayOffset = default)
{
    public static IReadOnlyList<string> Ranks { get; } = ["全部星级", "三星", "四星", "五星"];
    public static IReadOnlyList<string> Pools { get; } = ["全部卡池", .. Enum.GetValues<GachaPoolGroup>()
        .Select(GachaPoolGroupResolver.GetDisplayName)];
    public static IReadOnlyList<string> SortOrders { get; } = ["最新在前", "最早在前"];
    public int RankIndex => Rank is int rank ? rank - 2 : 0;
    public int PoolIndex => Pool is GachaPoolGroup pool ? (int)pool + 1 : 0;

    public static GachaReadOnlyFilter FromIndices(int rank, int pool, int sort) => new(
        rank > 0 ? rank + 2 : null,
        pool > 0 ? (GachaPoolGroup)(pool - 1) : null,
        sort == 1 ? GachaRecordSortOrder.OldestFirst : GachaRecordSortOrder.NewestFirst);
}

public sealed record GachaCalendarFilter(DateOnly? Month, bool UseChinaTime)
{
    public TimeSpan Offset => UseChinaTime ? TimeSpan.FromHours(8) : TimeSpan.Zero;
    public string TimeZoneLabel => UseChinaTime ? "UTC+08:00" : "UTC";
    public DateTimeOffset? Start => Month is DateOnly month
        ? new DateTimeOffset(month.Year, month.Month, 1, 0, 0, 0, Offset) : null;
    public DateTimeOffset? End => Start?.AddMonths(1).AddTicks(-1);
}

public sealed record GachaCalendarCell(DateOnly Date, int Column, int Week, int Pulls)
{
    public string Label => $"{Date:yyyy-MM-dd} · {Pulls} 抽";
    public static IReadOnlyList<string> Weekdays { get; } = ["一", "二", "三", "四", "五", "六", "日"];

    public static IEnumerable<GachaCalendarCell> Build(DateOnly month, IReadOnlyList<GachaReadOnlyRow> rows)
    {
        var counts = rows.Where(row => row.CalendarDay.HasValue)
            .ToDictionary(row => row.CalendarDay!.Value, row => row.CalendarPulls);
        DateOnly first = new(month.Year, month.Month, 1);
        int shift = ((int)first.DayOfWeek + 6) % 7;
        for (int i = 0; i < DateTime.DaysInMonth(first.Year, first.Month); i++)
        {
            DateOnly day = first.AddDays(i);
            yield return new(day, (shift + i) % 7, (shift + i) / 7, counts.GetValueOrDefault(day));
        }
    }
}

public sealed record GachaReadOnlySnapshot
{
    public IReadOnlyList<GachaScopeOption> Archives { get; init; } = [];
    public IReadOnlyList<GachaScopeOption> Accounts { get; init; } = [];
    public Guid? ArchiveId { get; init; }
    public Guid? AccountId { get; init; }
    public IReadOnlyList<Guid>? ArchiveAccountIds { get; init; }
    public GachaReadOnlyFilter Filter { get; init; } = new();
    public GachaReadOnlyView View { get; init; }
    public GachaCalendarFilter Calendar { get; init; } = new(null, true);
    public DateOnly? VisibleMonth { get; init; }
    public IReadOnlyList<GachaReadOnlyRow> Rows { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; } = 1;
    public int TotalPages { get; init; } = 1;
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入";
    public string Summary => $"{(View == GachaReadOnlyView.Records ? "本地记录" : "分析条目")} {TotalCount} 条 · 第 {PageNumber}/{TotalPages} 页";
    public bool CanPrevious => !IsBusy && PageNumber > 1;
    public bool CanNext => !IsBusy && PageNumber < TotalPages;
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.ComponentModel;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Presentation.Gacha;

// One instance per page. Selection is deliberately in memory and never shared across pages.
// All entry points and snapshot changes run on the host's UI dispatcher; I/O may complete anywhere.
public sealed class GachaReadOnlyState(IGachaReadOnlySource source, IUiDispatcher dispatcher)
    : INotifyPropertyChanged
{
    public const int PageSize = 25;
    private readonly Dictionary<Guid, Guid> rememberedAccounts = [];
    private CancellationTokenSource? pending;
    private long generation;
    private bool active;
    public GachaReadOnlySnapshot Snapshot { get; private set; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;

    public Task EnterAsync()
    {
        active = true;
        return ReloadPageAsync(1);
    }

    public Task SelectArchiveAsync(Guid id) => LoadAsync(id, null, 1, null, Snapshot.Filter);
    public Task SelectAccountAsync(Guid id) => LoadAsync(Snapshot.ArchiveId, id, 1, null, Snapshot.Filter);
    public Task SelectArchiveAccountsAsync(IReadOnlyList<Guid> ids) =>
        SelectScopeAsync(Snapshot.ArchiveId, Snapshot.AccountId, ids);
    public Task SelectScopeAsync(Guid? archiveId, Guid? accountId, IReadOnlyList<Guid>? archiveAccountIds) =>
        LoadAsync(archiveId, accountId, 1, archiveAccountIds?.Distinct().ToArray(), Snapshot.Filter);
    public Task ApplyFilterAsync(GachaReadOnlyFilter filter) =>
        LoadAsync(Snapshot.ArchiveId, Snapshot.AccountId, 1, Snapshot.ArchiveAccountIds, filter);
    public Task SelectViewAsync(GachaReadOnlyView view) =>
        LoadAsync(Snapshot.ArchiveId, Snapshot.AccountId, 1, Snapshot.ArchiveAccountIds, Snapshot.Filter, view);
    public Task ApplyCalendarAsync(GachaCalendarFilter calendar) =>
        LoadAsync(Snapshot.ArchiveId, Snapshot.AccountId, 1, Snapshot.ArchiveAccountIds, Snapshot.Filter,
            GachaReadOnlyView.Calendar, calendar);
    public Task ShiftCalendarAsync(int months)
    {
        if (Snapshot.VisibleMonth is not DateOnly month) return Task.CompletedTask;
        return ApplyCalendarAsync(Snapshot.Calendar with { Month = month.AddMonths(months) });
    }
    public Task OpenCalendarDayAsync(DateOnly day) =>
        LoadAsync(Snapshot.ArchiveId, Snapshot.AccountId, 1, Snapshot.ArchiveAccountIds,
            new GachaReadOnlyFilter(Day: day, DayOffset: Snapshot.Calendar.Offset), GachaReadOnlyView.Records);
    public Task ChangePageAsync(int page) => ReloadPageAsync(Math.Max(1, page));

    private Task ReloadPageAsync(int page) => LoadAsync(
        Snapshot.ArchiveId, Snapshot.AccountId, page, Snapshot.ArchiveAccountIds, Snapshot.Filter);

    public void Leave()
    {
        active = false;
        Cancel();
    }

    public void Cancel()
    {
        generation++;
        pending?.Cancel();
        pending?.Dispose();
        pending = null;
        Set(Snapshot with
        {
            Rows = [], VisibleMonth = null, TotalCount = 0, PageNumber = 1, TotalPages = 1,
            IsBusy = false, Status = "已取消载入；可重新载入"
        });
    }

    private async Task LoadAsync(Guid? requestedArchive, Guid? requestedAccount, int page,
        IReadOnlyList<Guid>? requestedAccounts, GachaReadOnlyFilter filter, GachaReadOnlyView? requestedView = null,
        GachaCalendarFilter? requestedCalendar = null)
    {
        if (!active) return;
        pending?.Cancel();
        pending?.Dispose();
        var request = new CancellationTokenSource();
        pending = request;
        CancellationToken token = request.Token;
        long version = ++generation;
        GachaReadOnlyView view = requestedView ?? Snapshot.View;
        GachaCalendarFilter calendar = requestedCalendar ?? Snapshot.Calendar;
        Dictionary<Guid, Guid> remembered = new(rememberedAccounts);
        // Preserve the requested IDs immediately, so fast A -> B -> paging cannot revive A.
        Set(Snapshot with
        {
            ArchiveId = requestedArchive, AccountId = requestedAccount,
            ArchiveAccountIds = requestedAccounts, Filter = filter, View = view, Calendar = calendar,
            Accounts = requestedArchive == Snapshot.ArchiveId ? Snapshot.Accounts : [],
            Rows = [], VisibleMonth = null, TotalCount = 0, PageNumber = 1, TotalPages = 1,
            IsBusy = true, Status = "正在读取本地记录…"
        });
        try
        {
            IReadOnlyList<GachaScopeOption> archives = await source.GetArchivesAsync(token).ConfigureAwait(false);
            Guid? archiveId = archives.FirstOrDefault(a => a.Id == requestedArchive)?.Id ?? archives.FirstOrDefault()?.Id;
            IReadOnlyList<GachaScopeOption> accounts = archiveId is Guid id
                ? await source.GetAccountsAsync(id, token).ConfigureAwait(false) : [];
            Guid? desiredAccount = requestedAccount;
            if (desiredAccount is null && archiveId is Guid rememberedArchive && remembered.TryGetValue(rememberedArchive, out Guid saved))
                desiredAccount = saved;
            Guid? accountId = accounts.FirstOrDefault(a => a.Id == desiredAccount)?.Id ?? accounts.FirstOrDefault()?.Id;
            Guid[]? archiveAccountIds = requestedAccounts is null ? null : accounts
                .Where(account => requestedAccounts.Contains(account.Id)).Select(account => account.Id).ToArray();
            Guid[] scopeIds = archiveAccountIds ?? (accountId is Guid selected ? [selected] : []);
            bool fallback = (requestedArchive is not null && archiveId != requestedArchive) ||
                (desiredAccount is not null && accountId != desiredAccount);
            bool scopeShrank = requestedAccounts?.Count > archiveAccountIds?.Length;
            await ApplyAsync(version, token, () => Set(Snapshot with
            {
                Archives = archives, Accounts = accounts, ArchiveId = archiveId, AccountId = accountId,
                ArchiveAccountIds = archiveAccountIds
            })).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            GachaReadOnlyPage? result = await GetPageAsync(scopeIds, accounts, archiveAccountIds is not null,
                filter, view, calendar, page, token).ConfigureAwait(false);
            await ApplyAsync(version, token, () =>
            {
                if (archiveId is Guid a && accountId is Guid b) rememberedAccounts[a] = b;
                Set(new GachaReadOnlySnapshot
                {
                    Archives = archives, Accounts = accounts, ArchiveId = archiveId, AccountId = accountId,
                    ArchiveAccountIds = archiveAccountIds, Filter = filter, View = view, Calendar = calendar,
                    Rows = result?.Rows ?? [], TotalCount = result?.TotalCount ?? 0,
                    VisibleMonth = result?.VisibleMonth ?? (view == GachaReadOnlyView.Calendar ? calendar.Month : null),
                    PageNumber = result?.PageNumber ?? 1, TotalPages = result?.TotalPages ?? 1,
                    Status = (scopeShrank ? "原范围包含已失效账号，已移除这些账号。" :
                        fallback ? "原选择已不存在，已回退到首个可用范围。" : "") +
                        (archiveId is null ? "尚未创建本地档案" : accountId is null ? "此档案还没有游戏账号" :
                        scopeIds.Length == 0 ? "请为此档案范围选择账号" :
                        result?.TotalCount == 0 ? (view == GachaReadOnlyView.Records && archiveAccountIds is null && filter == new GachaReadOnlyFilter()
                            ? "尚未记录此账号的抽卡数据" : "当前范围与筛选条件下暂无数据") : "本地读取完成；图片缺失时显示占位，不需要联网")
                });
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            // Platform exceptions can contain paths or source contents. Keep the UI message bounded.
            await ApplyAsync(version, token, () => Set(Snapshot with
            {
                Rows = [], IsBusy = false, Status = "本地记录载入失败；请重新载入。已有数据未改变。"
            })).ConfigureAwait(false);
        }
    }

    private async Task<GachaReadOnlyPage?> GetPageAsync(Guid[] scopeIds, IReadOnlyList<GachaScopeOption> accounts,
        bool archiveScope, GachaReadOnlyFilter filter, GachaReadOnlyView view, GachaCalendarFilter calendar,
        int page, CancellationToken token)
    {
        if (scopeIds.Length == 0) return null;
        if (source is not IGachaScopedReadOnlySource scoped)
            return await source.GetPageAsync(scopeIds[0], page, PageSize, token).ConfigureAwait(false);

        if (view != GachaReadOnlyView.Records)
        {
            var analysisQuery = view == GachaReadOnlyView.Calendar
                ? new GachaRecordQuery(scopeIds, StartTime: calendar.Start, EndTime: calendar.End)
                : new GachaRecordQuery(scopeIds);
            return await scoped.GetAnalysisAsync(analysisQuery, view, accounts, page, PageSize, calendar,
                token).ConfigureAwait(false);
        }

        DateTimeOffset? start = filter.Day is DateOnly day ? new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), filter.DayOffset) : null;
        var query = new GachaRecordQuery(scopeIds,
            RankTypes: filter.Rank is int rank ? new HashSet<int> { rank } : null,
            PoolGroups: filter.Pool is { } pool ? new HashSet<GachaPoolGroup> { pool } : null,
            StartTime: start, EndTime: start?.AddDays(1).AddTicks(-1),
            SortOrder: filter.SortOrder);
        IReadOnlyDictionary<Guid, string> labels = archiveScope
            ? accounts.ToDictionary(account => account.Id, account => account.Label)
            : new Dictionary<Guid, string>();
        return await scoped.GetPageAsync(query, page, PageSize, labels, token).ConfigureAwait(false);
    }

    private Task ApplyAsync(long version, CancellationToken token, Action action) =>
        token.IsCancellationRequested ? Task.CompletedTask : dispatcher.InvokeAsync(() =>
        {
            if (active && generation == version && !token.IsCancellationRequested) action();
        });

    private void Set(GachaReadOnlySnapshot value)
    {
        Snapshot = value;
        PropertyChanged?.Invoke(this, new(nameof(Snapshot)));
    }
}

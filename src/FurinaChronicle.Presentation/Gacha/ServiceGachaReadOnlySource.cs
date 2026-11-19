// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Analytics;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Presentation.Gacha;

public sealed class ServiceGachaReadOnlySource(
    GetPlayerArchives archives,
    GetGameAccounts accounts,
    GetGachaRecordPage pages,
    Func<CancellationToken, Task> initialize,
    Func<string?, string?> cachedIconPath,
    BuildGachaAnalytics? analytics = null,
    Func<GachaFactReference, CancellationToken, Task<IReadOnlyList<GachaRevisionItem>>>? readRevisions = null,
    IGachaEventCatalog? eventCatalog = null) : IGachaScopedReadOnlySource
{
    public async Task<IReadOnlyList<GachaScopeOption>> GetArchivesAsync(CancellationToken cancellationToken)
    {
        await initialize(cancellationToken).ConfigureAwait(false);
        return (await archives.ExecuteAsync(cancellationToken).ConfigureAwait(false))
            .OrderBy(a => a.Name, StringComparer.Ordinal).Select(a => new GachaScopeOption(a.Id, a.Name)).ToArray();
    }

    public async Task<IReadOnlyList<GachaScopeOption>> GetAccountsAsync(Guid archiveId, CancellationToken cancellationToken) =>
        (await accounts.ExecuteAsync(archiveId, cancellationToken).ConfigureAwait(false))
            .OrderBy(a => a.DisplayName, StringComparer.Ordinal)
            .Select(a => new GachaScopeOption(a.Id, $"{a.DisplayName ?? "游戏账号"} · {a.Uid} · {a.ServerRegion}")
            {
                Region = a.ServerRegion
            }).ToArray();

    public async Task<GachaReadOnlyPage> GetPageAsync(Guid accountId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        GachaRecordPage page = await pages.ExecuteAsync(accountId, pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        return new(page.Records.Select(ToRow).ToArray(), page.TotalCount, page.PageNumber, page.TotalPages);
    }

    public async Task<GachaReadOnlyPage> GetPageAsync(GachaRecordQuery query, int pageNumber, int pageSize,
        IReadOnlyDictionary<Guid, string> accountLabels, CancellationToken cancellationToken)
    {
        GachaRecordPage page = await pages.ExecuteAsync(query, pageNumber, pageSize, cancellationToken).ConfigureAwait(false);
        return new(page.Records.Select(record => ToRow(record) with
        {
            AccountLabel = accountLabels.GetValueOrDefault(record.GameAccountId)
        }).ToArray(), page.TotalCount, page.PageNumber, page.TotalPages);
    }

    public async Task<GachaReadOnlyPage> GetAnalysisAsync(GachaRecordQuery query, GachaReadOnlyView view,
        IReadOnlyList<GachaScopeOption> accounts, int pageNumber, int pageSize, GachaCalendarFilter calendar,
        bool archiveScope, CancellationToken cancellationToken)
    {
        GachaAnalyticsComponents component = view switch
        {
            GachaReadOnlyView.Overview => GachaAnalyticsComponents.Pools,
            GachaReadOnlyView.History => GachaAnalyticsComponents.History,
            GachaReadOnlyView.Calendar => GachaAnalyticsComponents.Calendar,
            GachaReadOnlyView.Items => GachaAnalyticsComponents.Items,
            _ => throw new ArgumentOutOfRangeException(nameof(view))
        };
        var regions = accounts.Where(account => account.Region.HasValue)
            .ToDictionary(account => account.Id, account => account.Region!.Value);
        GachaAnalyticsReport report = await (analytics ?? throw new InvalidOperationException("Analytics is unavailable."))
            .ExecuteAsync(query, component, regions, cancellationToken, calendar.Offset).ConfigureAwait(false);
        if (view == GachaReadOnlyView.Calendar)
        {
            DateOnly date = calendar.Month ?? report.Calendar.LastOrDefault()?.Date ??
                DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(calendar.Offset).Date);
            var visibleMonth = new DateOnly(date.Year, calendar.YearView ? 1 : date.Month, 1);
            GachaCalendarDay[] visibleDays = report.Calendar
                .Where(day => day.Date.Year == date.Year && (calendar.YearView || day.Date.Month == date.Month)).ToArray();
            int maximum = visibleDays.Select(day => day.TotalPulls).DefaultIfEmpty(0).Max();
            GachaReadOnlyRow[] days = visibleDays
                .Select(day => AnalysisRow($"{day.Date:yyyy-MM-dd}",
                    $"{day.TotalPulls} 抽 · 五星 {day.FiveStarCount} · 四星 {day.FourStarCount}", "查看该日抽卡记录。")
                    with
                    {
                        CalendarDay = day.Date, CalendarPulls = day.TotalPulls,
                        CalendarFiveStarCount = day.FiveStarCount, CalendarFourStarCount = day.FourStarCount,
                        CalendarIntensity = maximum == 0 ? 0 : Math.Clamp((int)Math.Ceiling(day.TotalPulls * 5D / maximum), 1, 5),
                    }).ToArray();
            return new(days, days.Length, 1, 1) { VisibleMonth = visibleMonth };
        }
        if (view == GachaReadOnlyView.History)
        {
            return await GetHistoryPageAsync(report, query, accounts, cancellationToken).ConfigureAwait(false);
        }

        var labels = accounts.ToDictionary(account => account.Id, account => account.Label);
        int count = view switch
        {
            GachaReadOnlyView.Overview => report.Pools.Count,
            _ => report.Items.Count
        };
        int totalPages = Math.Max(1, (count + pageSize - 1) / pageSize);
        int normalizedPage = Math.Min(pageNumber, totalPages);
        int offset = (normalizedPage - 1) * pageSize;
        IEnumerable<GachaReadOnlyRow> rows = view switch
        {
            GachaReadOnlyView.Overview => report.Pools.Skip(offset).Take(pageSize).Select(pool =>
                AnalysisRow(pool.PoolName, $"本地 {pool.TotalPulls} 抽 · 五星 {pool.FiveStarCount} 次",
                    $"记录范围：{FormatTime(pool.StartTime)} ～ {FormatTime(pool.EndTime)}\n" +
                    $"星级分布：五星 {pool.FiveStarCount} · 四星 {pool.FourStarCount} · 三星 {pool.ThreeStarCount}\n" +
                    $"平均五星抽数：{FormatAverage(pool.AverageFiveStarPulls)} · UP 平均：{FormatAverage(pool.AverageUpFiveStarPulls)}\n" +
                    $"五星间隔：{pool.MinimumFiveStarPulls?.ToString() ?? "未知"} ～ {pool.MaximumFiveStarPulls?.ToString() ?? "未知"}\n" +
                    (query.GameAccountIds.Count == 1 && pool.FiveStarCount > 0
                        ? $"最近一次五星后有 {pool.PullsSinceLastFiveStar} 条本地抽取。\n" : "") +
                    "平均值采用现有统计服务认可的同账号间隔。\n最近五星：\n" +
                    string.Join("\n", pool.FiveStarHistory.Take(10).Select(item =>
                        $"{item.ItemName} · {FormatTime(item.Time)} · {labels.GetValueOrDefault(item.GameAccountId)}")))
                with { Overview = new GachaOverviewCard(pool, archiveScope, cachedIconPath) }),
            _ => report.Items.Skip(offset).Take(pageSize).Select(item =>
            {
                var card = new GachaItemCard(item, archiveScope, cachedIconPath);
                return card.Icon with
                {
                    Item = card,
                    DetailText = "抽取次数仅来自抽卡记录。" + (archiveScope || item.RankType is not (4 or 5) ? "" :
                        "\n账号模式可在「列表与时间」中查看获取时间；四星需手动展开。"),
                };
            })
        };
        return new(rows.ToArray(), count, normalizedPage, totalPages);
    }

    private async Task<GachaReadOnlyPage> GetHistoryPageAsync(
        GachaAnalyticsReport report, GachaRecordQuery query, IReadOnlyList<GachaScopeOption> accounts,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<GachaEventPeriod> catalog = eventCatalog is null
            ? report.History.Select(item => item.EventPeriod).OfType<GachaEventPeriod>().ToArray()
            : await eventCatalog.GetAllAsync(GachaGame.GenshinImpact, cancellationToken).ConfigureAwait(false);
        var history = report.History.Where(item => item.EventPeriod is not null)
            .ToDictionary(item => item.EventPeriod!.Id, StringComparer.Ordinal);
        var scope = accounts.Where(account => query.GameAccountIds.Contains(account.Id))
            .Select(account => account.Region ?? FurinaChronicle.Core.Archives.GameServerRegion.Unknown).ToArray();
        DateTimeOffset now = DateTimeOffset.Now;
        GachaReadOnlyRow[] rows = catalog
            .Where(period => period.PoolGroup is GachaPoolGroup.CharacterEvent or GachaPoolGroup.WeaponEvent or GachaPoolGroup.Chronicled)
            .Where(period => period.StartsAt <= now && scope.Any(period.Supports))
            .DistinctBy(period => period.Id, StringComparer.Ordinal)
            .OrderByDescending(period => period.Contains(now))
            .ThenByDescending(period => period.StartsAt)
            .ThenBy(period => period.PoolGroup)
            .ThenBy(period => period.Id, StringComparer.Ordinal)
            .Select(period =>
            {
                history.TryGetValue(period.Id, out GachaHistoryPeriod? obtained);
                var card = new GachaHistoryCard(period, obtained, cachedIconPath);
                return AnalysisRow(card.Title, $"{card.Version} · {card.Total}", card.ItemsText)
                    with { History = card, Banners = card.Banners };
            }).ToArray();
        return new GachaReadOnlyPage(rows, rows.Length, 1, 1);
    }

    private GachaReadOnlyRow AnalysisRow(string name, string summary, string detail, string? itemId = null, int? rank = null, string? iconUrl = null) =>
        new("", name, rank is int stars ? $"{stars} 星" : "", "", "", "", new(itemId, cachedIconPath(itemId), iconUrl))
        {
            Subtitle = summary,
            DetailText = detail,
            Rarity = rank,
        };

    private static string FormatTime(DateTimeOffset? time) => time?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "未知";
    private static string FormatAverage(double? value) => value?.ToString("0.0") ?? "未知";

    public async Task<string> GetDetailAsync(GachaReadOnlyRow row, CancellationToken cancellationToken)
    {
        string detail = row.Detail + "\n\n" + row.DetailText;
        if (row.Reference is not GachaFactReference reference || readRevisions is null) return detail;
        IReadOnlyList<GachaRevisionItem> revisions = await readRevisions(reference, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (revisions.Count == 0) return detail + "\n\n无本地修订。";
        return detail + "\n\n本地修订\n" + string.Join("\n\n", revisions.Reverse().Select(revision =>
            $"{FormatTime(revision.CreatedAt)} · {revision.ChangeKind switch
            {
                EntityChangeKind.Insert => "新增", EntityChangeKind.Update => "更新",
                EntityChangeKind.Delete => "删除", _ => "未知"
            }}\n变更前：{RevisionRecord(revision.Before)}\n变更后：{RevisionRecord(revision.After)}"));
    }

    private static string RevisionRecord(GachaRecord? record) => record is null ? "无" :
        $"{record.ItemName ?? "物品未知"}（{record.ItemId ?? "ID 未知"}） · {record.ItemType ?? "类型未知"} · " +
        $"{record.RankType?.ToString() ?? "未知"} 星 · {FormatPool(record)} · {FormatTime(record.Time)} · {FormatOrigin(record.Provenance.Origin)}";

    private GachaReadOnlyRow ToRow(GachaRecord record) => new(
        record.ExternalRecordId,
        string.IsNullOrWhiteSpace(record.ItemName) ? $"物品 {record.ItemId ?? "未知"}" : record.ItemName,
        record.RankType is int rank ? $"{rank} 星" : "星级未知",
        record.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"),
        FormatPool(record),
        FormatOrigin(record.Provenance.Origin),
        new(record.ItemId, cachedIconPath(record.ItemId)))
        {
            Reference = new(record.GameAccountId, record.ExternalRecordId),
            ItemType = record.ItemType,
            Rarity = record.RankType,
            DetailText = $"记录 ID：{record.ExternalRecordId}\n物品 ID：{record.ItemId ?? "未知"}\n物品类型：{record.ItemType ?? "未知"}"
        };

    private static string FormatPool(GachaRecord record) => GachaPoolGroupResolver.GetDisplayName(GachaPoolGroupResolver.Resolve(record));

    private static string FormatOrigin(DataOrigin origin) => origin switch
    {
        DataOrigin.OfficialApi => "官方接口", DataOrigin.StandardImport => "标准导入",
        DataOrigin.FurinaImport => "Furina 导入", DataOrigin.LocalObservation => "本地观察",
        DataOrigin.LocalCollector => "本地采集", DataOrigin.UserEntered => "用户输入",
        DataOrigin.Derived => "派生结果", _ => "来源未知"
    };
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
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
    Func<GachaFactReference, CancellationToken, Task<IReadOnlyList<GachaRevisionItem>>>? readRevisions = null) : IGachaScopedReadOnlySource
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
        CancellationToken cancellationToken)
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
            var visibleMonth = new DateOnly(date.Year, date.Month, 1);
            GachaReadOnlyRow[] days = report.Calendar
                .Where(day => day.Date.Year == date.Year && day.Date.Month == date.Month)
                .Select(day => AnalysisRow($"{day.Date:yyyy-MM-dd}",
                    $"{day.TotalPulls} 抽 · 五星 {day.FiveStarCount} · 四星 {day.FourStarCount}", "查看该日抽卡记录。")
                    with { CalendarDay = day.Date, CalendarPulls = day.TotalPulls }).ToArray();
            return new(days, days.Length, 1, 1) { VisibleMonth = visibleMonth };
        }
        var labels = accounts.ToDictionary(account => account.Id, account => account.Label);
        int count = view switch
        {
            GachaReadOnlyView.Overview => report.Pools.Count,
            GachaReadOnlyView.History => report.History.Count,
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
                        $"{item.ItemName} · {FormatTime(item.Time)} · {labels.GetValueOrDefault(item.GameAccountId)}")))),
            GachaReadOnlyView.History => report.History.Skip(offset).Take(pageSize).Select(period =>
                AnalysisRow(period.EventPeriod?.Banners.FirstOrDefault()?.Name ?? period.PoolName,
                    $"{FormatTime(period.StartTime)} ～ {FormatTime(period.EndTime)} · {period.TotalPulls} 抽",
                    (period.MatchQuality switch
                    {
                        GachaEventMatchQuality.Verified => "已匹配卡池",
                        GachaEventMatchQuality.RegionUnverified => "区服未核实",
                        _ => "未匹配卡池活动"
                    }) + $"\n版本：{period.EventPeriod?.Version ?? "未知"} · 元数据：{period.EventPeriod?.SourceRevision ?? "未知"}\n" +
                    string.Join("\n", period.Items.Select(item => $"{item.ItemName} × {item.Count}")))),
            _ => report.Items.Skip(offset).Take(pageSize).Select(item =>
                AnalysisRow(item.ItemName, $"本地抽取 {item.Count} 次 · {item.ItemType ?? "类型未知"}",
                    "抽取次数仅来自抽卡记录。\n记录时间：\n" + string.Join("\n", item.AcquisitionTimes.Take(50).Select(time => FormatTime(time))) +
                    (item.AcquisitionTimes.Count > 50 ? "\n仅显示最近 50 条记录时间。" : ""), item.ItemId, item.RankType))
        };
        return new(rows.ToArray(), count, normalizedPage, totalPages);
    }

    private GachaReadOnlyRow AnalysisRow(string name, string summary, string detail, string? itemId = null, int? rank = null) =>
        new("", name, rank is int stars ? $"{stars} 星" : "", "", "", "", new(itemId, cachedIconPath(itemId)))
        {
            Subtitle = summary,
            DetailText = detail
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

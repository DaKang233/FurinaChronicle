// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Presentation.Management;

public sealed record OverviewArchiveSummary(
    Guid ArchiveId,
    string Name,
    int AccountCount,
    int RecordCount,
    string LastActivity);

public sealed record OverviewRecentActivity(
    string ArchiveName,
    string AccountLabel,
    string ItemName,
    string Rank,
    string Time,
    DateTimeOffset OccurredAt);

public sealed record OverviewSnapshot
{
    public IReadOnlyList<OverviewArchiveSummary> Archives { get; init; } = [];
    public IReadOnlyList<OverviewRecentActivity> RecentActivities { get; init; } = [];
    public IReadOnlyList<string> CoverageNotes { get; init; } = [];
    public int ArchiveCount { get; init; }
    public int AccountCount { get; init; }
    public int UnresolvedCount { get; init; }
    public int TotalGachaRecords { get; init; }
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入";
    public string? Error { get; init; }
    public string ContinueTargetLabel { get; init; } = "打开数据 · 抽卡";
}

public interface IOverviewSource
{
    Task<OverviewSnapshot> LoadAsync(CancellationToken cancellationToken = default);
}

// Lightweight local aggregation for the overview page. It reuses the existing archive/account and
// paged gacha queries; it does not build a Chronicle projection or duplicate any domain logic.
public sealed class ServiceOverviewSource(
    GetPlayerArchives getArchives,
    GetGameAccounts getAccounts,
    GetUnresolvedGameAccounts getUnresolved,
    GetGachaRecordPage getGachaPage) : IOverviewSource
{
    public async Task<OverviewSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PlayerArchive> archives = await getArchives.ExecuteAsync(cancellationToken);
        IReadOnlyList<UnresolvedGameAccountDiagnostic> unresolved =
            await getUnresolved.ExecuteAsync(cancellationToken);
        var archiveSummaries = new List<OverviewArchiveSummary>(archives.Count);
        var activities = new List<OverviewRecentActivity>();
        int accountCount = 0;
        int totalRecords = 0;
        foreach (PlayerArchive archive in archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<GameAccount> accounts =
                await getAccounts.ExecuteAsync(archive.Id, cancellationToken);
            accountCount += accounts.Count;
            int recordCount = 0;
            string lastActivity = accounts.Count == 0 ? "暂无游戏账号" : "暂无本地抽卡记录";
            if (accounts.Count > 0)
            {
                var labels = accounts.ToDictionary(
                    account => account.Id,
                    account => ManagementTexts.AccountLabel(account));
                GachaRecordPage page = await getGachaPage.ExecuteAsync(
                    new GachaRecordQuery(accounts.Select(account => account.Id).ToArray(), Limit: 3),
                    1,
                    3,
                    cancellationToken);
                recordCount = page.TotalCount;
                totalRecords += recordCount;
                if (page.Records.Count > 0)
                {
                    GachaRecord latest = page.Records[0];
                    lastActivity = $"{latest.ItemName ?? "物品未知"} · " +
                        $"{latest.Time:yyyy-MM-dd HH:mm}";
                    activities.AddRange(page.Records.Select(record => new OverviewRecentActivity(
                        archive.Name,
                        labels.GetValueOrDefault(record.GameAccountId, "游戏账号"),
                        record.ItemName ?? "物品未知",
                        record.RankType is int rank ? $"{rank} 星" : "星级未知",
                        record.Time.ToString("yyyy-MM-dd HH:mm"),
                        record.Time)));
                }
            }

            archiveSummaries.Add(new OverviewArchiveSummary(
                archive.Id,
                archive.Name,
                accounts.Count,
                recordCount,
                lastActivity));
        }

        IReadOnlyList<OverviewRecentActivity> recent = activities
            .OrderByDescending(activity => activity.OccurredAt)
            .Take(5)
            .ToArray();
        var notes = new List<string>
        {
            $"抽卡记录：{totalRecords} 条（隔离合成库）",
            $"档案与账号：{archives.Count} 个档案 / {accountCount} 个账号副本",
            unresolved.Count == 0
                ? "身份待确认账号：无"
                : $"身份待确认账号：{unresolved.Count} 个（前往档案与账号页处理）",
            "游戏历程、角色与武器、周期挑战：待 UI-5 交付，本页不提供伪入口"
        };
        string status = archives.Count == 0
            ? "本地隔离库尚未创建档案。请前往档案与账号页创建。"
            : "已载入本地聚合；只使用隔离库查询，不重建 Chronicle 投影。";
        return new OverviewSnapshot
        {
            Archives = archiveSummaries,
            RecentActivities = recent,
            CoverageNotes = notes,
            ArchiveCount = archives.Count,
            AccountCount = accountCount,
            UnresolvedCount = unresolved.Count,
            TotalGachaRecords = totalRecords,
            Status = status,
            ContinueTargetLabel = recent.Count > 0
                ? $"继续查看：{recent[0].ArchiveName} · {recent[0].AccountLabel}"
                : "打开数据 · 抽卡"
        };
    }
}

public sealed class OverviewState(IOverviewSource source)
{
    public OverviewSnapshot Snapshot { get; private set; } = new();

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Snapshot = Snapshot with { IsBusy = true, Status = "正在汇总本地隔离库…", Error = null };
        try
        {
            Snapshot = await source.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            Snapshot = Snapshot with
            {
                IsBusy = false,
                Status = "概览读取失败；未改变本地数据。",
                Error = "无法读取本地聚合，请重试。"
            };
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

// Lightweight local aggregation for the Android overview destination. It reuses the existing
// archive/account and paged gacha queries, builds no Chronicle projection and writes nothing.
public sealed record OverviewArchiveCard(
    Guid ArchiveId,
    string Name,
    int AccountCount,
    int RecordCount,
    string LastActivity)
{
    public string Summary => $"账号 {AccountCount} · 抽卡记录 {RecordCount} 条";
}

public sealed record OverviewActivityCard(
    string ItemName,
    string Context,
    string Time);

public partial class OverviewPageViewModel(
    GetPlayerArchives getPlayerArchives,
    GetGameAccounts getGameAccounts,
    GetUnresolvedGameAccounts getUnresolvedGameAccounts,
    GetGachaRecordPage getGachaRecordPage)
    : ObservableObject
{
    private CancellationTokenSource? activeLoad;

    public ObservableCollection<OverviewArchiveCard> Archives { get; } = [];

    public ObservableCollection<OverviewActivityCard> RecentActivities { get; } = [];

    public ObservableCollection<string> CoverageNotes { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "尚未载入本地档案。";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasArchives))]
    public partial int ArchiveCount { get; set; }

    public bool HasArchives => ArchiveCount > 0;

    public bool IsEmpty => !IsBusy && !HasError && ArchiveCount == 0;

    [ObservableProperty]
    public partial int AccountCount { get; set; }

    [ObservableProperty]
    public partial int TotalGachaRecords { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIdentityPending))]
    public partial string? IdentityPendingNote { get; set; }

    public bool HasIdentityPending => !string.IsNullOrWhiteSpace(IdentityPendingNote);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecentActivities))]
    public partial string RecentActivityEmptyText { get; set; } =
        "尚未载入最近活动。";

    public bool HasRecentActivities => RecentActivities.Count > 0;

    public bool HasNoRecentActivities => RecentActivities.Count == 0;

    public Task InitializeAsync() => ReloadAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ReloadAsync()
    {
        activeLoad?.Cancel();
        activeLoad?.Dispose();
        var cancellation = new CancellationTokenSource();
        activeLoad = cancellation;
        CancellationToken token = cancellation.Token;

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = "正在汇总本地档案与记录…";
        try
        {
            Archives.Clear();
            RecentActivities.Clear();
            CoverageNotes.Clear();

            IReadOnlyList<PlayerArchive> archives =
                await getPlayerArchives.ExecuteAsync(token);
            IReadOnlyList<UnresolvedGameAccountDiagnostic> unresolved =
                await getUnresolvedGameAccounts.ExecuteAsync(token);
            var activities = new List<(OverviewActivityCard Card, DateTimeOffset OccurredAt)>();
            int accountCount = 0;
            int totalRecords = 0;
            foreach (PlayerArchive archive in archives)
            {
                token.ThrowIfCancellationRequested();
                IReadOnlyList<GameAccount> accounts =
                    await getGameAccounts.ExecuteAsync(archive.Id, token);
                accountCount += accounts.Count;
                int recordCount = 0;
                string lastActivity = accounts.Count == 0
                    ? "暂无游戏账号"
                    : "暂无本地抽卡记录";
                if (accounts.Count > 0)
                {
                    Dictionary<Guid, string> labels = accounts.ToDictionary(
                        account => account.Id,
                        ManagementTexts.AccountLabel);
                    GachaRecordPage page = await getGachaRecordPage.ExecuteAsync(
                        new GachaRecordQuery(
                            accounts.Select(account => account.Id).ToArray(),
                            Limit: 3),
                        1,
                        3,
                        token);
                    recordCount = page.TotalCount;
                    totalRecords += recordCount;
                    if (page.Records.Count > 0)
                    {
                        GachaRecord latest = page.Records[0];
                        lastActivity =
                            $"{latest.ItemName ?? "物品未知"} · " +
                            $"{latest.Time:yyyy-MM-dd HH:mm}";
                        activities.AddRange(page.Records.Select(
                            record => (
                                new OverviewActivityCard(
                                    record.ItemName ?? "物品未知",
                                    $"{archive.Name} · " +
                                        labels.GetValueOrDefault(
                                            record.GameAccountId,
                                            "游戏账号") +
                                        " · " +
                                        (record.RankType is int rank
                                            ? $"{rank} 星"
                                            : "星级未知"),
                                    record.Time.ToString("yyyy-MM-dd HH:mm")),
                                record.Time)));
                    }
                }

                Archives.Add(new OverviewArchiveCard(
                    archive.Id,
                    archive.Name,
                    accounts.Count,
                    recordCount,
                    lastActivity));
            }

            foreach ((OverviewActivityCard card, DateTimeOffset occurredAt) in activities
                .OrderByDescending(item => item.OccurredAt)
                .Take(5))
            {
                RecentActivities.Add(card);
            }

            OnPropertyChanged(nameof(HasRecentActivities));
            OnPropertyChanged(nameof(HasNoRecentActivities));
            ArchiveCount = archives.Count;
            AccountCount = accountCount;
            TotalGachaRecords = totalRecords;
            IdentityPendingNote = unresolved.Count == 0
                ? null
                : $"身份待确认账号：{unresolved.Count} 个；请前往“档案”页处理。";
            RecentActivityEmptyText = archives.Count == 0
                ? "本地还没有档案；创建或导入档案后这里会显示最近活动。"
                : "已载入档案，但没有可显示的本地抽卡记录。";

            CoverageNotes.Add(
                $"抽卡记录：{totalRecords} 条（来自本地档案，不重建 Chronicle 投影）");
            CoverageNotes.Add(
                $"档案与账号：{archives.Count} 个档案 / {accountCount} 个账号副本");
            CoverageNotes.Add(
                "游戏历程：从底部“游戏历程”进入，按时间回顾领域事件。");
            CoverageNotes.Add(
                "角色与武器、周期挑战：从底部“数据”进入。");

            StatusMessage = archives.Count == 0
                ? "本地尚未创建档案；请前往“档案”页创建或导入。"
                : "已载入本地聚合；本页只读，不修改任何业务数据。";
        }
        catch (OperationCanceledException)
        {
            // A newer load replaced this one.
        }
        catch (Exception exception)
        {
            ErrorMessage = ManagementTexts.DescribeOperationFailure(exception);
            StatusMessage = "概览读取失败；本地数据未被修改。";
        }
        finally
        {
            if (ReferenceEquals(activeLoad, cancellation))
            {
                IsBusy = false;
            }
        }
    }

    public void CancelActiveLoad()
    {
        activeLoad?.Cancel();
    }

    private bool CanRun() => !IsBusy;
}

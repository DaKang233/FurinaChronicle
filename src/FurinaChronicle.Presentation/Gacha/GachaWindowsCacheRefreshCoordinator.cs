// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// Windows web-cache refresh seam. The provider discovers a temporary gacha URL from the local
// game cache; the URL is never surfaced by the presentation layer or written anywhere.
public interface IGachaWindowsCacheRefreshGateway
{
    bool IsSupported { get; }

    Task<GachaRefreshIdentity> DiscoverIdentityAsync(
        string gameInstallationPath,
        GameServerRegion regionHint,
        CancellationToken cancellationToken = default);

    Task<GachaRefreshResult> RefreshAsync(
        GameAccount targetAccount,
        GachaImportMode mode,
        string gameInstallationPath,
        CancellationToken cancellationToken = default);
}

public sealed record GachaWindowsCacheRefreshPlan(
    Guid ArchiveId,
    string ArchiveName,
    GameAccount TargetAccount,
    GachaImportMode Mode,
    string GameInstallationPath,
    string ConfirmationText);

public sealed record GachaWindowsCacheRefreshPlanResult(
    GachaWindowsCacheRefreshPlan? Plan,
    string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}

public sealed record GachaWindowsCacheRefreshOutcome(
    bool Succeeded,
    GachaRefreshResult? Result,
    string Message);

// Plans and performs a Windows web-cache refresh against the frozen archive/account target and
// the shared full-import mode. Missing cache, missing URL, invalid URL and stale targets are all
// rejected before any persistence happens.
public sealed class GachaWindowsCacheRefreshCoordinator(
    IGachaImportPlanningSource planningSource,
    IGachaWindowsCacheRefreshGateway gateway)
{
    public async Task<GachaWindowsCacheRefreshPlanResult> PlanAsync(
        string? gameInstallationPath,
        Guid archiveId,
        Guid gameAccountId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!gateway.IsSupported)
        {
            return new(null, "网页缓存刷新仅在 Windows 上可用。未写入任何数据。");
        }

        if (string.IsNullOrWhiteSpace(gameInstallationPath))
        {
            return new(null, "请选择原神游戏安装目录。未写入任何数据。");
        }

        if (archiveId == Guid.Empty || gameAccountId == Guid.Empty)
        {
            return new(null, "请先选择档案和要刷新的账号。未写入任何数据。");
        }

        try
        {
            IReadOnlyList<PlayerArchive> archives =
                await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
            PlayerArchive? archive = archives.FirstOrDefault(item => item.Id == archiveId);
            if (archive is null)
            {
                return new(null, "目标档案已不存在，未写入任何数据。请重新选择。");
            }

            IReadOnlyList<GameAccount> accounts = await planningSource
                .GetAccountsAsync(archiveId, cancellationToken)
                .ConfigureAwait(false);
            GameAccount? account = accounts.FirstOrDefault(item => item.Id == gameAccountId);
            if (account is null)
            {
                return new(null, "目标账号已不存在，未写入任何数据。请重新选择档案和账号。");
            }

            string modeLabel = mode == GachaImportMode.Full
                ? "全量导入：覆盖同记录"
                : "仅补充";
            string text =
                $"来源：Windows 游戏网页缓存（本机）" + Environment.NewLine +
                $"目标：{archive.Name} · {AccountLabel(account)}" + Environment.NewLine +
                $"模式：{modeLabel}" + Environment.NewLine +
                (mode == GachaImportMode.Full
                    ? "覆盖说明：只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
                    : "覆盖说明：仅补充模式不会覆盖不同的既有事实；不删除来源缺失的本地记录。") +
                Environment.NewLine +
                "说明：此操作读取本机游戏网页缓存中的临时抽卡链接，链接不显示、不保存、不写入日志。" +
                Environment.NewLine +
                "其他说明：请求会访问米哈游抽卡接口；命中本机不可逆删除的记录会被抑制，不会静默恢复；成功后重查当前范围。";
            return new(
                new GachaWindowsCacheRefreshPlan(
                    archive.Id,
                    archive.Name,
                    account,
                    mode,
                    gameInstallationPath.Trim(),
                    text),
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, "无法读取本地档案与账号范围，未发起刷新。请稍后重试。");
        }
    }

    public async Task<GachaWindowsCacheRefreshOutcome> ExecuteAsync(
        GachaWindowsCacheRefreshPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (string.IsNullOrWhiteSpace(plan.GameInstallationPath))
        {
            return new(false, null, "请选择原神游戏安装目录。未写入任何数据。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            IReadOnlyList<PlayerArchive> archives =
                await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
            PlayerArchive? archive = archives.FirstOrDefault(item => item.Id == plan.ArchiveId);
            IReadOnlyList<GameAccount> accounts = archive is null
                ? []
                : await planningSource.GetAccountsAsync(plan.ArchiveId, cancellationToken)
                    .ConfigureAwait(false);
            GameAccount? account = accounts.FirstOrDefault(
                item => item.Id == plan.TargetAccount.Id);
            if (archive is null || account is null ||
                account.PlayerArchiveId != plan.ArchiveId ||
                !string.Equals(account.Uid, plan.TargetAccount.Uid, StringComparison.Ordinal))
            {
                return new(
                    false,
                    null,
                    "目标账号或档案已在预览后变化，未写入任何数据。请重新选择目标并重试。");
            }

            GachaRefreshIdentity identity = await gateway.DiscoverIdentityAsync(
                plan.GameInstallationPath,
                account.ServerRegion,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(identity.Uid, account.Uid, StringComparison.Ordinal))
            {
                return new(
                    false,
                    null,
                    $"网页缓存返回的 UID {identity.Uid} 与目标账号 UID {account.Uid} 不一致，" +
                    "未写入任何数据。请确认游戏账号与所选档案账号一致。");
            }

            GachaRefreshResult result = await gateway.RefreshAsync(
                account,
                plan.Mode,
                plan.GameInstallationPath,
                cancellationToken).ConfigureAwait(false);
            string message =
                $"网页缓存刷新完成：获取 {result.FetchedCount} 条，新增 {result.InsertedCount} 条，" +
                $"覆盖校正 {result.UpdatedCount} 条，保留重复 {result.DuplicateCount} 条，" +
                $"永久删除抑制 {result.SuppressedCount} 条，请求 {result.PageCount} 页。";
            return new(true, result, message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, null, DescribeFailure(exception));
        }
    }

    private static string DescribeFailure(Exception exception) => exception switch
    {
        PlatformNotSupportedException =>
            "网页缓存刷新仅在 Windows 上可用。未写入任何数据。",
        FileNotFoundException or DirectoryNotFoundException =>
            "未在所选目录找到原神网页缓存文件（data_2）。请选择包含“YuanShen_Data”或" +
            "“GenshinImpact_Data”的游戏安装目录，或先在游戏内打开抽卡历史后重试。未写入任何数据。",
        InvalidDataException =>
            "网页缓存中没有找到可用的抽卡链接。请先在游戏内打开抽卡历史，再重试。未写入任何数据。",
        FormatException =>
            "网页缓存中的抽卡链接无效或缺少 authkey，未写入任何数据。请重新打开游戏抽卡历史后重试。",
        UnauthorizedAccessException or IOException =>
            "无法读取游戏网页缓存，未写入任何数据。请关闭游戏或确认缓存文件可访问后重试。",
        _ => "网页缓存刷新未完成；未确认任何记录被修改，请重试。"
    };

    private static string AccountLabel(GameAccount account) =>
        $"{account.DisplayName ?? "游戏账号"} · UID {account.Uid} · " +
        GachaImportText.RegionLabel(account.ServerRegion);
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Gacha;

// The two network steps needed by a manual-URL refresh. The URL itself is passed through and is
// never stored, logged or echoed by the presentation layer.
public interface IGachaManualRefreshGateway
{
    Task<GachaRefreshIdentity> DiscoverIdentityAsync(
        string manualUrl,
        CancellationToken cancellationToken = default);

    Task<GachaRefreshResult> RefreshAsync(
        GameAccount targetAccount,
        GachaImportMode mode,
        string manualUrl,
        CancellationToken cancellationToken = default);
}

public sealed record GachaManualRefreshPlan(
    Guid ArchiveId,
    string ArchiveName,
    GameAccount TargetAccount,
    GachaImportMode Mode,
    string ConfirmationText);

public sealed record GachaManualRefreshPlanResult(GachaManualRefreshPlan? Plan, string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}

public sealed record GachaManualRefreshOutcome(
    bool Succeeded,
    GachaRefreshResult? Result,
    string Message);

// Plans a manual-URL refresh against the frozen archive/account selection, re-validates the
// target before the network write path and never touches SToken or web-cache credentials.
public sealed class GachaManualRefreshCoordinator(
    IGachaImportPlanningSource planningSource,
    IGachaManualRefreshGateway gateway)
{
    public const string UrlInputHelp =
        "链接仅在本次请求中使用；不显示、不保存、不写入日志或异常消息。";

    public async Task<GachaManualRefreshPlanResult> PlanAsync(
        string manualUrl,
        Guid archiveId,
        Guid gameAccountId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (ValidateUrl(manualUrl) is string urlError)
        {
            return new(null, urlError);
        }

        if (archiveId == Guid.Empty || gameAccountId == Guid.Empty)
        {
            return new(null, "请先选择要刷新的档案和账号。");
        }

        IReadOnlyList<PlayerArchive> archives;
        IReadOnlyList<GameAccount> accounts;
        try
        {
            archives = await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
            PlayerArchive? archive = archives.FirstOrDefault(item => item.Id == archiveId);
            if (archive is null)
            {
                return new(null, "目标档案已不存在，请重新选择。");
            }

            accounts = await planningSource.GetAccountsAsync(archiveId, cancellationToken)
                .ConfigureAwait(false);
            GameAccount? account = accounts.FirstOrDefault(item => item.Id == gameAccountId);
            if (account is null)
            {
                return new(null, "目标账号已不存在，请重新选择档案和账号。");
            }

            string modeLabel = mode == GachaImportMode.Full
                ? "全量导入：覆盖同记录"
                : "仅补充";
            string text =
                $"目标：{archive.Name} · {AccountLabel(account)}" + Environment.NewLine +
                $"模式：{modeLabel}" + Environment.NewLine +
                (mode == GachaImportMode.Full
                    ? "覆盖说明：只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
                    : "覆盖说明：仅补充模式不会覆盖不同的既有事实；不删除来源缺失的本地记录。") +
                Environment.NewLine +
                "请求范围：角色、武器、集录、新手与常驻 5 类卡池；以现有原子变更集提交。" +
                Environment.NewLine +
                "安全说明：" + UrlInputHelp + Environment.NewLine +
                "其他说明：命中本机不可逆删除的记录会被抑制，不会静默恢复；成功后重查当前范围。";
            return new(
                new GachaManualRefreshPlan(archive.Id, archive.Name, account, mode, text),
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

    public async Task<GachaManualRefreshOutcome> ExecuteAsync(
        GachaManualRefreshPlan plan,
        string manualUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (ValidateUrl(manualUrl) is string urlError)
        {
            return new(false, null, urlError);
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
            GameAccount? account = accounts.FirstOrDefault(item => item.Id == plan.TargetAccount.Id);
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
                manualUrl,
                cancellationToken).ConfigureAwait(false);
            if (!string.Equals(identity.Uid, account.Uid, StringComparison.Ordinal))
            {
                return new(
                    false,
                    null,
                    $"链接返回的 UID {identity.Uid} 与目标账号 UID {account.Uid} 不一致，" +
                    "未写入任何数据。请确认链接属于该账号。");
            }

            GachaRefreshResult result = await gateway.RefreshAsync(
                account,
                plan.Mode,
                manualUrl,
                cancellationToken).ConfigureAwait(false);
            string message =
                $"刷新完成：获取 {result.FetchedCount} 条，新增 {result.InsertedCount} 条，" +
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

    public static string? ValidateUrl(string? manualUrl)
    {
        if (string.IsNullOrWhiteSpace(manualUrl))
        {
            return "请输入完整的抽卡链接。未写入任何数据。";
        }

        try
        {
            GachaRefreshUrl.Parse(manualUrl);
            return null;
        }
        catch (FormatException exception)
        {
            return exception.Message switch
            {
                var message when message.Contains("HTTPS", StringComparison.Ordinal) =>
                    "抽卡链接必须是以 https:// 开头的完整地址。未写入任何数据。",
                var message when message.Contains("host", StringComparison.Ordinal) =>
                    "抽卡链接的域名不受支持。请使用米哈游或 HoYoverse 的抽卡记录链接。未写入任何数据。",
                var message when message.Contains("authkey", StringComparison.Ordinal) =>
                    "抽卡链接缺少 authkey 参数，无法用于刷新。未写入任何数据。",
                _ => "抽卡链接格式无效，未写入任何数据。"
            };
        }
    }

    private static string DescribeFailure(Exception exception) => exception switch
    {
        HttpRequestException =>
            "无法访问米哈游抽卡接口；可能是网络不可用或链接已过期，未写入任何数据。",
        TaskCanceledException =>
            "抽卡接口请求超时，未写入任何数据。请检查网络后重试。",
        InvalidDataException =>
            "抽卡接口返回的数据无法识别，未写入任何数据。请确认链接来自原神抽卡记录。",
        _ => "手动链接刷新未完成；未确认任何记录被修改，请重试。"
    };

    private static string AccountLabel(GameAccount account) =>
        $"{account.DisplayName ?? "游戏账号"} · UID {account.Uid} · " +
        GachaImportText.RegionLabel(account.ServerRegion);
}

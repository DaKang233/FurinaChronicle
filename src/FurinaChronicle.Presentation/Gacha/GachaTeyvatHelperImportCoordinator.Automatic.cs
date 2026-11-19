// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

public interface IGachaTeyvatHelperAutomaticImportGateway
{
    Task<TeyvatHelperUigfDownload> DownloadForRoleAsync(
        Guid passportAccountId, string uid, CancellationToken cancellationToken = default);
}

public sealed partial class GachaTeyvatHelperImportCoordinator
{
    public async Task<IReadOnlyList<GachaSTokenRoleChoice>> GetAutomaticRolesAsync(
        CancellationToken cancellationToken = default) =>
        credentials is null || gateway is not IGachaTeyvatHelperAutomaticImportGateway ? [] :
        GachaPassportRoleChoices.From(await credentials.ListAsync(cancellationToken).ConfigureAwait(false));

    // Pure local preview. Fresh URL generation and third-party traffic only happen in DownloadAutomaticAsync,
    // which the host calls after the user has confirmed the privacy notice.
    public async Task<GachaTeyvatHelperImportPlanResult> PlanAutomaticAsync(
        Guid passportAccountId, string uid, Guid archiveId, GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
            throw new ArgumentOutOfRangeException(nameof(mode));
        try
        {
            var role = (await GetAutomaticRolesAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.PassportAccountId == passportAccountId && item.Uid == uid);
            if (role is null) return new(null, "没有可用的已登录国服角色；可重新选择或使用手动方式。");
            PlayerArchive? archive = (await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(item => item.Id == archiveId);
            if (archive is null) return new(null, "目标档案已不存在；未发起请求。");
            string privacy = $"将为 UID {uid} 即时生成新的抽卡链接，并将 UID 和含 authkey 的链接发送给第三方 " +
                $"{ThirdPartyHost}（提瓦特小助手）以获取 UIGF。链接不显示、不保存、不写入日志。" +
                "取消本确认不会发出请求；发送后取消后续导入预览只取消本地写入，不能撤回已发送的数据。";
            return new(new(uid, archive.Id, archive.Name, mode, true, privacy,
                $"来源：提瓦特小助手自动导入\n角色：{role.Label}\n目标档案：{archive.Name}\n" +
                $"模式：{new GachaImportModeOption(mode).Label}\n{privacy}\n" +
                "下载后仍须通过统一 UIGF 预览；不删除来源缺失的记录，保留现有修订、撤销和永久删除保护。")
            { PassportAccountId = passportAccountId }, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return new(null, "无法读取本机登录角色或导入范围；未发起请求，可使用手动方式。"); }
    }

    public async Task<GachaTeyvatHelperImportOutcome> DownloadAutomaticAsync(
        GachaTeyvatHelperImportPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (plan.PassportAccountId is not Guid passportId || credentials is null ||
            gateway is not IGachaTeyvatHelperAutomaticImportGateway automatic)
            return new(false, "没有有效的自动导入上下文；未发起请求。", null, null);
        try
        {
            if (!(await GetAutomaticRolesAsync(cancellationToken).ConfigureAwait(false))
                .Any(item => item.PassportAccountId == passportId && item.Uid == plan.Uid))
                return new(false, "所选登录账号或角色已变化；未生成链接或发起第三方请求。", null, null);
            if (!(await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false))
                .Any(item => item.Id == plan.ArchiveId))
                return new(false, "目标档案已不存在；未生成链接或发起第三方请求。", null, null);
            var download = await automatic.DownloadForRoleAsync(passportId, plan.Uid, cancellationToken).ConfigureAwait(false);
            return new(true, null, download.FileName, download.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (NotSupportedException)
        { return new(false, "自动导入仅支持包含 SToken/MID 的国服通行证及国服角色；未导入本地数据。", null, null); }
        catch (Exception exception) { return new(false, DescribeFailure(exception), null, null); }
    }

    public static bool MatchesDownloadedRole(GachaImportSourceSummary summary, string uid) =>
        summary.Accounts.Count == 1 && string.Equals(summary.Accounts[0].Uid, uid, StringComparison.Ordinal);
}

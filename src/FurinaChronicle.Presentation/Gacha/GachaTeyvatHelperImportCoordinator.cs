// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

// Third-party Teyvat Helper fetch seam. Only the manual path is used by the isolated host: the
// automatic path needs a passport login/role-selection context that this host does not own.
public interface IGachaTeyvatHelperImportGateway
{
    Task<TeyvatHelperImportAvailability> GetAvailabilityAsync(
        CancellationToken cancellationToken = default);

    Task<TeyvatHelperUigfDownload> DownloadManuallyAsync(
        string uid,
        string gachaUrl,
        CancellationToken cancellationToken = default);
}

public sealed record GachaTeyvatHelperImportPlan(
    string Uid,
    Guid ArchiveId,
    string ArchiveName,
    GachaImportMode Mode,
    bool AutomaticImportAvailable,
    string PrivacyNotice,
    string ConfirmationText);

public sealed record GachaTeyvatHelperImportPlanResult(
    GachaTeyvatHelperImportPlan? Plan,
    string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}

public sealed record GachaTeyvatHelperImportOutcome(
    bool Succeeded,
    string? FailureMessage,
    string? FileName,
    byte[]? Content);

// Validates UID/link locally, freezes the import target and full-mode snapshot, renders the
// third-party privacy notice, then downloads the UIGF payload after explicit confirmation. The
// caller feeds the payload into the same unified UIGF import pipeline as a chosen JSON file.
public sealed class GachaTeyvatHelperImportCoordinator(
    IGachaImportPlanningSource planningSource,
    IGachaTeyvatHelperImportGateway gateway)
{
    public const string ThirdPartyHost = "lelaer.com";

    public async Task<GachaTeyvatHelperImportPlanResult> PlanAsync(
        string uid,
        string gachaUrl,
        Guid archiveId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        string normalizedUid = uid?.Trim() ?? string.Empty;
        if (!GameUidValidation.IsValidUid(normalizedUid))
        {
            return new(
                null,
                "请输入有效的原神 UID（9～10 位数字且区服可识别）。未发起第三方请求。");
        }

        if (GachaManualRefreshCoordinator.ValidateUrl(gachaUrl) is string urlError)
        {
            return new(null, urlError.Replace("未写入任何数据", "未发起第三方请求"));
        }

        if (archiveId == Guid.Empty)
        {
            return new(null, "请先选择要导入的档案。未发起第三方请求。");
        }

        PlayerArchive? archive;
        try
        {
            IReadOnlyList<PlayerArchive> archives =
                await planningSource.GetArchivesAsync(cancellationToken).ConfigureAwait(false);
            archive = archives.FirstOrDefault(item => item.Id == archiveId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, "无法读取本地档案列表，未发起第三方请求。请稍后重试。");
        }

        if (archive is null)
        {
            return new(null, "目标档案已不存在，未发起第三方请求。请重新选择。");
        }

        bool automaticAvailable = false;
        try
        {
            automaticAvailable = (await gateway.GetAvailabilityAsync(cancellationToken)
                .ConfigureAwait(false)).CanAutomaticallyImport;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Availability is advisory; the manual path still works without a passport context.
        }

        string automaticText = automaticAvailable
            ? "自动导入：通行证上下文可用，但本入口只提供手动方式。"
            : "自动导入：不可用（需要用户页通行证登录与角色选择；孤立宿主未提供该上下文），请使用手动方式。";
        string modeLabel = mode == GachaImportMode.Full
            ? "全量导入：覆盖同记录"
            : "仅补充";
        string privacyNotice =
            $"将把 UID {normalizedUid} 与含 authkey 的抽卡链接发送给第三方 " +
            $"{ThirdPartyHost}（提瓦特小助手）以生成 UIGF 文件。" +
            "链接仅用于本次请求，不显示、不保存、不写入日志。";
        string text =
            $"来源：提瓦特小助手手动导入（第三方服务 {ThirdPartyHost}）" + Environment.NewLine +
            $"UID：{normalizedUid}" + Environment.NewLine +
            $"目标档案：{archive.Name}" + Environment.NewLine +
            $"模式：{modeLabel}" + Environment.NewLine +
            (mode == GachaImportMode.Full
                ? "覆盖说明：只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
                : "覆盖说明：仅补充模式不会覆盖不同的既有事实；不删除来源缺失的本地记录。") +
            Environment.NewLine +
            "隐私提示：" + privacyNotice + Environment.NewLine +
            automaticText + Environment.NewLine +
            "后续：确认并下载成功后，将复用统一 UIGF 预览、目标选择与原子提交流程；" +
            "下载失败或取消不会改变本地数据库。";
        return new(
            new GachaTeyvatHelperImportPlan(
                normalizedUid,
                archive.Id,
                archive.Name,
                mode,
                automaticAvailable,
                privacyNotice,
                text),
            null);
    }

    public async Task<GachaTeyvatHelperImportOutcome> DownloadAsync(
        GachaTeyvatHelperImportPlan plan,
        string gachaUrl,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();
        if (GachaManualRefreshCoordinator.ValidateUrl(gachaUrl) is string urlError)
        {
            return new(false, urlError.Replace("未写入任何数据", "未发起第三方请求"), null, null);
        }

        try
        {
            TeyvatHelperUigfDownload download = await gateway.DownloadManuallyAsync(
                plan.Uid,
                gachaUrl,
                cancellationToken).ConfigureAwait(false);
            return new(true, null, download.FileName, download.Content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, DescribeFailure(exception), null, null);
        }
    }

    private static string DescribeFailure(Exception exception) => exception switch
    {
        HttpRequestException =>
            "无法访问提瓦特小助手（lelaer.com）；可能是网络不可用或链接已过期、无效。" +
            "未写入任何数据。",
        TaskCanceledException =>
            "请求提瓦特小助手超时，未写入任何数据。请检查网络后重试。",
        InvalidDataException =>
            "提瓦特小助手返回的内容不是可导入的 UIGF 文件，未写入任何数据。",
        InvalidOperationException =>
            "提瓦特小助手拒绝或无法导出该链接，未写入任何数据。" +
            "请确认 UID 与链接属于同一账号且链接未过期。",
        FormatException =>
            "抽卡链接格式无效，未发起第三方请求。",
        ArgumentException =>
            "原神 UID 或抽卡链接无效，未发起第三方请求。",
        _ => "从提瓦特小助手导入未完成；未确认任何记录被修改，请重试。"
    };
}

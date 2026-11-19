// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Presentation.Management;

public sealed record GachaSTokenRefreshPlan(
    Guid PassportAccountId,
    GameAccount TargetAccount,
    GachaImportMode Mode,
    IsolatedCredentialSummary Credential)
{
    public PreparedGachaRefreshTarget? TargetPreparation { get; init; }
    public string ConfirmationText =>
        $"将通过本机 WinUI 登录凭据刷新：\n" +
        (TargetPreparation?.ArchiveName is { } archiveName ? $"目标档案：{archiveName}\n" : "") +
        $"目标账号：UID {TargetAccount.Uid}（{TargetAccount.ServerRegion}）\n" +
        $"模式：{(Mode == GachaImportMode.Full ? "全量刷新（流程开始时快照）" : "增量刷新")}\n" +
        $"凭据：{Credential.Label ?? "未命名"} · MID {Credential.MaskedMid} · " +
        $"绑定 UID {TargetAccount.Uid}\n" +
        "提交阶段不可取消；不会显示或记录凭据和完整链接。" +
        (TargetPreparation?.AccountToCreate is not null
            ? "\n将随记录原子创建账号" + (TargetPreparation.ArchiveToCreate is { } archive
                ? $"和档案“{archive.Name}”" : "") +
                "；没有可保存的新记录则不创建。包含新父对象的操作不能整体撤销。"
            : "");
}

public sealed record GachaSTokenRefreshPlanResult(
    bool Succeeded,
    GachaSTokenRefreshPlan? Plan,
    string? FailureMessage);

public sealed record GachaSTokenRefreshOutcome(
    bool Succeeded,
    string Message,
    GachaRefreshResult? Result);

public sealed record GachaSTokenRoleChoice(Guid PassportAccountId, string Uid, string Label);

public interface IGachaSTokenRefreshExecutor
{
    Task<GachaRefreshResult> ExecuteAsync(
        GachaRefreshRequest request,
        IProgress<GachaRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class ServiceGachaSTokenRefreshExecutor(RefreshGachaRecords refresher)
    : IGachaSTokenRefreshExecutor
{
    public Task<GachaRefreshResult> ExecuteAsync(
        GachaRefreshRequest request,
        IProgress<GachaRefreshProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        refresher.ExecuteAsync(request, progress, cancellationToken);
}

// Freezes the target account and selected mode before any credential/network work. SToken never
// appears here; the refresh service reads it directly from the credential store boundary.
public sealed class GachaSTokenRefreshCoordinator(
    IIsolatedCredentialStore credentials,
    IGameAccountRepository accounts,
    IGachaSTokenRefreshExecutor refresher,
    PrepareGachaRefreshTarget? targets = null)
{
    public Task<GachaSTokenRefreshPlanResult> PlanAsync(Guid gameAccountId, CancellationToken cancellationToken = default) =>
        PlanAsync(gameAccountId, GachaImportMode.Full, cancellationToken);

    public async Task<GachaSTokenRefreshPlanResult> PlanAsync(
        Guid gameAccountId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (gameAccountId == Guid.Empty)
        {
            return new(false, null, "请先选择要刷新的账号。");
        }

        GameAccount? account = await accounts.GetByIdAsync(gameAccountId, cancellationToken);
        if (account is null)
        {
            return new(false, null, "目标账号已不存在，请重新载入抽卡页。");
        }

        IReadOnlyList<IsolatedCredentialSummary> stored =
            await credentials.ListAsync(cancellationToken);
        if (stored.Count == 0)
        {
            return new(
                false,
                null,
                "本机 WinUI 没有已保存的登录凭据；请先在“登录账号”页添加。");
        }

        IsolatedCredentialSummary[] matching = stored
            .Where(item => item.CanRefreshGacha && item.MatchesRole(account.Uid))
            .ToArray();
        if (matching.Length == 0)
        {
            return new(
                false,
                null,
                $"没有绑定当前档案账号 UID {account.Uid} 的登录凭据；" +
                "请在“登录账号”页添加或更新绑定。");
        }

        IsolatedCredentialSummary? usable = matching.FirstOrDefault(item => item.IsUsable);
        if (usable is null)
        {
            return new(
                false,
                null,
                "绑定当前账号的登录凭据已被标记失效；请在“登录账号”页删除并重新添加，或先检查凭据。");
        }

        return new(
            true,
            new GachaSTokenRefreshPlan(
                usable.Id,
                account,
                mode,
                usable),
            null);
    }

    public async Task<IReadOnlyList<GachaSTokenRoleChoice>> GetRoleChoicesAsync(
        CancellationToken cancellationToken = default) =>
        GachaPassportRoleChoices.From(await credentials.ListAsync(cancellationToken));

    public async Task<GachaSTokenRefreshPlanResult> PlanRoleAsync(
        Guid passportAccountId, string uid, Guid? archiveId, GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
            throw new ArgumentOutOfRangeException(nameof(mode));
        if (targets is null) return new(false, null, "此宿主未配置自动创建账号功能。");
        IsolatedCredentialSummary? credential = (await credentials.ListAsync(cancellationToken))
            .FirstOrDefault(item => item.Id == passportAccountId && item.IsUsable &&
                item.CanRefreshGacha && item.MatchesRole(uid));
        if (credential is null) return new(false, null, "登录账号或角色已变化，请重新选择。");
        if (!GenshinGameRoleIdentity.TryCreate(uid, GameServerRegion.Unknown, out _))
            return new(false, null, "无法可靠识别角色区服；请先在档案与账号页建立账号。");
        if (GameServerRegionResolver.Resolve(uid) is not (GameServerRegion.ChinaOfficial or GameServerRegion.ChinaBilibili))
            return new(false, null, "当前自动生成抽卡链接仅支持国服角色；国际服请使用手动链接。");
        PreparedGachaRefreshTarget target = await targets.ExecuteAsync(
            new(uid, GameServerRegionResolver.Resolve(uid)), archiveId, cancellationToken: cancellationToken);
        return new(true, new(passportAccountId, target.Account, mode, credential)
        {
            TargetPreparation = target,
        }, null);
    }

    public async Task<GachaSTokenRefreshOutcome> ExecuteAsync(
        GachaSTokenRefreshPlan plan,
        IProgress<GachaRefreshProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
            throw new ArgumentOutOfRangeException(nameof(plan));
        // Revalidate the binding immediately before the network/commit path.
        IReadOnlyList<IsolatedCredentialSummary> stored =
            await credentials.ListAsync(CancellationToken.None);
        IsolatedCredentialSummary? current = stored.FirstOrDefault(item =>
            item.Id == plan.PassportAccountId &&
            item.CanRefreshGacha &&
            item.MatchesRole(plan.TargetAccount.Uid) &&
            item.IsUsable);
        if (current is null)
        {
            return new(
                false,
                "登录凭据已不存在、已解除绑定或已失效；未开始网络刷新，未写入任何数据。",
                null);
        }

        try
        {
            // Mode and target account are frozen in the plan. The commit phase must not be
            // cancelled by page navigation/window close; the shell waits for this task.
            GachaRefreshResult result = await refresher.ExecuteAsync(
                new GachaRefreshRequest(
                    plan.TargetAccount,
                    GachaRefreshSource.SToken,
                    plan.Mode,
                    plan.PassportAccountId,
                    ArchiveToCreate: plan.TargetPreparation?.ArchiveToCreate,
                    AccountToCreate: plan.TargetPreparation?.AccountToCreate),
                progress,
                CancellationToken.None);
            // The atomic refresh has committed. A credential-status write is ancillary and must
            // never turn this result into a failed refresh or suggest retrying a committed change.
            bool credentialStatusSaved = true;
            try
            {
                await credentials.MarkUsableAsync(plan.PassportAccountId, CancellationToken.None);
            }
            catch (Exception)
            {
                credentialStatusSaved = false;
            }
            string message =
                $"通过登录账号刷新完成：获取 {result.FetchedCount} 条，" +
                $"新增 {result.InsertedCount} 条，覆盖校正 {result.UpdatedCount} 条，" +
                $"保留重复 {result.DuplicateCount} 条，永久删除抑制 {result.SuppressedCount} 条，" +
                $"请求 {result.PageCount} 页。";
            if (!credentialStatusSaved)
                message += " 抽卡记录已保存，但本机凭据状态未更新；请重新载入登录账号页核对。";
            return new(true, message, result);
        }
        catch (TaskCanceledException)
        {
            return new(false, "网络请求失败或超时；未写入任何数据，请检查网络后重试。", null);
        }
        catch (OperationCanceledException)
        {
            return new(false, "刷新已取消；未确认任何记录被修改。", null);
        }
        catch (Exception exception)
        {
            string message = Describe(exception, plan.TargetAccount);
            bool invalidCredential = IsCredentialFailure(exception);
            if (invalidCredential)
            {
                try
                {
                    await credentials.MarkUnusableAsync(
                        plan.PassportAccountId,
                        message,
                        CancellationToken.None);
                }
                catch (Exception)
                {
                }
            }

            return new(false, message, null);
        }
    }

    private static bool IsCredentialFailure(Exception exception) =>
        exception is KeyNotFoundException or NotSupportedException ||
        exception is InvalidOperationException operation &&
        (operation.Message.Contains("genAuthKey", StringComparison.OrdinalIgnoreCase) ||
         operation.Message.Contains("SToken", StringComparison.Ordinal) ||
         operation.Message.Contains("mid", StringComparison.OrdinalIgnoreCase));

    private static string Describe(Exception exception, GameAccount target)
    {
        return exception switch
        {
            KeyNotFoundException =>
                "保存的登录凭据已被删除或不存在；未写入任何数据。",
            InvalidOperationException operation
                when operation.Message.Contains("genAuthKey", StringComparison.OrdinalIgnoreCase) =>
                "登录凭据已失效或接口拒绝 genAuthKey；请重新添加凭据。未写入任何数据。",
            InvalidOperationException operation
                when operation.Message.Contains("does not contain SToken", StringComparison.Ordinal) ||
                     operation.Message.Contains("does not contain SToken and mid", StringComparison.Ordinal) =>
                "登录凭据缺少 SToken/MID；请重新添加。未写入任何数据。",
            InvalidDataException =>
                $"抽卡接口返回的 UID 与目标账号 UID {target.Uid} 不一致；未写入任何数据。",
            NotSupportedException =>
                "当前登录凭据仅支持国服官服/B 服原神账号；未写入任何数据。",
            HttpRequestException or TaskCanceledException =>
                "网络请求失败或超时；未写入任何数据，请检查网络后重试。",
            _ =>
                "通过登录账号刷新未完成；未确认任何记录被修改，请重新载入核对。"
        };
    }
}

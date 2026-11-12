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
    public string ConfirmationText =>
        $"将通过本机隔离宿主登录凭据刷新：\n" +
        $"目标账号：UID {TargetAccount.Uid}（{TargetAccount.ServerRegion}）\n" +
        $"模式：{(Mode == GachaImportMode.Full ? "全量刷新（流程开始时快照）" : "增量刷新")}\n" +
        $"凭据：{Credential.Label ?? "未命名"} · MID {Credential.MaskedMid} · " +
        $"绑定 UID {Credential.BoundRoleUid}\n" +
        "提交阶段不可取消；不会显示、记录或回显 SToken/MID/完整链接。\n" +
        "仅使用本机隔离宿主凭据，生产凭据迁移待 UI-6 评审。";
}

public sealed record GachaSTokenRefreshPlanResult(
    bool Succeeded,
    GachaSTokenRefreshPlan? Plan,
    string? FailureMessage);

public sealed record GachaSTokenRefreshOutcome(
    bool Succeeded,
    string Message,
    GachaRefreshResult? Result);

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

// Freezes the target account and Full mode before any credential/network work. SToken never
// appears here; the refresh service reads it directly from the credential store boundary.
public sealed class GachaSTokenRefreshCoordinator(
    IIsolatedCredentialStore credentials,
    IGameAccountRepository accounts,
    IGachaSTokenRefreshExecutor refresher)
{
    public async Task<GachaSTokenRefreshPlanResult> PlanAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default)
    {
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
                "本机隔离宿主没有已保存的登录凭据；请先在“登录账号”页添加。");
        }

        IsolatedCredentialSummary[] matching = stored
            .Where(item => string.Equals(
                item.BoundRoleUid, account.Uid, StringComparison.Ordinal))
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
                GachaImportMode.Full,
                usable),
            null);
    }

    public async Task<GachaSTokenRefreshOutcome> ExecuteAsync(
        GachaSTokenRefreshPlan plan,
        IProgress<GachaRefreshProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        // Revalidate the binding immediately before the network/commit path.
        IReadOnlyList<IsolatedCredentialSummary> stored =
            await credentials.ListAsync(CancellationToken.None);
        IsolatedCredentialSummary? current = stored.FirstOrDefault(item =>
            item.Id == plan.PassportAccountId &&
            string.Equals(item.BoundRoleUid, plan.TargetAccount.Uid, StringComparison.Ordinal) &&
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
            // Full mode and target account are frozen in the plan. The commit phase must not be
            // cancelled by page navigation/window close; the shell waits for this task.
            GachaRefreshResult result = await refresher.ExecuteAsync(
                new GachaRefreshRequest(
                    plan.TargetAccount,
                    GachaRefreshSource.SToken,
                    GachaImportMode.Full,
                    plan.PassportAccountId),
                progress,
                CancellationToken.None);
            await credentials.MarkUsableAsync(plan.PassportAccountId, CancellationToken.None);
            string message =
                $"通过登录账号刷新完成：获取 {result.FetchedCount} 条，" +
                $"新增 {result.InsertedCount} 条，覆盖校正 {result.UpdatedCount} 条，" +
                $"保留重复 {result.DuplicateCount} 条，永久删除抑制 {result.SuppressedCount} 条，" +
                $"请求 {result.PageCount} 页。";
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

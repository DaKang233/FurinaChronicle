// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Gacha;

// One Portable import flow: stage the ZIP to private disk, build a session-scoped index, plan
// against the frozen target, then apply only after re-validating that preview. The package is
// never expanded into UI collections.
public sealed class GachaPortableImportCoordinator(
    IGachaPortableInputStager stager,
    IGachaPortableIndexedPackageFactory packageFactory,
    IGachaPortableImportPlanner planner,
    IGachaPortableImportApplier applier,
    TimeProvider timeProvider)
{
    public async Task<GachaPortableImportPrepareResult> PrepareAsync(
        GachaImportSource source,
        GachaPortableImportTarget target,
        GachaImportMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (mode is not (GachaImportMode.Incremental or GachaImportMode.Full))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (ValidateTarget(target) is string targetError)
        {
            return new(null, targetError);
        }

        IGachaPortableStagedInput? staged = null;
        IGachaPortableIndexedPackage? package = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using (Stream input = await source.OpenReadAsync(cancellationToken).ConfigureAwait(false))
            {
                staged = await stager.StageAsync(input, cancellationToken).ConfigureAwait(false);
            }

            package = await packageFactory.OpenAsync(staged, cancellationToken).ConfigureAwait(false);
            GachaPortableImportPlan plan = await planner.ExecuteAsync(
                BuildRequest(package, target, mode),
                cancellationToken).ConfigureAwait(false);
            var session = new GachaPortableImportSession(
                package,
                plan,
                target,
                mode,
                timeProvider.GetUtcNow(),
                staged.Sha256,
                staged.Length,
                source.FileName);
            package = null;
            staged = null;
            return new(session, null);
        }
        catch (OperationCanceledException)
        {
            await DisposeQuietlyAsync(package, staged).ConfigureAwait(false);
            throw;
        }
        catch (Exception exception)
        {
            await DisposeQuietlyAsync(package, staged).ConfigureAwait(false);
            return new(null, GachaPortableText.DescribeFailure(exception));
        }
    }

    public async Task<GachaPortableReplanResult> ReplanAsync(
        GachaPortableImportSession session,
        GachaPortableImportTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(target);
        if (session.IsApplied)
        {
            return new(false, "本次 Portable 操作已经提交，不能再用旧会话重新规划。");
        }

        if (ValidateTarget(target) is string targetError)
        {
            return new(false, targetError);
        }

        try
        {
            GachaPortableImportPlan plan = await planner.ExecuteAsync(
                BuildRequest(session.Package, target, session.Mode),
                cancellationToken).ConfigureAwait(false);
            session.Plan = plan;
            session.Target = target;
            return new(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(false, GachaPortableText.DescribeFailure(exception));
        }
    }

    public Task<GachaPortableApplyOutcome> ApplyAsync(
        GachaPortableImportSession session,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(session, null, null, cancellationToken);

    public Task<GachaPortableApplyOutcome> ApplyAsync(
        GachaPortableImportSession session,
        IReadOnlyList<TombstoneReintroductionConfirmation> reintroductionConfirmations,
        Guid? cleanupConfirmationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reintroductionConfirmations);
        return ApplyCoreAsync(session, reintroductionConfirmations, cleanupConfirmationId, cancellationToken);
    }

    private async Task<GachaPortableApplyOutcome> ApplyCoreAsync(
        GachaPortableImportSession session,
        IReadOnlyList<TombstoneReintroductionConfirmation>? reintroductions,
        Guid? cleanupConfirmationId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (session.IsApplied)
        {
            return new(
                GachaPortableApplyOutcomeKind.AlreadyCommitted,
                "此 Portable 操作此前已经提交，未重复写入。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Re-plan before applying: a changed target, archive state or local fact set invalidates
        // the old preview and the apply is refused instead of writing a stale plan.
        GachaPortableImportPlan fresh;
        try
        {
            fresh = await planner.ExecuteAsync(
                BuildRequest(session.Package, session.Target, session.Mode),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(
                GachaPortableApplyOutcomeKind.Conflict,
                "预览后目标或本地数据状态已变化，未写入任何数据。请重新选择 Portable 文件并预览。" +
                $"\n{GachaPortableText.DescribeFailure(exception)}");
        }

        if (!fresh.CanApply)
        {
            return new(
                GachaPortableApplyOutcomeKind.Conflict,
                GachaPortableText.DescribeBlockers(fresh));
        }

        if (!GachaPortableText.PlansMatch(session.Plan, fresh))
        {
            return new(
                GachaPortableApplyOutcomeKind.Conflict,
                "预览后本地事实或目标档案发生变化，原预览已失效，未写入任何数据。" +
                "请重新生成预览后再应用。");
        }

        session.Plan = fresh;
        try
        {
            GachaPortableApplyResult result = await applier.ApplyAsync(
                new GachaPortableIndexedApplyRequest(
                    session.Package,
                    fresh,
                    session.OperationId,
                    session.ReceivedAt,
                    session.ReceiptBatchId,
                    reintroductions,
                    cleanupConfirmationId),
                cancellationToken).ConfigureAwait(false);
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.NoOp or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                session.IsApplied = true;
            }

            return GachaPortableText.FromApplyResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new(
                GachaPortableApplyOutcomeKind.Failed,
                GachaPortableText.DescribeFailure(exception));
        }
    }

    private static string? ValidateTarget(GachaPortableImportTarget target) =>
        target.Mode switch
        {
            GachaPortableImportTargetMode.Automatic => null,
            GachaPortableImportTargetMode.NewArchive => null,
            GachaPortableImportTargetMode.CurrentArchive =>
                target.ArchiveId is Guid archiveId && archiveId != Guid.Empty
                    ? null
                    : "未指定 Portable 导入目标档案。请重新选择目标。",
            _ => "不支持的 Portable 导入目标。请重新选择目标。"
        };

    private static GachaPortableIndexedImportPlanRequest BuildRequest(
        IGachaPortableIndexedPackage package,
        GachaPortableImportTarget target,
        GachaImportMode mode) =>
        new(
            package,
            TargetArchiveId: target.Mode == GachaPortableImportTargetMode.CurrentArchive
                ? target.ArchiveId
                : null,
            CreateNewArchive: target.Mode == GachaPortableImportTargetMode.NewArchive,
            RequireUniqueArchiveNames: true,
            Mode: mode == GachaImportMode.Full
                ? GachaPortableApplyMode.ReplaceExisting
                : GachaPortableApplyMode.AddOnly);

    private static async ValueTask DisposeQuietlyAsync(
        IGachaPortableIndexedPackage? package,
        IGachaPortableStagedInput? staged)
    {
        try
        {
            if (package is not null)
            {
                await package.DisposeAsync().ConfigureAwait(false);
            }
            else if (staged is not null)
            {
                await staged.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Session cleanup must never mask the user-facing failure.
        }
    }
}

// Chinese wording for Portable preview, confirmation and result surfaces. It lives in the shared
// presentation layer so both hosts render the same semantics and tests can assert on it.
internal static class GachaPortableText
{
    public static string BuildConfirmationText(GachaPortableImportSession session)
    {
        GachaPortableImportPlan plan = session.Plan;
        var lines = new List<string>
        {
            $"文件：{session.FileName}",
            $"格式：Furina Gacha Portable v{session.Package.FormatVersion}（{session.SourceLength} 字节）",
            $"来源档案：{session.Package.SourceArchive.Name}",
            $"目标：{DescribeArchivePlan(plan.Archive)}",
            $"模式：{(plan.Mode == GachaPortableApplyMode.ReplaceExisting ? "全量导入：覆盖同记录" : "仅补充")}",
            "包指纹：" + ShortFingerprint(session.Package.PackageFingerprint)
        };

        if (plan.Archive.Candidates.Count > 0 &&
            plan.Archive.Kind == GachaPortableArchivePlanKind.RequiresSelection)
        {
            lines.Add("候选档案：");
            lines.AddRange(plan.Archive.Candidates.Take(8).Select(candidate =>
                $"· {candidate.Name}" + (candidate.IsExactMatch ? "（精确同名）" : "（名称相似）")));
        }

        lines.Add(
            $"预计：新建账号 {plan.Preview.AccountCreateCount}，复用账号 {plan.Preview.AccountReuseCount}，" +
            $"新增 {plan.Preview.AddCount}，覆盖 {plan.Preview.ReplaceCount}，" +
            $"保留 {plan.Preview.SkipCount}，冲突 {plan.Preview.ConflictCount}，" +
            $"身份别名 {plan.Preview.AliasCount}。");
        lines.Add("账号映射：");
        foreach (GachaPortableAccountImportPlan account in plan.Accounts)
        {
            lines.Add(DescribeAccount(account));
        }

        IReadOnlyList<GachaPortableRecordConflict> conflicts = plan.Accounts
            .SelectMany(account => account.Conflicts)
            .Take(12)
            .ToArray();
        if (conflicts.Count > 0)
        {
            lines.Add("冲突预览（最多 12 条）：");
            lines.AddRange(conflicts.Select(conflict =>
                $"· 记录 {conflict.ExternalRecordId}：字段 " +
                string.Join("、", conflict.DifferentFields)));
        }

        lines.Add(plan.Mode == GachaPortableApplyMode.ReplaceExisting
            ? "覆盖说明：只覆盖来源明确包含的同记录并保留修订；不删除来源缺失的本地记录。"
            : "覆盖说明：当前为仅补充模式，不会覆盖不同的既有事实；不删除来源缺失的本地记录。");
        bool infrastructureChanges =
            plan.Archive.Kind == GachaPortableArchivePlanKind.CreateNew ||
            plan.Preview.AccountCreateCount > 0 ||
            plan.Preview.AliasCount > 0;
        lines.Add(
            infrastructureChanges
                ? "撤销说明：本次会创建档案、账号或身份别名，因此整个导入不承诺可撤销。"
                : "撤销说明：本次仅变更已有账号事实；若撤销材料未被容量淘汰，可按现有保留策略撤销（本页不提供撤销入口）。");
        lines.Add(
            "数据边界：这是 Furina Gacha Portable v1 抽卡数据集，不是完整应用备份；" +
            "来源 provenance 原样保留，本机接收信息另存为 Receipt，不包含凭据或其他领域数据。");
        lines.Add(
            "提交说明：整个导入作为单一原子事务执行；预览后目标或本地事实变化会拒绝旧预览。");
        return string.Join(Environment.NewLine, lines);
    }

    public static string DescribeBlockers(GachaPortableImportPlan plan)
    {
        if (plan.Archive.Kind == GachaPortableArchivePlanKind.RequiresSelection)
        {
            return "同名档案不唯一，需要重新选择导入目标档案。未写入任何数据。";
        }

        var collisions = plan.Accounts
            .Where(account => account.Identity.Kind == GachaPortableIdentityPlanKind.Collision)
            .Select(account => account.NaturalIdentity.Uid)
            .ToArray();
        if (collisions.Length > 0)
        {
            return $"包内身份 ID 与本机既有身份冲突（UID {string.Join("、", collisions)}）。" +
                "未写入任何数据。请更换目标或移除冲突身份后重试。";
        }

        if (plan.Preview.ConflictCount > 0)
        {
            IReadOnlyList<GachaPortableRecordConflict> conflicts = plan.Accounts
                .SelectMany(account => account.Conflicts)
                .Take(8)
                .ToArray();
            return $"发现 {plan.Preview.ConflictCount} 条不同的既有事实，仅补充模式不会覆盖。" +
                "未写入任何数据。可切换为“全量导入：覆盖同记录”后重新预览。" +
                (conflicts.Count == 0
                    ? ""
                    : Environment.NewLine + string.Join(
                        Environment.NewLine,
                        conflicts.Select(conflict =>
                            $"· 记录 {conflict.ExternalRecordId}：" +
                            string.Join("、", conflict.DifferentFields))));
        }

        return "Portable 预览不可应用，未写入任何数据。请重新选择目标后生成预览。";
    }

    public static bool PlansMatch(GachaPortableImportPlan left, GachaPortableImportPlan right)
    {
        // Proposed archive/account GUIDs are deliberately regenerated by every planning pass.
        // Only stable external facts and deterministic proposals (target archive, exact name)
        // decide whether the confirmed preview is still the same plan.
        if (left.PackageFingerprint != right.PackageFingerprint ||
            left.Mode != right.Mode ||
            left.Archive.Kind != right.Archive.Kind ||
            left.Archive.TargetArchiveId != right.Archive.TargetArchiveId ||
            !string.Equals(
                left.Archive.ProposedName,
                right.Archive.ProposedName,
                StringComparison.Ordinal) ||
            left.Preview != right.Preview ||
            left.Accounts.Count != right.Accounts.Count)
        {
            return false;
        }

        return left.Accounts.Zip(right.Accounts).All(pair => AccountsMatch(pair.First, pair.Second));
    }

    public static GachaPortableApplyOutcome FromApplyResult(GachaPortableApplyResult result)
    {
        switch (result.Status)
        {
            case ChangeExecutionStatus.Applied:
                return new(
                    GachaPortableApplyOutcomeKind.Applied,
                    $"已导入 {result.InsertedRecordCount} 条，覆盖校正 {result.UpdatedRecordCount} 条，" +
                    $"保留 {result.SkippedRecordCount} 条。",
                    result);
            case ChangeExecutionStatus.NoOp:
                return new(
                    GachaPortableApplyOutcomeKind.NoOp,
                    "目标已包含相同事实，未写入新内容。",
                    result);
            case ChangeExecutionStatus.AlreadyCommitted:
                return new(
                    GachaPortableApplyOutcomeKind.AlreadyCommitted,
                    "此操作此前已经提交，未重复写入。",
                    result);
            case ChangeExecutionStatus.Suppressed:
                IReadOnlyList<TombstoneReintroductionWarning> warnings =
                    result.ReintroductionWarnings ??
                    (result.ReintroductionWarning is null
                        ? []
                        : [result.ReintroductionWarning]);
                return new(
                    GachaPortableApplyOutcomeKind.RequiresReintroduction,
                    $"包命中 {warnings.Count} 条本机永久删除标记；必须逐次明确确认才会重新引入，" +
                    "取消不会改变数据库。",
                    result,
                    warnings);
            case ChangeExecutionStatus.NeedsConfirmation when result.CleanupPlan is not null:
                return new(
                    GachaPortableApplyOutcomeKind.RequiresCleanup,
                    $"需要清理 {result.CleanupPlan.Candidates.Count} 份较早撤销材料" +
                    $"（约 {result.CleanupPlan.ReclaimableBytes} 字节）；不会删除 Revision 或业务事实。",
                    result,
                    CleanupPlan: result.CleanupPlan);
            case ChangeExecutionStatus.Conflict:
                return new(
                    GachaPortableApplyOutcomeKind.Conflict,
                    "预览后目标状态已变化，未写入任何数据。请重新选择 Portable 文件并预览。",
                    result);
            default:
                return new(
                    GachaPortableApplyOutcomeKind.Failed,
                    "Portable 导入未完成，未写入任何数据。请重新预览后重试。",
                    result);
        }
    }

    public static string DescribeFailure(Exception exception) => exception switch
    {
        GachaPortableException portable => portable.Code switch
        {
            GachaPortableErrorCode.UnsupportedVersion =>
                "Portable 文件版本不受支持，未写入任何数据。请使用当前 FurinaChronicle 导出的 Portable v1 文件。",
            GachaPortableErrorCode.UnsupportedAccount =>
                "Portable 文件包含不支持的账号身份或区服，未写入任何数据。",
            GachaPortableErrorCode.HashMismatch or
            GachaPortableErrorCode.RecordMismatch or
            GachaPortableErrorCode.RecordCountMismatch or
            GachaPortableErrorCode.LengthMismatch =>
                "Portable 文件内容校验失败（哈希、长度或记录计数不一致），未写入任何数据。",
            GachaPortableErrorCode.ResourceLimitExceeded =>
                "Portable 文件超出本机允许的大小或记录数量限制，未写入任何数据。",
            _ => "Portable 文件无效、已损坏或缺少必需内容，未写入任何数据。"
        },
        UnauthorizedAccessException or IOException =>
            "无法读取所选 Portable 文件或本机临时目录，未写入任何数据。请确认文件仍可访问后重试。",
        KeyNotFoundException =>
            "目标档案或账号已不存在，未写入任何数据。请重新选择目标并预览。",
        InvalidOperationException =>
            "预览后目标或本地事实状态已变化，未写入任何数据。请重新生成预览。",
        _ => "Portable 操作未完成；未确认任何记录被修改，请重新预览后重试。"
    };

    private static string DescribeArchivePlan(GachaPortableArchivePlan archive) =>
        archive.Kind switch
        {
            GachaPortableArchivePlanKind.MapExisting =>
                $"映射到现有档案“{archive.ProposedName}”",
            GachaPortableArchivePlanKind.CreateNew =>
                $"按来源名称新建档案“{archive.ProposedName}”",
            _ => "需要选择目标档案（同名档案不唯一）"
        };

    private static string DescribeAccount(GachaPortableAccountImportPlan account)
    {
        string identity = GachaImportText.RegionLabel(
            GameServerRegionResolver.Resolve(account.NaturalIdentity.Uid));
        string mapping = account.Identity.Kind switch
        {
            GachaPortableIdentityPlanKind.Reuse => "复用同身份 ID",
            GachaPortableIdentityPlanKind.PreserveSource => "保留包内身份 ID",
            GachaPortableIdentityPlanKind.AliasSourceToTarget => "把来源身份别名到目标身份",
            _ => "身份 ID 冲突（阻塞）"
        };
        string target = account.TargetAccountId is null
            ? "将新建账号副本"
            : $"匹配目标账号（现有 {(account.TargetRecordCountAtPlan?.ToString() ?? "未知")} 条）";
        return $"· UID {account.NaturalIdentity.Uid}（{identity}）· {mapping} · {target} · " +
            $"新增 {account.AddCount} / 覆盖 {account.ReplaceCount} / " +
            $"保留 {account.SkipCount} / 冲突 {account.ConflictCount}";
    }

    private static bool AccountsMatch(
        GachaPortableAccountImportPlan left,
        GachaPortableAccountImportPlan right) =>
        left.SourceAccountReference == right.SourceAccountReference &&
        left.TargetAccountId == right.TargetAccountId &&
        left.NaturalIdentity == right.NaturalIdentity &&
        left.Identity.Kind == right.Identity.Kind &&
        left.AddCount == right.AddCount &&
        left.ReplaceCount == right.ReplaceCount &&
        left.SkipCount == right.SkipCount &&
        left.ConflictCount == right.ConflictCount;

    private static string ShortFingerprint(string fingerprint) =>
        fingerprint.Length <= 12 ? fingerprint : fingerprint[..12] + "…";
}

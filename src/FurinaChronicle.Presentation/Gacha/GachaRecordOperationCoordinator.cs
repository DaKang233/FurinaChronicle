// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Gacha;

public enum GachaRecordOperationKind
{
    Correct = 0,
    Delete = 1,
    IrreversibleDelete = 2
}

public sealed record GachaRecordOperationPlan(
    GachaRecordOperationKind Kind,
    GachaFactReference Reference,
    GachaRecord Current,
    FactVersion ExpectedVersion,
    Guid ArchiveId,
    GachaCorrectionInput? Correction,
    bool RequiresExplicitConfirmation,
    string KindLabel,
    string Summary,
    string ImpactText,
    string? BlockMessage = null)
{
    public bool CanExecute => BlockMessage is null;

    public bool IsUndoExpected => Kind != GachaRecordOperationKind.IrreversibleDelete;
}

public sealed record GachaRecordOperationPlanResult(GachaRecordOperationPlan? Plan, string? FailureMessage)
{
    public bool Succeeded => Plan is not null;
}

public enum GachaRecordOperationOutcomeKind
{
    Applied = 0,
    NoOp = 1,
    AlreadyCommitted = 2,
    Conflict = 3,
    RequiresCleanup = 4,
    Failed = 5
}

public sealed record GachaRecordOperationOutcome(
    GachaRecordOperationOutcomeKind Kind,
    string Message,
    HistoryCleanupPlan? CleanupPlan = null)
{
    public bool IsSuccess =>
        Kind is GachaRecordOperationOutcomeKind.Applied or
            GachaRecordOperationOutcomeKind.NoOp or
            GachaRecordOperationOutcomeKind.AlreadyCommitted;

    public bool RequiresUserConfirmation => Kind == GachaRecordOperationOutcomeKind.RequiresCleanup;
}

// Plans and commits one record-level correction/deletion through ManageGachaHistory. The plan
// freezes the current fact version, so a changed record invalidates the preview instead of
// silently mutating a different fact.
public sealed class GachaRecordOperationCoordinator(
    ManageGachaHistory history,
    IGachaAtomicChangeStore store)
{
    public async Task<GachaRecordOperationPlanResult> PlanAsync(
        GachaFactReference reference,
        GachaRecordOperationKind kind,
        GachaCorrectionInput? correction = null,
        CancellationToken cancellationToken = default)
    {
        if (reference.GameAccountId == Guid.Empty ||
            string.IsNullOrWhiteSpace(reference.ExternalRecordId))
        {
            return new(null, "未选择有效的抽奖记录。请重新载入明细后选择记录。");
        }

        GachaFactState? current;
        try
        {
            current = await store.GetCurrentAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(null, "无法读取所选记录的当前事实，未写入任何数据。请重新载入后重试。");
        }

        if (current is null)
        {
            return new(null, "所选记录已不存在，未写入任何数据。请重新载入当前范围。");
        }

        int? undoLimit = null;
        try
        {
            undoLimit = await store.GetUndoLimitAsync(current.ArchiveId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Undo wording degrades to the conservative conditional explanation.
        }

        return kind switch
        {
            GachaRecordOperationKind.Correct => PlanCorrection(current, reference, correction, undoLimit),
            GachaRecordOperationKind.Delete => new(PlanDelete(current, reference, undoLimit), null),
            GachaRecordOperationKind.IrreversibleDelete =>
                new(PlanIrreversibleDelete(current, reference), null),
            _ => new(null, "不支持的记录操作。")
        };
    }

    public async Task<GachaRecordOperationOutcome> ExecuteAsync(
        GachaRecordOperationPlan plan,
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanExecute)
        {
            return new(
                GachaRecordOperationOutcomeKind.Conflict,
                plan.BlockMessage ?? "记录操作预览不可执行，未写入任何数据。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        GachaFactState? current;
        try
        {
            current = await store.GetCurrentAsync(plan.Reference, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(
                GachaRecordOperationOutcomeKind.Failed,
                "无法读取所选记录的当前事实，未写入任何数据。请重新载入后重试。");
        }

        if (current is null || current.Version != plan.ExpectedVersion)
        {
            return new(
                GachaRecordOperationOutcomeKind.Conflict,
                "记录已在预览后变化或被删除，原预览已失效，未写入任何数据。请重新载入并重试。");
        }

        try
        {
            GachaAtomicChangeResult result = plan.Kind switch
            {
                GachaRecordOperationKind.Correct => await history.CorrectAsync(
                    OperationId.New(),
                    plan.Reference,
                    plan.Correction ?? throw new InvalidOperationException("Missing correction input."),
                    cleanupConfirmationId,
                    cancellationToken).ConfigureAwait(false),
                GachaRecordOperationKind.Delete => await history.DeleteAsync(
                    OperationId.New(),
                    plan.Reference,
                    cleanupConfirmationId,
                    cancellationToken).ConfigureAwait(false),
                GachaRecordOperationKind.IrreversibleDelete => await history.IrreversiblyDeleteAsync(
                    OperationId.New(),
                    plan.Reference,
                    cancellationToken).ConfigureAwait(false),
                _ => throw new InvalidOperationException("Unsupported record operation.")
            };
            return FromResult(plan, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (KeyNotFoundException)
        {
            return new(
                GachaRecordOperationOutcomeKind.Conflict,
                "记录已不存在，未写入任何数据。请重新载入当前范围。");
        }
        catch (InvalidOperationException)
        {
            return new(
                GachaRecordOperationOutcomeKind.Conflict,
                "记录已在预览后变化，未写入任何数据。请重新载入并重试。");
        }
        catch (Exception)
        {
            return new(
                GachaRecordOperationOutcomeKind.Failed,
                "记录操作未完成；未确认任何记录被修改，请重新载入后核对并重试。");
        }
    }

    private static GachaRecordOperationPlanResult PlanCorrection(
        GachaFactState current,
        GachaFactReference reference,
        GachaCorrectionInput? correction,
        int? undoLimit)
    {
        if (correction is null)
        {
            return new(null, "缺少纠正内容，未写入任何数据。");
        }

        string? itemName = Normalize(correction.ItemName);
        string? itemId = Normalize(correction.ItemId);
        GachaRecord corrected = current.Record with
        {
            ItemName = itemName,
            ItemId = itemId,
            RankType = correction.RankType,
            Time = correction.OccurredAt
        };
        List<string> changes = [];
        if (!string.Equals(current.Record.ItemName, itemName, StringComparison.Ordinal))
        {
            changes.Add($"名称“{current.Record.ItemName ?? "未知"}”→“{itemName ?? "空"}”");
        }
        if (!string.Equals(current.Record.ItemId, itemId, StringComparison.Ordinal))
        {
            changes.Add($"物品 ID“{current.Record.ItemId ?? "未知"}”→“{itemId ?? "空"}”");
        }
        if (current.Record.RankType != correction.RankType)
        {
            changes.Add($"星级 {current.Record.RankType?.ToString() ?? "未知"}→" +
                $"{correction.RankType?.ToString() ?? "未知"}");
        }
        if (current.Record.Time != correction.OccurredAt)
        {
            changes.Add($"时间 {current.Record.Time:yyyy-MM-dd HH:mm:ss zzz}→" +
                $"{correction.OccurredAt:yyyy-MM-dd HH:mm:ss zzz}");
        }

        if (changes.Count == 0)
        {
            return new(
                null,
                "所选记录没有需要保存的修改，未写入任何数据。");
        }

        string summary = $"纠正记录 {reference.ExternalRecordId}：{string.Join("，", changes)}。";
        string impact =
            "影响范围：仅 1 条记录。" + Environment.NewLine +
            $"变更：{string.Join("；", changes)}。" + Environment.NewLine +
            "结果：保留稳定记录 ID 与账号归属；来源标记为“用户输入”；产生 Revision 并保留变更前事实。" +
            Environment.NewLine +
            $"撤销：{UndoExplanation(undoLimit)}";
        return new(new GachaRecordOperationPlan(
            GachaRecordOperationKind.Correct,
            reference,
            current.Record,
            current.Version,
            current.ArchiveId,
            new GachaCorrectionInput(itemName, itemId, correction.RankType, correction.OccurredAt),
            RequiresExplicitConfirmation: false,
            KindLabel: "纠正记录",
            Summary: summary,
            ImpactText: impact), null);
    }

    private static GachaRecordOperationPlan PlanDelete(
        GachaFactState current,
        GachaFactReference reference,
        int? undoLimit)
    {
        string impact =
            "影响范围：1 条记录。" + Environment.NewLine +
            $"记录：{current.Record.ItemName ?? "物品未知"} · {current.Record.Time:yyyy-MM-dd HH:mm:ss zzz}" +
            Environment.NewLine +
            "结果：从当前事实中移除，保留可撤销快照与 Revision；这不是永久删除。" + Environment.NewLine +
            $"撤销：{UndoExplanation(undoLimit)}";
        return new(
            GachaRecordOperationKind.Delete,
            reference,
            current.Record,
            current.Version,
            current.ArchiveId,
            Correction: null,
            RequiresExplicitConfirmation: false,
            KindLabel: "删除记录（可撤销）",
            Summary: $"删除记录 {reference.ExternalRecordId}（可撤销）。",
            ImpactText: impact);
    }

    private static GachaRecordOperationPlan PlanIrreversibleDelete(
        GachaFactState current,
        GachaFactReference reference)
    {
        string impact =
            "影响范围：1 条记录。" + Environment.NewLine +
            $"记录：{current.Record.ItemName ?? "物品未知"} · {current.Record.Time:yyyy-MM-dd HH:mm:ss zzz}" +
            Environment.NewLine +
            "结果：清除当前事实、可恢复快照和关联撤销材料，并写入不可逆删除摘要（Tombstone）。" +
            Environment.NewLine +
            "后续：自动导入会抑制该记录；显式重新引入必须逐次确认，且不会恢复本次操作。" +
            Environment.NewLine +
            "撤销：永久删除不可撤销，并使关联操作失去撤销资格。";
        return new(
            GachaRecordOperationKind.IrreversibleDelete,
            reference,
            current.Record,
            current.Version,
            current.ArchiveId,
            Correction: null,
            RequiresExplicitConfirmation: true,
            KindLabel: "永久删除记录（不可撤销）",
            Summary: $"永久删除记录 {reference.ExternalRecordId}（不可撤销）。",
            ImpactText: impact);
    }

    private static GachaRecordOperationOutcome FromResult(
        GachaRecordOperationPlan plan,
        GachaAtomicChangeResult result)
    {
        switch (result.Status)
        {
            case ChangeExecutionStatus.Applied:
                return new(
                    GachaRecordOperationOutcomeKind.Applied,
                    plan.Kind switch
                    {
                        GachaRecordOperationKind.Correct => $"已保存纠正：记录 {plan.Reference.ExternalRecordId}。",
                        GachaRecordOperationKind.Delete => $"已删除记录 {plan.Reference.ExternalRecordId}（可撤销）。",
                        _ => $"已永久删除记录 {plan.Reference.ExternalRecordId}。"
                    });
            case ChangeExecutionStatus.NoOp:
                return new(
                    GachaRecordOperationOutcomeKind.NoOp,
                    "没有实际变更，未写入新内容。");
            case ChangeExecutionStatus.AlreadyCommitted:
                return new(
                    GachaRecordOperationOutcomeKind.AlreadyCommitted,
                    "此操作此前已经提交，未重复写入。");
            case ChangeExecutionStatus.NeedsConfirmation when result.CleanupPlan is not null:
                return new(
                    GachaRecordOperationOutcomeKind.RequiresCleanup,
                    $"需要清理 {result.CleanupPlan.Candidates.Count} 份较早撤销材料" +
                    $"（约 {result.CleanupPlan.ReclaimableBytes} 字节）；不会删除 Revision 或业务事实。",
                    result.CleanupPlan);
            case ChangeExecutionStatus.Conflict:
                return new(
                    GachaRecordOperationOutcomeKind.Conflict,
                    "记录已在预览后变化，未写入任何数据。请重新载入并重试。");
            default:
                return new(
                    GachaRecordOperationOutcomeKind.Failed,
                    "记录操作未完成，未写入任何数据。请重新载入后重试。");
        }
    }

    private static string UndoExplanation(int? undoLimit) => undoLimit switch
    {
        0 => "该档案已关闭撤销材料保存（限额 0）：本次变更保留 Revision，但不能撤销。",
        > 0 => $"该档案撤销限额为 {undoLimit}；若撤销材料未被容量淘汰，本次变更可在操作历史撤销。" +
            "本页不提供撤销入口。",
        _ => "撤销资格取决于该档案的撤销保留策略与剩余材料；本页不提供撤销入口。"
    };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

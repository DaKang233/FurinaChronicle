// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Presentation.Management;

// Read/undo port for the operation history page. It intentionally exposes only the production
// history services; the page never infers undo eligibility from its own counters.
public interface IArchiveHistorySource
{
    Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
        Guid archiveId,
        int count,
        CancellationToken cancellationToken = default);

    Task<int> GetUndoLimitAsync(Guid archiveId, CancellationToken cancellationToken = default);

    Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int limit,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> UndoLatestAsync(
        Guid archiveId,
        OperationId operationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GachaRevisionItem>> GetGachaRevisionsAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DomainRevisionItem>> GetDomainRevisionsAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default);
}

public sealed record ArchiveHistoryEntry(
    Guid ChangeSetId,
    DataChangeOperationKind OperationKind,
    string Operation,
    string CommittedAt,
    string Summary,
    string Affected,
    string UndoState,
    string Scope,
    bool IsUndoEligible,
    UndoIneligibilityReason IneligibilityReason)
{
    public bool IsCrossArchive => Scope.StartsWith("跨", StringComparison.Ordinal);

    public bool IsIrreversible =>
        OperationKind == DataChangeOperationKind.IrreversibleDelete ||
        IneligibilityReason == UndoIneligibilityReason.IrreversibleDeletion;

    public string Reversibility => IsIrreversible ? "不可逆" : "可逆";
}

public sealed record ArchiveHistoryRevision(
    string Kind,
    string Title,
    string Change,
    string Before,
    string After,
    string CreatedAt,
    bool IsDomainSummary,
    string? Note);

public sealed record ArchiveHistorySnapshot
{
    public IReadOnlyList<PlayerArchive> Archives { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public IReadOnlyList<ArchiveHistoryEntry> Entries { get; init; } = [];
    public Guid? SelectedChangeSetId { get; init; }
    public IReadOnlyList<ArchiveHistoryRevision> Revisions { get; init; } = [];
    public int UndoLimit { get; init; } = 3;
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入操作历史";
    public string RevisionStatus { get; init; } = "选择一项操作查看修订。";
    public string? Error { get; init; }

    public PlayerArchive? SelectedArchive =>
        Archives.FirstOrDefault(archive => archive.Id == SelectedArchiveId);

    public ArchiveHistoryEntry? LatestUndoCandidate =>
        Entries.FirstOrDefault(entry => entry.IsUndoEligible);
}

public sealed record ArchiveHistoryActionResult(bool Succeeded, string Message);

// Page-local operation history state. Archive selection is a stable ID re-queried on each entry;
// undo eligibility and cross-archive scope come from the history service, not the UI.
public sealed class ArchiveHistoryState(IArchiveHistorySource source)
{
    private Guid? preferredArchiveId;

    public ArchiveHistorySnapshot Snapshot { get; private set; } = new();

    public Task EnterAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(cancellationToken);

    public Task SelectArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        return LoadAsync(cancellationToken);
    }

    public async Task SelectOperationAsync(
        Guid? changeSetId,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is null)
        {
            return;
        }

        Set(Snapshot with
        {
            SelectedChangeSetId = changeSetId,
            Revisions = [],
            RevisionStatus = changeSetId is null
                ? "选择一项操作查看修订。"
                : "正在读取修订…",
            Error = null
        });
        if (changeSetId is not Guid id)
        {
            return;
        }

        try
        {
            IReadOnlyList<GachaRevisionItem> gacha =
                await source.GetGachaRevisionsAsync(id, cancellationToken);
            IReadOnlyList<DomainRevisionItem> domain =
                await source.GetDomainRevisionsAsync(id, cancellationToken);
            List<ArchiveHistoryRevision> revisions =
            [
                .. gacha.Select(ToRevision),
                .. domain.Select(ToRevision)
            ];

            string status = revisions.Count == 0
                ? "此操作没有保留可显示的事实修订内容。"
                : $"已加载 {revisions.Count} 条修订" +
                    (revisions.Any(item => item.IsDomainSummary) ? "（含领域摘要）" : "") + "。";
            Set(Snapshot with
            {
                Revisions = revisions,
                RevisionStatus = status
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Set(Snapshot with
            {
                Revisions = [],
                RevisionStatus = "修订读取失败。",
                Error = ManagementTexts.DescribeOperationFailure(exception)
            });
        }
    }

    public async Task<ArchiveHistoryActionResult> UndoLatestAsync(
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            return new(false, "请先选择档案。");
        }

        ArchiveHistoryEntry? candidate = Snapshot.LatestUndoCandidate;
        if (candidate is null)
        {
            return new(false, "当前档案没有仍满足撤销资格的操作。");
        }

        try
        {
            ArchiveOperationResult result = await source.UndoLatestAsync(
                archiveId,
                OperationId.New(),
                cancellationToken);
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                await LoadAsync(cancellationToken, keepStatus: true);
            }

            return new(
                result.Status is ChangeExecutionStatus.Applied or
                    ChangeExecutionStatus.AlreadyCommitted,
                DescribeUndoResult(result));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<ArchiveHistoryActionResult> SetUndoLimitAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            return new(false, "请先选择档案。");
        }

        if (limit is < 0 or > 1000)
        {
            string message = "撤销保留次数必须为 0～1000 的整数。";
            Set(Snapshot with { Error = message });
            return new(false, message);
        }

        try
        {
            UndoLimitChangeResult result = await source.SetUndoLimitAsync(
                archiveId,
                limit,
                cancellationToken);
            await LoadAsync(cancellationToken, keepStatus: true);
            string message = result.EvictedChangeSetIds.Count == 0
                ? $"撤销保留次数已设为 {result.CurrentLimit}。"
                : $"撤销保留次数已设为 {result.CurrentLimit}，" +
                    $"并清理了 {result.EvictedChangeSetIds.Count} 份较早撤销材料。";
            Set(Snapshot with { Status = message, Error = null });
            return new(true, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public string BuildUndoConfirmationText()
    {
        ArchiveHistoryEntry? candidate = Snapshot.LatestUndoCandidate;
        if (candidate is null) return "当前档案没有仍满足撤销资格的操作。";
        string scope = candidate.IsCrossArchive
            ? $"此操作同时涉及多个档案（{candidate.Scope}）；撤销会整体作用于这些档案，" +
                "任一事实变化都会使整个撤销安全失败且不写入。"
            : "此操作仅涉及当前档案。";
        return $"将撤销：{candidate.Operation} · {candidate.Summary}" + Environment.NewLine +
            scope + Environment.NewLine +
            "撤销只作用于最新可撤销项；本页不提供热回切（不提供 Redo 或撤销撤销）。";
    }

    private async Task LoadAsync(
        CancellationToken cancellationToken,
        bool keepStatus = false)
    {
        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            Status = keepStatus ? Snapshot.Status : "正在读取操作历史…"
        });
        try
        {
            IReadOnlyList<PlayerArchive> archives = await source.GetArchivesAsync(cancellationToken);
            List<string> notes = [];
            Guid? archiveId = preferredArchiveId is Guid preferred &&
                archives.Any(archive => archive.Id == preferred)
                ? preferred
                : archives.FirstOrDefault()?.Id;
            if (preferredArchiveId is not null && archiveId != preferredArchiveId)
            {
                notes.Add(archives.Count == 0
                    ? "原选择档案已不存在，请创建档案。"
                    : "原选择档案已不存在，已回退到第一个可用档案。");
            }

            IReadOnlyList<ArchiveHistoryEntry> entries = [];
            int undoLimit = 0;
            if (archiveId is Guid selected)
            {
                IReadOnlyList<OperationHistoryItem> history =
                    await source.GetHistoryAsync(selected, 30, cancellationToken);
                entries = history.Select(ToEntry).ToArray();
                undoLimit = await source.GetUndoLimitAsync(selected, cancellationToken);
            }

            preferredArchiveId = archiveId;
            string archiveName = archives.FirstOrDefault(
                archive => archive.Id == archiveId)?.Name ?? "所选档案";
            string status = notes.Count > 0
                ? string.Join(Environment.NewLine, notes)
                : archiveId is null
                    ? "当前没有可选择的档案。"
                    : entries.Count == 0
                        ? $"档案“{archiveName}”尚无可显示的操作。"
                        : $"最近 {entries.Count} 项操作；撤销仅作用于最新可撤销项。";
            Set(new ArchiveHistorySnapshot
            {
                Archives = archives,
                SelectedArchiveId = archiveId,
                Entries = entries,
                SelectedChangeSetId = null,
                Revisions = [],
                UndoLimit = undoLimit,
                Status = status,
                RevisionStatus = "选择一项操作查看修订。"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Set(Snapshot with
            {
                IsBusy = false,
                Status = "操作历史读取失败；本地数据未改变。",
                Error = ManagementTexts.DescribeOperationFailure(exception)
            });
        }
    }

    private static ArchiveHistoryEntry ToEntry(OperationHistoryItem item) =>
        new(
            item.ChangeSetId,
            item.OperationKind,
            OperationLabel(item.OperationKind),
            item.CommittedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            item.Summary,
            $"{item.AffectedRecordCount} 条",
            UndoLabel(item),
            item.ArchiveIds.Count <= 1 ? "当前档案" : $"跨 {item.ArchiveIds.Count} 个档案",
            item.IsUndoEligible,
            item.IneligibilityReason);

    private static string OperationLabel(DataChangeOperationKind kind) => kind switch
    {
        DataChangeOperationKind.Import => "导入",
        DataChangeOperationKind.Refresh => "刷新",
        DataChangeOperationKind.Correction => "人工纠正",
        DataChangeOperationKind.Delete => "普通删除",
        DataChangeOperationKind.Undo => "撤销",
        DataChangeOperationKind.IrreversibleDelete => "永久删除",
        _ => kind.ToString()
    };

    private static string UndoLabel(OperationHistoryItem item) =>
        item.IsUndoEligible
            ? "可撤销"
            : item.IneligibilityReason switch
            {
                UndoIneligibilityReason.Disabled => "未保留撤销材料",
                UndoIneligibilityReason.CapacityEvicted => "撤销材料已清理",
                UndoIneligibilityReason.AlreadyUndone => "已撤销",
                UndoIneligibilityReason.Conflict => "当前事实已变化",
                UndoIneligibilityReason.IrreversibleDeletion => "永久删除，不可撤销",
                UndoIneligibilityReason.MissingMaterial => "撤销材料缺失",
                UndoIneligibilityReason.InverseOperation => "撤销操作不可再撤销",
                UndoIneligibilityReason.SupersededByArchiveReplace => "已由档案替换取代",
                _ => "不可撤销"
            };

    private static string DescribeUndoResult(ArchiveOperationResult result) =>
        result.Status switch
        {
            ChangeExecutionStatus.Applied =>
                $"操作已提交，影响 {result.AffectedRecordCount} 条记录。",
            ChangeExecutionStatus.AlreadyCommitted =>
                "此操作此前已经撤销，未重复写入。",
            ChangeExecutionStatus.NoOp =>
                "没有产生可撤销的变化。",
            ChangeExecutionStatus.Conflict =>
                "当前事实已变化，整个撤销安全失败且未写入。请重新载入。",
            ChangeExecutionStatus.Suppressed =>
                "命中永久删除抑制，未写入任何数据。",
            ChangeExecutionStatus.NeedsConfirmation =>
                "撤销需要清理较早撤销材料的确认；本页暂不提供该确认，未写入任何数据。",
            _ => "撤销未完成，未写入任何数据。"
        };

    private static ArchiveHistoryRevision ToRevision(GachaRevisionItem item) =>
        new(
            "抽卡记录",
            item.Reference.ExternalRecordId,
            item.ChangeKind switch
            {
                EntityChangeKind.Insert => "新增",
                EntityChangeKind.Update => "修改",
                EntityChangeKind.Delete => "删除",
                _ => item.ChangeKind.ToString()
            },
            FormatBefore(item.Before, item.BeforeVersion),
            FormatAfter(item.After, item.AfterVersion),
            item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            IsDomainSummary: false,
            Note: null);

    private static ArchiveHistoryRevision ToRevision(DomainRevisionItem item) =>
        new(
            "领域修订",
            $"{item.EntityKind} · {item.EntityReference}",
            item.ChangeKind switch
            {
                EntityChangeKind.Insert => "新增",
                EntityChangeKind.Update => "修改",
                EntityChangeKind.Delete => "删除",
                _ => item.ChangeKind.ToString()
            },
            SnapshotLabel(item.BeforeVersion, item.BeforeSnapshotJson),
            SnapshotLabel(item.AfterVersion, item.AfterSnapshotJson),
            item.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            IsDomainSummary: true,
            Note: "领域修订仅展示服务提供的实体、版本与快照摘要；本页不伪造字段级差异。");

    private static string FormatBefore(GachaRecord? record, FactVersion? version) =>
        GachaSnapshot(record, version, "无");

    private static string FormatAfter(GachaRecord? record, FactVersion? version) =>
        GachaSnapshot(record, version, "无（已删除）");

    private static string GachaSnapshot(GachaRecord? record, FactVersion? version, string empty) =>
        record is null
            ? empty
            : $"v{version?.Value}: {record.ItemName ?? record.ItemId ?? "未知物品"}" +
                $" / {record.RankType?.ToString() ?? "?"} 星" +
                $" / {record.Time:yyyy-MM-dd HH:mm:ss zzz}" +
                $" / 来源 {record.Provenance.Origin}";

    private static string SnapshotLabel(FactVersion? version, string? snapshot)
    {
        string versionLabel = version is null ? "版本未知" : $"v{version.Value.Value}";
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return $"{versionLabel}；服务未提供快照";
        }

        string text = snapshot.Length <= 400 ? snapshot : snapshot[..400] + "…";
        return $"{versionLabel}；快照原文：{text}";
    }

    private void Set(ArchiveHistorySnapshot snapshot) => Snapshot = snapshot;
}

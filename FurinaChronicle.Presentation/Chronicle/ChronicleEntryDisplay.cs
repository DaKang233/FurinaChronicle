// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Platform-independent display projection of one Chronicle entry. It keeps
/// time basis, provenance, evidence and completeness separate so a host can
/// render them as distinct labels instead of collapsing them into one date.
/// </summary>
public sealed record ChronicleEntryDisplay
{
    public required string EntryId { get; init; }

    public required string Title { get; init; }

    public required ChronicleTimeDisplay Time { get; init; }

    public required string AccountText { get; init; }

    public required string SourceText { get; init; }

    public string? AnnotationText { get; init; }

    public string? CompletenessText { get; init; }

    public string? ConfidenceText { get; init; }

    public required string EvidenceText { get; init; }

    public string? EvidenceScopeText { get; init; }

    public string? PityText { get; init; }

    public string? MilestoneText { get; init; }

    public string? ItemText { get; init; }

    public required string DomainText { get; init; }

    public required string KindText { get; init; }

    public required string ImportanceText { get; init; }

    public string? CoverageNoticeText { get; init; }

    public bool IsUndated => Time.IsUndated;

    public DateTimeOffset? SortAtUtc => Time.SortAtUtc;

    public string MonthKey => Time.IsUndated
        ? "undated"
        : Time.LocalSortAt?.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) ??
          "undated";

    public string MonthHeading => Time.IsUndated
        ? "未注明时间"
        : Time.LocalSortAt is { } local
            ? $"{local:yyyy 年 MM 月}"
            : "未注明时间";

    public string SummaryLine => string.Join(
        " · ",
        new[] { AccountText, EvidenceText }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    public IReadOnlyList<string> Details => new[]
        {
            Time.DetailText,
            AccountText,
            SourceText,
            AnnotationText,
            CompletenessText,
            ConfidenceText,
            EvidenceText,
            EvidenceScopeText,
            PityText,
            MilestoneText,
            CoverageNoticeText,
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!)
        .ToArray();

    public string DetailsText => string.Join("\n", Details);

    public static ChronicleEntryDisplay FromEntry(
        ChronicleEntry entry,
        ChronicleDisplayContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ChronicleDisplayContext effective = context ?? ChronicleDisplayContext.Empty;
        string? itemName = effective.ResolveItemName(entry);
        return new ChronicleEntryDisplay
        {
            EntryId = entry.EntryId.Value,
            Title = ChronicleDisplayText.Title(entry, effective),
            Time = ChronicleTimeDisplay.From(entry.Time, effective.DisplayTimeZone),
            AccountText = $"账号：{effective.AccountLabel(entry.GameAccountId)}",
            SourceText = $"来源：{ChronicleDisplayText.Origin(entry.BaseOrigin)}",
            AnnotationText = entry.AnnotationOrigin == entry.BaseOrigin
                ? null
                : $"派生注释：{ChronicleDisplayText.Origin(entry.AnnotationOrigin)}",
            CompletenessText = entry.Completeness == DataCompleteness.Unknown
                ? null
                : $"完整度：{ChronicleDisplayText.Completeness(entry.Completeness)}（按所属领域声明范围）",
            ConfidenceText = entry.Confidence == DataConfidence.Unknown
                ? null
                : $"可信度：{ChronicleDisplayText.Confidence(entry.Confidence)}",
            EvidenceText = $"依据：{ChronicleDisplayText.EvidenceReason(entry.Evidence.ReasonKey)}",
            EvidenceScopeText = entry.Evidence.Scope == ChronicleEvidenceScope.Unknown
                ? null
                : $"依据范围：{ChronicleDisplayText.EvidenceScope(entry.Evidence.Scope)}",
            PityText = Pity(entry),
            MilestoneText = ChronicleDisplayText.Milestone(entry.Milestone),
            ItemText = itemName,
            DomainText = $"领域：{ChronicleDisplayText.Domain(entry.Domain)}",
            KindText = $"类型：{ChronicleDisplayText.EntryKind(entry.Kind)}",
            ImportanceText = $"重要级别：{ChronicleDisplayText.Importance(entry.Importance)}",
            CoverageNoticeText = CoverageNotice(entry),
        };
    }

    private static string? Pity(ChronicleEntry entry)
    {
        if (entry.ExactPity is int exact)
        {
            return $"保底：距上一个五星 {exact} 抽（含本次五星）";
        }
        if (entry.LocalRecordedPulls is int local)
        {
            return $"保底：本地记录 {local} 抽（未声明完整历史）";
        }
        return null;
    }

    private static string? CoverageNotice(ChronicleEntry entry) => entry.Kind switch
    {
        ChronicleEntryKind.GachaAcquisition =>
            "数据说明：来源于本地抽卡记录，不包括活动赠送、自选、邮件、商店或其他非抽卡途径。",
        ChronicleEntryKind.ManualAcquisition =>
            "数据说明：用户手工录入的获取事实，来源为用户输入。",
        ChronicleEntryKind.ChallengeObservation =>
            "数据说明：本地已保存的挑战观察，不代表之后的状态。",
        ChronicleEntryKind.ChallengeCompleted =>
            "数据说明：完成判断由本地挑战观察派生。",
        ChronicleEntryKind.StateObservation or ChronicleEntryKind.StateChange =>
            "数据说明：本地已保存的角色或武器状态观察。",
        ChronicleEntryKind.AchievementUnlocked =>
            "数据说明：依据版本化重要成就目录。",
        _ => null,
    };
}

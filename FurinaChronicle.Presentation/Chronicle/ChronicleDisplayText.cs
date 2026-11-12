// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Stable keys emitted by the current Chronicle contributors. Display code must
/// map these keys to user-facing text; the key itself is never shown to the
/// user, including when a key is unknown.
/// </summary>
public static class ChronicleKeyCatalog
{
    public const string GachaAcquisitionTitle = "chronicle.gacha.acquisition";
    public const string ManualAcquisitionTitle = "chronicle.manual.acquisition";
    public const string ChallengeObservationTitle = "chronicle.challenge.observation";
    public const string ChallengeCompletedTitle = "chronicle.challenge.completed";
    public const string InventoryObservationTitle = "chronicle.inventory.observation";
    public const string InventoryStateChangeTitle = "chronicle.inventory.state-change";
    public const string AchievementUnlockedTitle = "chronicle.achievement.unlocked";

    public const string ItemIdUnknownReason = "chronicle.evidence.item-id-unknown";
    public const string OrderUnknownReason = "chronicle.evidence.order-unknown";
    public const string AcquisitionOrderAmbiguousReason =
        "chronicle.evidence.acquisition-order-ambiguous";
    public const string DeclaredCompleteOrderReason =
        "chronicle.evidence.declared-complete-order";
    public const string LocalRecordOrderReason =
        "chronicle.evidence.local-record-order";
    public const string SavedObservationReason =
        "chronicle.evidence.saved-observation";
    public const string SpiralAbyssCompletionReason =
        ChronicleVersions.SpiralAbyssCompletionRule;
    public const string CompleteAdjacentObservationsReason =
        "chronicle.evidence.complete-adjacent-observations";
    public const string CoveredAdjacentFieldsReason =
        "chronicle.evidence.covered-adjacent-fields";
    public const string ImportantAchievementCatalogReason =
        "chronicle.evidence.important-achievement-catalog";

    public const string FirstAcquisitionMilestone = "first-acquisition";
    public const string LocalFirstRecordMilestone = "local-first-record";
    public const string SeventhAcquisitionMilestone = "seventh-acquisition";
    public const string LocalSeventhRecordMilestone = "local-seventh-record";
    public const string ChallengeCompletedMilestone = "challenge-completed";
    public const string ImportantAchievementMilestone = "important-achievement";

    private static readonly HashSet<string> TitleKeys =
        new(StringComparer.Ordinal)
        {
            GachaAcquisitionTitle,
            ManualAcquisitionTitle,
            ChallengeObservationTitle,
            ChallengeCompletedTitle,
            InventoryObservationTitle,
            InventoryStateChangeTitle,
            AchievementUnlockedTitle,
        };

    private static readonly HashSet<string> EvidenceReasonKeys =
        new(StringComparer.Ordinal)
        {
            ItemIdUnknownReason,
            OrderUnknownReason,
            AcquisitionOrderAmbiguousReason,
            DeclaredCompleteOrderReason,
            LocalRecordOrderReason,
            SavedObservationReason,
            SpiralAbyssCompletionReason,
            CompleteAdjacentObservationsReason,
            CoveredAdjacentFieldsReason,
            ImportantAchievementCatalogReason,
        };

    private static readonly HashSet<string> MilestoneKeys =
        new(StringComparer.Ordinal)
        {
            FirstAcquisitionMilestone,
            LocalFirstRecordMilestone,
            SeventhAcquisitionMilestone,
            LocalSeventhRecordMilestone,
            ChallengeCompletedMilestone,
            ImportantAchievementMilestone,
        };

    public static IReadOnlyCollection<string> KnownTitleKeys => TitleKeys;

    public static IReadOnlyCollection<string> KnownEvidenceReasonKeys =>
        EvidenceReasonKeys;

    public static IReadOnlyCollection<string> KnownMilestoneKeys => MilestoneKeys;

    public static bool IsKnownTitleKey(string? key) =>
        key is not null && TitleKeys.Contains(key);

    public static bool IsKnownEvidenceReasonKey(string? key) =>
        key is not null && EvidenceReasonKeys.Contains(key);

    public static bool IsKnownMilestoneKey(string? key) =>
        key is not null && MilestoneKeys.Contains(key);
}

/// <summary>
/// Chinese display wording shared by MAUI and future WinUI Chronicle pages.
/// Every method has a safe fallback: unknown keys and enum values degrade to a
/// generic description or "未知" and never leak a localization key or raw enum
/// name to the user.
/// </summary>
public static class ChronicleDisplayText
{
    public static string Title(ChronicleEntry entry, ChronicleDisplayContext context)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(context);

        string? milestone = Milestone(entry.Milestone);
        string title = entry.TitleKey switch
        {
            ChronicleKeyCatalog.GachaAcquisitionTitle =>
                Acquisition(entry, context, milestone),
            ChronicleKeyCatalog.ManualAcquisitionTitle =>
                ManualAcquisition(entry, context, milestone),
            ChronicleKeyCatalog.ChallengeObservationTitle =>
                Observation(entry),
            ChronicleKeyCatalog.ChallengeCompletedTitle => Completed(entry),
            ChronicleKeyCatalog.InventoryObservationTitle => InventoryObservation(entry),
            ChronicleKeyCatalog.InventoryStateChangeTitle =>
                InventoryStateChange(entry, context),
            ChronicleKeyCatalog.AchievementUnlockedTitle => Achievement(entry),
            _ => UnknownTitle(entry),
        };
        return title;
    }

    private static string Acquisition(
        ChronicleEntry entry,
        ChronicleDisplayContext context,
        string? milestone)
    {
        string item = context.ResolveItemName(entry) ?? "未知物品";
        bool weapon = IsWeaponEntry(entry);
        string sentence = entry.RankType switch
        {
            5 when weapon => $"获得五星武器 · {item}",
            5 => $"获得五星角色 · {item}",
            4 when !weapon => $"获得四星角色 · {item}",
            int rank when weapon => $"获得{rank}星武器 · {item}",
            int rank => $"获得{rank}星角色 · {item}",
            _ when weapon => $"获得未知星级武器 · {item}",
            _ => $"获得未知星级物品 · {item}",
        };
        return AppendMilestone(sentence, milestone);
    }

    private static string ManualAcquisition(
        ChronicleEntry entry,
        ChronicleDisplayContext context,
        string? milestone)
    {
        string item = context.ResolveItemName(entry) ?? "未知物品";
        string sentence = IsWeaponEntry(entry)
            ? $"手工记录武器获取 · {item}"
            : $"手工记录角色获取 · {item}";
        return AppendMilestone(sentence, milestone);
    }

    private static string Observation(ChronicleEntry entry) =>
        $"{ChallengeMode(entry)} · 第 {SeasonId(entry)} 期 · 挑战观察";

    private static string Completed(ChronicleEntry entry) =>
        $"完成{ChallengeMode(entry)} · 第 {SeasonId(entry)} 期";

    private static string InventoryObservation(ChronicleEntry entry)
    {
        string kind = InventoryKind(entry) switch
        {
            InventoryItemKind.Character => "角色",
            InventoryItemKind.Weapon => "武器",
            _ => "角色或武器",
        };
        string count = entry.SummaryArguments.TryGetValue("entry_count", out string? value) &&
            int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? $" · {parsed} 条"
                : string.Empty;
        return $"{kind}状态观察{count}";
    }

    private static string InventoryStateChange(
        ChronicleEntry entry,
        ChronicleDisplayContext context)
    {
        string kind = InventoryKind(entry) switch
        {
            InventoryItemKind.Character => "角色",
            InventoryItemKind.Weapon => "武器",
            _ => "角色或武器",
        };
        string change = entry.SummaryArguments.TryGetValue("change", out string? value)
            ? value switch
            {
                "added" => "新增",
                "removed" => "移除",
                "changed" => "字段变化",
                _ => "状态变化",
            }
            : "状态变化";
        string? item = context.ResolveItemName(entry);
        return item is null
            ? $"{kind}状态变化 · {change}"
            : $"{kind}状态变化 · {change} · {item}";
    }

    private static string Achievement(ChronicleEntry entry)
    {
        string sentence = "解锁重要成就";
        if (entry.SummaryArguments.TryGetValue("achievement_id", out string? id) &&
            !string.IsNullOrWhiteSpace(id))
        {
            sentence += $" · 编号 {id.Trim()}";
        }
        return sentence;
    }

    private static string UnknownTitle(ChronicleEntry entry)
    {
        string kind = EntryKind(entry.Kind);
        return kind == "未知"
            ? "未能识别的历程节点"
            : $"{kind} · 未能识别的历程描述";
    }

    private static string AppendMilestone(string sentence, string? milestone) =>
        milestone is null ? sentence : $"{sentence}（{milestone}）";

    public static string? Milestone(string? milestoneKey) => milestoneKey switch
    {
        null or "" => null,
        ChronicleKeyCatalog.FirstAcquisitionMilestone => "首次获取",
        ChronicleKeyCatalog.LocalFirstRecordMilestone => "本地首次记录",
        ChronicleKeyCatalog.SeventhAcquisitionMilestone => "第七次获取（满命）",
        ChronicleKeyCatalog.LocalSeventhRecordMilestone => "本地第七次记录（满命）",
        ChronicleKeyCatalog.ChallengeCompletedMilestone => "完成节点",
        ChronicleKeyCatalog.ImportantAchievementMilestone => "重要成就",
        _ => null,
    };

    public static string EvidenceReason(string? reasonKey) => reasonKey switch
    {
        ChronicleKeyCatalog.ItemIdUnknownReason =>
            "条目缺少稳定物品编号，无法判断获取序号。",
        ChronicleKeyCatalog.OrderUnknownReason =>
            "当前档案记录不足以确认获取序号。",
        ChronicleKeyCatalog.AcquisitionOrderAmbiguousReason =>
            "同一物品的获取顺序无法从本地记录唯一确认。",
        ChronicleKeyCatalog.DeclaredCompleteOrderReason =>
            "获取序号依据声明完整的抽卡历史计算。",
        ChronicleKeyCatalog.LocalRecordOrderReason =>
            "获取序号只依据当前档案的本地记录。",
        ChronicleKeyCatalog.SavedObservationReason =>
            "依据本地已保存的领域观察。",
        ChronicleKeyCatalog.SpiralAbyssCompletionReason =>
            "依据深境螺旋第 12 层第 3 间完成证据。",
        ChronicleKeyCatalog.CompleteAdjacentObservationsReason =>
            "两次相邻观察都完整，差异按覆盖字段比较。",
        ChronicleKeyCatalog.CoveredAdjacentFieldsReason =>
            "仅在两次观察共同覆盖的字段中发现差异。",
        ChronicleKeyCatalog.ImportantAchievementCatalogReason =>
            "依据版本化的重要成就目录。",
        null or "" => "未提供依据说明。",
        _ => "依据说明不可用（未知依据类型）。",
    };

    public static string EvidenceStatus(ChronicleEvidenceStatus status) => status switch
    {
        ChronicleEvidenceStatus.Known => "依据明确",
        ChronicleEvidenceStatus.Ambiguous => "依据不确定",
        ChronicleEvidenceStatus.Unknown => "依据未知",
        _ => "依据未知",
    };

    public static string EvidenceScope(ChronicleEvidenceScope scope) => scope switch
    {
        ChronicleEvidenceScope.LocalArchiveRecords => "本地档案记录",
        ChronicleEvidenceScope.LocalGachaRecords => "本地抽卡记录",
        ChronicleEvidenceScope.DeclaredCompleteHistory => "声明完整的历史",
        ChronicleEvidenceScope.AccountHistory => "账号历史",
        ChronicleEvidenceScope.Unknown => "未知范围",
        _ => "未知范围",
    };

    public static string Origin(DataOrigin origin) => origin switch
    {
        DataOrigin.OfficialApi => "官方 API",
        DataOrigin.StandardImport => "标准导入",
        DataOrigin.FurinaImport => "Furina 导入",
        DataOrigin.LocalObservation => "本地观察",
        DataOrigin.LocalCollector => "本地采集",
        DataOrigin.UserEntered => "用户录入",
        DataOrigin.Derived => "派生结果",
        DataOrigin.Unknown => "未知来源",
        _ => "未知来源",
    };

    public static string Confidence(DataConfidence confidence) => confidence switch
    {
        DataConfidence.Low => "低",
        DataConfidence.Medium => "中",
        DataConfidence.High => "高",
        DataConfidence.Confirmed => "已确认",
        DataConfidence.Unknown => "未知",
        _ => "未知",
    };

    public static string Completeness(DataCompleteness completeness) => completeness switch
    {
        DataCompleteness.Partial => "部分",
        DataCompleteness.Complete => "完整",
        DataCompleteness.Unknown => "未知",
        _ => "未知",
    };

    public static string Importance(ChronicleImportance importance) => importance switch
    {
        ChronicleImportance.Minor => "次要",
        ChronicleImportance.Normal => "一般",
        ChronicleImportance.Major => "重要",
        ChronicleImportance.Critical => "关键",
        _ => "未知",
    };

    public static string Domain(ChronicleDomain domain) => domain switch
    {
        ChronicleDomain.Gacha => "抽卡",
        ChronicleDomain.Acquisition => "获取",
        ChronicleDomain.Inventory => "角色与武器",
        ChronicleDomain.Challenge => "挑战",
        ChronicleDomain.Achievement => "成就",
        _ => "未知领域",
    };

    public static string EntryKind(ChronicleEntryKind kind) => kind switch
    {
        ChronicleEntryKind.GachaAcquisition => "抽卡获取",
        ChronicleEntryKind.ManualAcquisition => "手工获取",
        ChronicleEntryKind.StateObservation => "状态观察",
        ChronicleEntryKind.StateChange => "状态变化",
        ChronicleEntryKind.ChallengeObservation => "挑战观察",
        ChronicleEntryKind.ChallengeCompleted => "挑战完成",
        ChronicleEntryKind.AchievementUnlocked => "成就解锁",
        _ => "未知",
    };

    public static string Timeline(ChronicleTimeline timeline) => timeline switch
    {
        ChronicleTimeline.Main => "主时间线",
        ChronicleTimeline.Character => "角色时间线",
        ChronicleTimeline.Weapon => "武器时间线",
        ChronicleTimeline.Challenge => "挑战时间线",
        ChronicleTimeline.None => "无时间线",
        _ => "未知时间线",
    };

    public static string Granularity(ChronicleGranularity granularity) => granularity switch
    {
        ChronicleGranularity.Default => "默认",
        ChronicleGranularity.Detailed => "详细",
        _ => "未知",
    };

    public static string SortBasis(ChronicleSortBasis basis) => basis switch
    {
        ChronicleSortBasis.Occurred => "发生时间",
        ChronicleSortBasis.Observed => "观察时间",
        ChronicleSortBasis.Fetched => "采集时间",
        ChronicleSortBasis.Unknown => "无排序锚点",
        _ => "无排序锚点",
    };

    public static string ChallengeMode(string? mode) => mode switch
    {
        "SpiralAbyss" => "深境螺旋",
        "ImaginariumTheater" => "幻想真境剧诗",
        "StygianOnslaught" => "幽境危战",
        null or "" => "未知挑战模式",
        _ => "未知挑战模式",
    };

    private static string ChallengeMode(ChronicleEntry entry) =>
        entry.SummaryArguments.TryGetValue("mode", out string? mode)
            ? ChallengeMode(mode)
            : "未知挑战模式";

    private static string SeasonId(ChronicleEntry entry) =>
        entry.SummaryArguments.TryGetValue("season_id", out string? season) &&
        !string.IsNullOrWhiteSpace(season)
            ? season.Trim()
            : "未知期次";

    private static InventoryItemKind? InventoryKind(ChronicleEntry entry)
    {
        if (entry.SummaryArguments.TryGetValue("item_kind", out string? kind))
        {
            return kind switch
            {
                "Character" => InventoryItemKind.Character,
                "Weapon" => InventoryItemKind.Weapon,
                _ => null,
            };
        }
        if (entry.SummaryArguments.TryGetValue("item_key", out string? key) &&
            ChronicleItemKey.TryParse(key, out InventoryItemKind parsed, out _))
        {
            return parsed;
        }
        bool weapon = (entry.Timelines & ChronicleTimeline.Weapon) != 0 &&
            (entry.Timelines & ChronicleTimeline.Character) == 0;
        return weapon ? InventoryItemKind.Weapon : null;
    }

    private static bool IsWeaponEntry(ChronicleEntry entry) =>
        (entry.Timelines & ChronicleTimeline.Character) == 0 &&
        (entry.Timelines & ChronicleTimeline.Weapon) != 0;
}

/// <summary>
/// Display wording for page/load state. Kept out of the state machine so the
/// same state can be tested without UI and reused on another host.
/// </summary>
public static class ChronicleStatusText
{
    public static string DescribeLoadState(ChronicleLoadState state, int loadedCount) =>
        state switch
        {
            ChronicleLoadState.Idle => "尚未载入游戏历程。",
            ChronicleLoadState.LoadingFirstPage => "正在载入游戏历程…",
            ChronicleLoadState.LoadingMore => "正在载入更早的历程…",
            ChronicleLoadState.Loaded => $"已载入 {loadedCount} 条历程。",
            ChronicleLoadState.Empty => "当前条件下没有可显示的历程记录。",
            ChronicleLoadState.Error => "读取游戏历程时遇到问题。",
            _ => "尚未载入游戏历程。",
        };

    public static string DescribeStale(ChronicleStaleReason reason) => reason switch
    {
        ChronicleStaleReason.None => string.Empty,
        ChronicleStaleReason.CursorInvalidated =>
            "时间线数据已被纠正、删除或撤销，当前浏览内容不再有效。请重新载入。",
        ChronicleStaleReason.CursorExpired =>
            "当前浏览快照已过期，新数据可能已生成。为避免混用不同快照，请重新载入。",
        ChronicleStaleReason.CursorInvalid =>
            "分页游标无效或与当前筛选不匹配。请重新载入。",
        ChronicleStaleReason.CursorVersionMismatch =>
            "分页游标版本不受支持。请重新载入。",
        ChronicleStaleReason.GenerationChanged =>
            "时间线已切换到新数据快照。为避免混用不同快照，请重新载入。",
        _ => string.Empty,
    };
}

public enum ChronicleEmptyReason
{
    None = 0,
    NoArchive = 1,
    NoAccount = 2,
    NoData = 3,
    FilteredOut = 4,
}

public static class ChronicleEmptyStateText
{
    public static string Describe(ChronicleEmptyReason reason) => reason switch
    {
        ChronicleEmptyReason.NoArchive =>
            "还没有本地档案。请先在“档案与账号管理”中创建档案。",
        ChronicleEmptyReason.NoAccount =>
            "当前档案还没有游戏账号。请先在“档案与账号管理”中添加账号。",
        ChronicleEmptyReason.NoData =>
            "当前档案还没有足够的领域数据来生成游戏历程。导入或刷新抽卡、角色与武器或挑战记录后再查看。",
        ChronicleEmptyReason.FilteredOut =>
            "当前筛选条件下没有历程记录。可以放宽时间范围、时间线或星级筛选后重试。",
        _ => "当前条件下没有可显示的历程记录。",
    };
}

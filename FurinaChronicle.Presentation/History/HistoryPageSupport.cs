// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

/// <summary>Selection option shown by a WinUI ComboBox without leaking domain types.</summary>
public sealed record HistoryOption<T>(T Value, string Label);

public sealed record HistoryChoice(Guid Id, string Label);

/// <summary>
/// Metadata placeholder for the isolated host and for callers that have no
/// metadata source. Names fall back to the raw stable type id; a missing name
/// never fails an observation query or hides the entry.
/// </summary>
public sealed class NullDomainMetadataService : IDomainMetadataService
{
    public static NullDomainMetadataService Instance { get; } = new();

    private NullDomainMetadataService()
    {
    }

    public ValueTask<DomainItemMetadata?> ResolveItemAsync(
        InventoryItemKind kind,
        string typeId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<DomainItemMetadata?>(null);

    public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(
        ChallengeMode mode,
        string seasonId,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ChallengeScheduleMetadata?>(null);
}

public static class HistoryLabels
{
    public static string Origin(DataOrigin origin) => origin switch
    {
        DataOrigin.OfficialApi => "官方 API",
        DataOrigin.StandardImport => "标准导入",
        DataOrigin.FurinaImport => "Furina 导入",
        DataOrigin.LocalObservation => "本地观察",
        DataOrigin.LocalCollector => "本地采集",
        DataOrigin.UserEntered => "用户录入",
        DataOrigin.Derived => "派生结果",
        _ => "未知来源",
    };

    public static string Completeness(DataCompleteness completeness, string scope) =>
        completeness switch
        {
            DataCompleteness.Complete => $"完整（按{scope}）",
            DataCompleteness.Partial => $"部分（按{scope}）",
            _ => $"覆盖未知（按{scope}）",
        };

    public static string Kind(InventoryItemKind kind) => kind switch
    {
        InventoryItemKind.Character => "角色",
        InventoryItemKind.Weapon => "武器",
        _ => "未知类别",
    };

    public static string Mode(ChallengeMode mode) => mode switch
    {
        ChallengeMode.ImaginariumTheater => "幻想真境剧诗",
        ChallengeMode.StygianOnslaught => "幽境危战",
        _ => "深境螺旋",
    };

    public static string ServerRegion(GameServerRegion region) => region switch
    {
        GameServerRegion.ChinaOfficial => "天空岛（官服）",
        GameServerRegion.ChinaBilibili => "世界树（B服）",
        GameServerRegion.America => "美服",
        GameServerRegion.Europe => "欧服",
        GameServerRegion.Asia => "亚服",
        GameServerRegion.TaiwanHongKongMacao => "港澳台服",
        _ => "未知区服",
    };

    public static string AccountLabel(GameAccount account) =>
        $"UID {account.Uid} · {ServerRegion(account.ServerRegion)}";

    public static string ArchiveChoice(PlayerArchive archive) => archive.Name;

    public static string Timestamp(DateTimeOffset? value) => value is { } time
        ? time.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
        : "未知";

    public static string ObservedText(RecordProvenance provenance) =>
        provenance.Timestamps.ObservedAt is { } observed
            ? $"观察于 {Timestamp(observed)}"
            : "观察时间未知";

    public static string FetchedText(RecordProvenance provenance) =>
        provenance.Timestamps.FetchedAt is { } fetched
            ? $"采集于 {Timestamp(fetched)}"
            : "采集时间未知";
}

/// <summary>
/// Display projection for one item state observation. It reuses the shared
/// character/weapon card wording and keeps occurrence, observation, source and
/// completeness labels separate.
/// </summary>
public sealed record CharacterWeaponObservationView
{
    public required Guid ObservationId { get; init; }

    public required string Title { get; init; }

    public required string Evidence { get; init; }

    public required string Detail { get; init; }

    public required string KindLabel { get; init; }

    public required string SourceText { get; init; }

    public required string OccurredText { get; init; }

    public required string ObservedText { get; init; }

    public required string FetchedText { get; init; }

    public required string CompletenessText { get; init; }

    public required string EvidenceLabel { get; init; }

    public required string AccountLabel { get; init; }

    public required string MissingFieldsNote { get; init; }

    public string DetailText => string.Join(
        Environment.NewLine,
        new[]
        {
            AccountLabel,
            OccurredText,
            ObservedText,
            FetchedText,
            CompletenessText,
            SourceText,
            EvidenceLabel,
            MissingFieldsNote,
            Detail,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

/// <summary>
/// Display projection for one challenge observation. Occurrence, observation,
/// fetch and completeness remain distinct; missing names keep the stable id.
/// </summary>
public sealed record ChallengeObservationView
{
    public required Guid ObservationId { get; init; }

    public required string Title { get; init; }

    public required string Evidence { get; init; }

    public required string Detail { get; init; }

    public required string ModeLabel { get; init; }

    public required string SeasonId { get; init; }

    public required string OccurredText { get; init; }

    public required string ObservedText { get; init; }

    public required string FetchedText { get; init; }

    public required string CompletenessText { get; init; }

    public required string SourceText { get; init; }

    public required string DifficultyText { get; init; }

    public required string MedalText { get; init; }

    public required string StarsText { get; init; }

    public required string SecondsText { get; init; }

    public required string AccountLabel { get; init; }

    public string DetailText => string.Join(
        Environment.NewLine,
        new[]
        {
            AccountLabel,
            OccurredText,
            ObservedText,
            FetchedText,
            CompletenessText,
            SourceText,
            DifficultyText,
            MedalText,
            StarsText,
            SecondsText,
            Evidence,
            Detail,
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
}

public static class HistoryObservationViewBuilder
{
    public static async Task<CharacterWeaponObservationView> CharacterWeaponAsync(
        ItemStateObservation observation,
        IDomainMetadataService metadata,
        string accountLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(metadata);
        CharacterObservationCard card = await CharacterObservationCard
            .FromObservationAsync(observation, metadata, cancellationToken);
        var time = ChronicleTimeDisplay.From(
            new ChronicleTime(
                occurred: null,
                observation.Provenance.Timestamps.ObservedAt,
                observation.Provenance.Timestamps.FetchedAt),
            TimeZoneInfo.Local);
        int unknownLevel = observation.Entries.Count(entry => entry.Level is null);
        int unknownProgression = observation.Entries.Count(
            entry => entry.Progression is null);
        string missingFields = unknownLevel == 0 && unknownProgression == 0
            ? "缺失字段：无（该次观察覆盖字段可见）。"
            : $"缺失字段：{unknownLevel} 条缺少等级、{unknownProgression} 条缺少命座/精炼；" +
              "界面保持未知，不推断。";
        return new CharacterWeaponObservationView
        {
            ObservationId = observation.ObservationId.Value,
            Title = card.Title,
            Evidence = card.Evidence,
            Detail = card.Detail,
            KindLabel = HistoryLabels.Kind(observation.Kind),
            SourceText = $"来源：{HistoryLabels.Origin(observation.Provenance.Origin)}",
            OccurredText = "游戏内发生时间：不适用（角色/武器状态观察）",
            ObservedText = HistoryLabels.ObservedText(observation.Provenance),
            FetchedText = HistoryLabels.FetchedText(observation.Provenance),
            CompletenessText = $"完整度：{HistoryLabels.Completeness(observation.CollectionCompleteness, "该次观察范围")}",
            EvidenceLabel = $"证据：{card.Evidence}",
            AccountLabel = accountLabel,
            MissingFieldsNote = missingFields,
        };
    }

    public static async Task<ChallengeObservationView> ChallengeAsync(
        ChallengeObservation observation,
        IDomainMetadataService metadata,
        string accountLabel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(metadata);
        ChallengeScheduleMetadata? schedule = await metadata.ResolveScheduleAsync(
            observation.Mode,
            observation.SeasonId,
            cancellationToken);
        ChallengeObservationCard card = ChallengeObservationCard.FromObservation(
            observation,
            schedule?.Name);
        OccurrenceTime? occurred = Occurrence(observation);
        var time = ChronicleTimeDisplay.From(
            new ChronicleTime(
                occurred,
                observation.Provenance.Timestamps.ObservedAt,
                observation.Provenance.Timestamps.FetchedAt),
            TimeZoneInfo.Local);
        int stars = observation.SpiralAbyssFloors.Sum(floor => floor.Stars);
        int? bestSeconds = null;
        if (observation.Mode == ChallengeMode.StygianOnslaught)
        {
            int[] scores = observation.Stages
                .Where(stage => stage.Score is >= 0)
                .Select(stage => stage.Score!.Value)
                .ToArray();
            if (scores.Length > 0)
            {
                bestSeconds = scores.Min();
            }
        }
        return new ChallengeObservationView
        {
            ObservationId = observation.ObservationId.Value,
            Title = card.Title,
            Evidence = card.Evidence,
            Detail = card.Detail,
            ModeLabel = HistoryLabels.Mode(observation.Mode),
            SeasonId = observation.SeasonId,
            OccurredText = occurred is null ||
                observation.Mode == ChallengeMode.StygianOnslaught
                    ? "游戏内发生时间：未知（只以观察/采集时间排序）"
                    : time.PrimaryText,
            ObservedText = HistoryLabels.ObservedText(observation.Provenance),
            FetchedText = HistoryLabels.FetchedText(observation.Provenance),
            CompletenessText = $"完整度：{HistoryLabels.Completeness(observation.Completeness, "该模式/期次观察范围")}",
            SourceText = $"来源：{HistoryLabels.Origin(observation.Provenance.Origin)}",
            DifficultyText = observation.Difficulty is int difficulty
                ? $"难度：{difficulty}"
                : "难度：未知",
            MedalText = observation.MedalCount is int medals
                ? $"奖章：{medals}"
                : "奖章：未知",
            StarsText = observation.Mode == ChallengeMode.SpiralAbyss
                ? $"星数：{stars}"
                : "星数：不适用",
            SecondsText = observation.Mode == ChallengeMode.StygianOnslaught
                ? bestSeconds is int seconds
                    ? $"秒数成绩：最快 {seconds} 秒"
                    : "秒数成绩：未知"
                : "秒数成绩：不适用",
            AccountLabel = accountLabel,
        };
    }

    private static OccurrenceTime? Occurrence(ChallengeObservation observation)
    {
        if (observation.Mode == ChallengeMode.SpiralAbyss)
        {
            SpiralAbyssChamberResult? last = observation.SpiralAbyssFloors
                .SelectMany(floor => floor.Chambers)
                .Where(chamber => chamber.CompletedAt.Kind != OccurrenceTimeKind.Unknown)
                .OrderByDescending(chamber => chamber.CompletedAt.UtcEnd ??
                    chamber.CompletedAt.UtcStart)
                .FirstOrDefault();
            return last?.CompletedAt;
        }
        if (observation.Mode == ChallengeMode.ImaginariumTheater)
        {
            ChallengeStageResult? last = observation.Stages
                .Where(stage => stage.CompletedAt.Kind != OccurrenceTimeKind.Unknown)
                .OrderByDescending(stage => stage.CompletedAt.UtcEnd ??
                    stage.CompletedAt.UtcStart)
                .FirstOrDefault();
            return last?.CompletedAt;
        }
        return null;
    }
}

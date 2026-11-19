// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

public sealed partial record ChallengeObservationCard
{
    private static IEnumerable<ChallengeStageResult> StagesForDisplay(ChallengeObservation observation) =>
        observation.Mode != ChallengeMode.ImaginariumTheater ? observation.Stages :
        observation.Stages.OrderBy(stage => stage.TheaterRound?.IsTarot == true ? 1 : 0)
            .ThenBy(stage => stage.TheaterRound?.IsTarot == true && stage.TheaterRound.TarotSerialNumber is > 0
                ? stage.TheaterRound.TarotSerialNumber.Value
                : int.TryParse(stage.StageId, NumberStyles.None, CultureInfo.InvariantCulture, out int id) ? id : int.MaxValue)
            .ThenBy(stage => stage.StageId, StringComparer.Ordinal);

    private static string TheaterStageTitle(ChallengeStageResult stage) =>
        stage.TheaterRound?.IsTarot == true
            ? $"塔罗挑战 {(stage.TheaterRound.TarotSerialNumber is > 0 ? stage.TheaterRound.TarotSerialNumber.Value.ToString(CultureInfo.InvariantCulture) : stage.StageId)}"
            : int.TryParse(stage.StageId, out int id) ? $"第 {id} 幕" : stage.StageId;

    private static string DescribeParticipant(ChallengeParticipant participant, string? name = null) =>
        DescribeCombatant(name ?? participant.Name ?? participant.Id, participant.Level) +
        (participant.Constellation is int constellation ? $" · {constellation} 命" : "") +
        (participant.Origin switch
        {
            ChallengeParticipantOrigin.Owned => " · 自有",
            ChallengeParticipantOrigin.Trial => " · 试用",
            ChallengeParticipantOrigin.Support => " · 助演",
            _ => string.Empty,
        });

    private static async Task AddTheaterDetailsAsync(
        List<ChallengeDetailSection> sections, ChallengeObservation observation,
        ChallengeScheduleMetadata? schedule, IDomainMetadataService metadata,
        Dictionary<string, DomainItemMetadata?> names, CancellationToken cancellationToken)
    {
        if (schedule?.Theater is { } setup)
        {
            if (setup.AllowedElements.Count > 0)
                sections.Add(new ChallengeDetailSection("本期配置",
                    ["准入元素：" + string.Join("、", setup.AllowedElements.Select(ElementName))], []) { Kind = ChallengeSectionKind.TheaterSetup });
            await AddRosterSectionAsync(sections, "初始角色（期次配置）",
                setup.InitialAvatarIds.Select(id => new ChallengeParticipant(id, null, null, null, null)).ToArray(),
                metadata, names, cancellationToken);
            await AddRosterSectionAsync(sections, "特邀角色（期次配置）",
                setup.SpecialAvatarIds.Select(id => new ChallengeParticipant(id, null, null, null, null)).ToArray(),
                metadata, names, cancellationToken);
        }
        if (observation.TheaterStatistics is not { } statistics) return;
        var lines = new List<string>();
        if (statistics.MaxRoundId is int round) lines.Add($"最高幕次：{round}");
        if (statistics.Heraldry is int heraldry) lines.Add($"演出徽记：{heraldry}");
        if (statistics.CoinCount is int coins) lines.Add($"消耗「幻剧之花」：{coins}");
        if (statistics.AvatarBonusCount is int bonus) lines.Add($"场外声援：{bonus}");
        if (statistics.RentCount is int rents) lines.Add($"助演次数：{rents}");
        if (statistics.TarotFinishedCount is int tarot) lines.Add($"完成塔罗挑战：{tarot}");
        if (statistics.MedalRounds.Count > 0) lines.Add("获奖章幕次：" + string.Join("、", statistics.MedalRounds));
        if (statistics.TotalUseTime is int seconds)
        {
            TimeSpan duration = TimeSpan.FromSeconds(seconds);
            lines.Add($"演出总时长：{seconds} 秒（{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}）");
        }
        if (statistics.TotalCoinsConsumed is { } consumed)
            lines.Add($"消耗「幻剧之花」（战斗统计）：{consumed}");
        if (statistics.ShowBattleStats == false) lines.Add("官方未开放本次战斗统计。");
        if (lines.Count > 0) sections.Add(new ChallengeDetailSection("演出总览", lines, []) { Kind = ChallengeSectionKind.TheaterSummary });
        await AddRosterSectionAsync(sections, "候补角色", statistics.BackupAvatars,
            metadata, names, cancellationToken);
        if (statistics.ShowBattleStats == false) return;
        (string Title, TheaterAvatarStatistic? Value)[] metrics =
        [
            ("最多击破", statistics.MaxDefeat), ("最强一击", statistics.MaxDamage),
            ("承受伤害", statistics.MaxTakeDamage),
        ];
        foreach (var metric in metrics.Where(metric => metric.Value is not null))
            sections.Add(await TheaterMetricSectionAsync(metric.Title, [metric.Value!], metadata, names, cancellationToken));
        if (statistics.ShortestAvatars.Count > 0)
            sections.Add(await TheaterMetricSectionAsync("最快演出", statistics.ShortestAvatars, metadata, names, cancellationToken));
    }

    private static async Task AddRosterSectionAsync(
        List<ChallengeDetailSection> sections, string title, IReadOnlyList<ChallengeParticipant> roster,
        IDomainMetadataService metadata, Dictionary<string, DomainItemMetadata?> names,
        CancellationToken cancellationToken)
    {
        if (roster.Count == 0) return;
        var groups = await BuildPortraitGroupsAsync(roster, [], metadata, names, cancellationToken);
        sections.Add(new ChallengeDetailSection(title, [],
            [new ChallengeCombatDetail(string.Empty, [], groups.Select(group => group with { Title = string.Empty }).ToArray())]) { Kind = ChallengeSectionKind.TheaterRoster });
    }

    private static async Task<ChallengeDetailSection> TheaterMetricSectionAsync(
        string title, IReadOnlyList<TheaterAvatarStatistic> metrics, IDomainMetadataService metadata,
        Dictionary<string, DomainItemMetadata?> names, CancellationToken cancellationToken)
    {
        var groups = await BuildPortraitGroupsAsync(metrics.Select(metric => metric.Participant).ToArray(),
            [], metadata, names, cancellationToken);
        var portraits = groups[0].Portraits.Select((portrait, index) => portrait with
        {
            Label = portrait.Label + " · " + (metrics[index].Value ?? "未知"),
            IsSidePortrait = true,
        }).ToArray();
        return new ChallengeDetailSection(title, [],
            [new ChallengeCombatDetail(string.Empty, [], [new ChallengePortraitGroup(string.Empty, portraits)])])
        {
            Kind = ChallengeSectionKind.Metric,
            Metrics = metrics.Select((metric, index) => new ChallengeMetric(title, metric.Value ?? "未知", portraits[index])).ToArray(),
        };
    }

    private static IReadOnlyList<ChallengeBuffDetail> BuildTheaterBuffCards(ChallengeStageResult stage) =>
        (stage.TheaterRound?.BuffDetails ?? []).Select(buff =>
        {
            string category = buff.Kind switch
            {
                TheaterBuffKind.ChoiceCard => "选择卡牌", TheaterBuffKind.Buff => "增益",
                TheaterBuffKind.Splendour => "辉彩祝福", _ => "未知类别",
            };
            string title = buff.Name + (buff.Level is int level ? $" · Lv.{level}" : "") +
                (buff.IsEnhanced is bool enhanced ? enhanced ? " · 已强化" : " · 未强化" : "");
            string details = string.Join("\n", new[] { category, buff.Description }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Concat(buff.Effects).Select(value => HoYoDescriptionText.Plain(value!)));
            return new ChallengeBuffDetail(title, details,
                new ChallengePortrait(title, ReadArtworkUri(buff.IconUrl)))
            {
                IsSplendour = buff.Kind == TheaterBuffKind.Splendour,
                Level = buff.Level,
                RichDetail = string.Join("\n", new[] { category, buff.Description }.Where(value => !string.IsNullOrWhiteSpace(value)).Concat(buff.Effects)),
            };
        }).ToArray();

    internal static string TheaterDifficultyName(int? difficulty) => difficulty switch
    {
        1 => "轻简模式（1）", 2 => "普通模式（2）", 3 => "困难模式（3）",
        4 => "卓越模式（4）", 5 => "月谕模式（5）", 0 => "未定级（0）",
        null => "未知", _ => $"未知难度（{difficulty}）",
    };

    private static string ElementName(int element) => element switch
    {
        1 => "火", 2 => "水", 3 => "草", 4 => "雷", 5 => "冰", 7 => "风", 8 => "岩",
        _ => $"未知元素（{element}）",
    };
}

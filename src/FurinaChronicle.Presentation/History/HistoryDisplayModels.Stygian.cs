// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Presentation.History;

public sealed partial record ChallengeObservationCard
{
    private static async Task AddStygianDetailsAsync(
        List<ChallengeDetailSection> sections, ChallengeObservation observation, IDomainMetadataService metadata,
        Dictionary<string, DomainItemMetadata?> names, CancellationToken cancellationToken)
    {
        StygianStatistics? statistics = observation.StygianStatistics;
        AddStygianSummary(sections, "单人挑战", statistics?.SinglePlayer);
        await AddStygianStagesAsync(sections, "单人", observation.Stages, metadata, names, cancellationToken);
        AddStygianSummary(sections, "联机挑战", statistics?.Multiplayer);
        await AddStygianStagesAsync(sections, "联机", observation.MultiplayerStages, metadata, names, cancellationToken);
        if (statistics?.BlingAvatars is not { Count: > 0 } blings) return;
        var groups = await BuildPortraitGroupsAsync(
            blings.Select(item => item.Participant).ToArray(), [], metadata, names, cancellationToken);
        var portraits = groups[0].Portraits.Select((portrait, index) => portrait with
        {
            Label = portrait.Label + (blings[index].IsPlus switch
            {
                true => " · 增辉上榜", false => " · 未上榜", _ => " · 上榜状态未知",
            }) + (blings[index].Element is { } element ? $" · {element}" : ""),
        }).ToArray();
        sections.Add(new("增辉角色", ["仅反映本期挑战信息，不表示角色持有情况。"],
            [new ChallengeCombatDetail("", [], [new ChallengePortraitGroup("", portraits)])]));
    }

    private static void AddStygianSummary(List<ChallengeDetailSection> sections, string title, StygianRunSummary? summary)
    {
        if (summary is null) return;
        var lines = new List<string>();
        if (!summary.HasData) lines.Add("暂无官方记录。");
        else
        {
            lines.Add($"最佳难度：{StygianDifficultyName(summary.Difficulty)}");
            lines.Add($"总成绩：{summary.Seconds?.ToString(CultureInfo.InvariantCulture) ?? "未知"} 秒");
            lines.Add("发生时间未知；记录按采集时间查看。");
        }
        sections.Add(new(title, lines, [])
        {
            HeaderArtwork = summary.HasData ? StygianDifficultyArtwork.FromSource(summary.IconUrl,
                $"{title} · {StygianDifficultyName(summary.Difficulty)}") : null,
        });
    }

    private static async Task AddStygianStagesAsync(
        List<ChallengeDetailSection> sections, string run, IReadOnlyList<ChallengeStageResult> stages,
        IDomainMetadataService metadata, Dictionary<string, DomainItemMetadata?> names, CancellationToken cancellationToken)
    {
        foreach (var stage in stages)
        {
            var lines = DescribeStageFields(stage, ChallengeMode.StygianOnslaught, includeCombatants: false).ToList();
            var groups = (await BuildPortraitGroupsAsync(stage.Participants, stage.Enemies,
                metadata, names, cancellationToken)).ToList();
            if (stage.StygianDetails is { } details)
            {
                lines.AddRange(details.MonsterDescriptions.Select(description => "敌人说明：" + HoYoDescriptionText.Plain(description)));
                lines.AddRange(details.MonsterTags.Select(tag => "敌人标签：" + HoYoDescriptionText.Plain(tag.Description ??
                    $"未知标签类型（{tag.Type}）")));
                if (details.BestAvatars.Count > 0)
                {
                    var performance = await BuildPortraitGroupsAsync(
                        details.BestAvatars.Select(item => item.Participant).ToArray(), [], metadata, names, cancellationToken);
                    groups.Add(new("最佳角色", performance[0].Portraits.Select((portrait, index) => portrait with
                    {
                        Label = portrait.Label + $" · {StygianPerformanceLabel(details.BestAvatars[index].Type)}：" +
                            (details.BestAvatars[index].Value?.ToString(CultureInfo.InvariantCulture) ?? "未知"),
                    }).ToArray()));
                }
            }
            sections.Add(new($"{run} · {stage.StageId}", lines,
                groups.Count > 0 ? [new ChallengeCombatDetail("战斗详情", [], groups)] : []));
        }
    }

    private static string StygianPerformanceLabel(int? type) => type switch
    {
        1 => "最强一击", 2 => "最高总伤害", null or 0 => "未知指标", _ => $"未知指标（{type}）",
    };

    internal static string StygianDifficultySummary(StygianStatistics statistics) =>
        $"单人难度：{RunDifficulty(statistics.SinglePlayer)} · 联机难度：{RunDifficulty(statistics.Multiplayer)}";

    internal static string StygianSecondsSummary(StygianStatistics statistics) =>
        $"单人总成绩：{RunSeconds(statistics.SinglePlayer)} · 联机总成绩：{RunSeconds(statistics.Multiplayer)}";

    private static string RunDifficulty(StygianRunSummary? run) => run is { HasData: false } ? "无记录" :
        StygianDifficultyName(run?.Difficulty);

    private static string RunSeconds(StygianRunSummary? run) => run is { HasData: false } ? "无记录" :
        run?.Seconds is int seconds ? $"{seconds} 秒" : "未知";

    private static string StygianDifficultyName(int? difficulty) => difficulty switch
    {
        1 => "普通（1）", 2 => "进阶（2）", 3 => "困难（3）", 4 => "险恶（4）",
        5 => "无畏（5）", 6 => "绝境（6）", 0 => "未定级（0）",
        null => "未知", _ => $"未知难度（{difficulty}）",
    };
}

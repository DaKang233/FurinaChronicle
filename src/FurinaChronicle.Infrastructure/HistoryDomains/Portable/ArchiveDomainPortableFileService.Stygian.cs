// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Portable;

public sealed partial class ArchiveDomainPortableFileService
{
    private sealed record StygianRunWire(bool HasData, int? Difficulty, int? Seconds, string? IconUrl)
    {
        public static StygianRunWire? FromDomain(StygianRunSummary? value) =>
            value is null ? null : new(value.HasData, value.Difficulty, value.Seconds, value.IconUrl);
        public StygianRunSummary ToDomain() => new(HasData, Difficulty, Seconds, IconUrl);
    }

    private sealed record StygianPerformanceWire(ChallengeParticipantWire Participant, long? Value, int? Type)
    {
        public static StygianPerformanceWire FromDomain(StygianAvatarPerformance value) =>
            new(ChallengeParticipantWire.FromDomain(value.Participant), value.Value, value.Type);
        public StygianAvatarPerformance ToDomain() => new(ChallengeParticipantWire.ToDomainChecked(Participant), Value, Type);
    }

    private sealed record StygianTagWire(int? Type, string? Description)
    {
        public static StygianTagWire FromDomain(StygianMonsterTag value) => new(value.Type, value.Description);
        public StygianMonsterTag ToDomain() => new(Type, Description);
    }

    private sealed record StygianStageWire(
        string? MonsterId, IReadOnlyList<string>? MonsterDescriptions,
        IReadOnlyList<StygianTagWire>? MonsterTags, IReadOnlyList<StygianPerformanceWire>? BestAvatars)
    {
        public static StygianStageWire FromDomain(StygianStageDetails value) =>
            new(value.MonsterId, value.MonsterDescriptions, value.MonsterTags.Select(StygianTagWire.FromDomain).ToArray(),
                value.BestAvatars.Select(StygianPerformanceWire.FromDomain).ToArray());
        public StygianStageDetails ToDomain() => new(MonsterId, MonsterDescriptions,
            (MonsterTags ?? []).Select(item => item?.ToDomain() ?? throw new InvalidDataException("An enemy tag cannot be null.")).ToArray(),
            (BestAvatars ?? []).Select(item => item?.ToDomain() ?? throw new InvalidDataException("A performance cannot be null.")).ToArray());
    }

    private sealed record StygianBlingWire(ChallengeParticipantWire Participant, bool? IsPlus, string? Element)
    {
        public static StygianBlingWire FromDomain(StygianBlingAvatar value) =>
            new(ChallengeParticipantWire.FromDomain(value.Participant), value.IsPlus, value.Element);
        public StygianBlingAvatar ToDomain() => new(ChallengeParticipantWire.ToDomainChecked(Participant), IsPlus, Element);
    }

    private sealed record StygianStatisticsWire(
        StygianRunWire? SinglePlayer, StygianRunWire? Multiplayer, IReadOnlyList<StygianBlingWire>? BlingAvatars)
    {
        public static StygianStatisticsWire FromDomain(StygianStatistics value) =>
            new(StygianRunWire.FromDomain(value.SinglePlayer), StygianRunWire.FromDomain(value.Multiplayer),
                value.BlingAvatars.Select(StygianBlingWire.FromDomain).ToArray());
        public StygianStatistics ToDomain() => new(SinglePlayer?.ToDomain(), Multiplayer?.ToDomain(),
            (BlingAvatars ?? []).Select(item => item?.ToDomain() ?? throw new InvalidDataException("A bling avatar cannot be null.")).ToArray());
    }

    private static async Task WriteStygianDetailsAsync(TextWriter destination, ChallengeObservation observation)
    {
        if (observation.StygianStatistics is { } statistics)
        {
            foreach (var run in new[] { (Name: "单人", Value: statistics.SinglePlayer), (Name: "多人", Value: statistics.Multiplayer) })
            {
                if (run.Value is null) continue;
                await destination.WriteLineAsync(!run.Value.HasData ? $"  - {run.Name}：暂无官方记录。" :
                    $"  - {run.Name}最佳难度：{run.Value.Difficulty?.ToString() ?? "未知"}，总成绩：{run.Value.Seconds?.ToString() ?? "未知"} 秒；发生时间未知。");
            }
            foreach (var bling in statistics.BlingAvatars)
                await destination.WriteLineAsync($"  - 增辉角色：{DescribeParticipant(bling.Participant)}，" +
                    $"上榜：{bling.IsPlus?.ToString() ?? "未知"}，元素：{FormatMissing(bling.Element)}（不表示玩家持有）");
        }
        await WriteStygianStagesAsync(destination, "单人", observation.Stages);
        await WriteStygianStagesAsync(destination, "多人", observation.MultiplayerStages);
    }

    private static async Task WriteStygianStagesAsync(
        TextWriter destination, string run, IReadOnlyList<ChallengeStageResult> stages)
    {
        foreach (var stage in stages)
        {
            await destination.WriteLineAsync($"  - {run}关卡 {EscapeMarkdown(stage.StageId)}：" +
                $"耗时 {stage.Score?.ToString() ?? "未知"} 秒，发生时间 {FormatOccurrence(stage.CompletedAt)}");
            if (stage.Participants.Count > 0)
                await destination.WriteLineAsync("    - 阵容：" + string.Join("、", stage.Participants.Select(DescribeParticipant)));
            foreach (var enemy in stage.Enemies)
                await destination.WriteLineAsync($"    - 敌人：{FormatMissing(enemy.Name)}，等级 {FormatMissing(enemy.Level)}");
            foreach (var buff in stage.Buffs) await destination.WriteLineAsync("    - 增益：" + EscapeMarkdown(buff));
            if (stage.StygianDetails is not { } details) continue;
            if (details.MonsterId is { } id) await destination.WriteLineAsync("    - 怪物 ID：" + EscapeMarkdown(id));
            foreach (var description in details.MonsterDescriptions)
                await destination.WriteLineAsync("    - 敌人说明：" + EscapeMarkdown(description.Replace("\\n", "\n", StringComparison.Ordinal)));
            foreach (var tag in details.MonsterTags)
                await destination.WriteLineAsync($"    - 敌人标签（{tag.Type?.ToString() ?? "未知"}）：{FormatMissing(tag.Description)}");
            foreach (var performance in details.BestAvatars)
                await destination.WriteLineAsync($"    - 最佳角色：{DescribeParticipant(performance.Participant)}，" +
                    $"{(performance.Type switch { 1 => "最强一击", 2 => "最高总伤害", _ => $"未知指标（{performance.Type?.ToString() ?? "未知"}）" })}：" +
                    (performance.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "未知"));
        }
    }
}

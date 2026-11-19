// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Portable;

public sealed partial class ArchiveDomainPortableFileService
{
    private static string DescribeParticipant(ChallengeParticipant participant) =>
        EscapeMarkdown(participant.Name ?? participant.Id) +
        (participant.Level is int level ? $"(Lv.{level})" : string.Empty) +
        (participant.Constellation is int constellation ? $" · {constellation} 命" : "") +
        (participant.Origin switch
        {
            ChallengeParticipantOrigin.Owned => " · 自有",
            ChallengeParticipantOrigin.Trial => " · 试用",
            ChallengeParticipantOrigin.Support => " · 助演",
            _ => string.Empty,
        });

    private static async Task WriteTheaterBuffsAsync(TextWriter destination, TheaterRoundDetails round)
    {
        if (round.BuffSummary is { } summary)
            await destination.WriteLineAsync("    - 祝福总览：" + EscapeMarkdown(summary));
        foreach (TheaterBuff buff in round.BuffDetails ?? [])
        {
            await destination.WriteLineAsync($"    - {buff.Kind}：{EscapeMarkdown(buff.Name)}" +
                (buff.Level is int level ? $" · Lv.{level}" : "") +
                (buff.IsEnhanced is bool enhanced ? enhanced ? " · 已强化" : " · 未强化" : "") +
                (buff.SourceId is long id ? $" · 来源 ID {id}" : ""));
            if (buff.Description is { } description)
                await destination.WriteLineAsync("      - " + EscapeMarkdown(description));
            foreach (string effect in buff.Effects)
                await destination.WriteLineAsync("      - " + EscapeMarkdown(effect));
        }
    }

    private static async Task WriteTheaterSummaryAsync(TextWriter destination, TheaterStatistics statistics)
    {
        if (statistics.MaxRoundId is int round) await destination.WriteLineAsync($"  - 最高幕次：{round}");
        if (statistics.Heraldry is int heraldry) await destination.WriteLineAsync($"  - 演出徽记：{heraldry}");
        if (statistics.CoinCount is int coins) await destination.WriteLineAsync($"  - 消耗「幻剧之花」：{coins}");
        if (statistics.AvatarBonusCount is int bonus) await destination.WriteLineAsync($"  - 场外声援：{bonus}");
        if (statistics.RentCount is int rents) await destination.WriteLineAsync($"  - 助演次数：{rents}");
        if (statistics.TarotFinishedCount is int tarot) await destination.WriteLineAsync($"  - 完成塔罗挑战：{tarot}");
        if (statistics.TotalUseTime is int seconds) await destination.WriteLineAsync($"  - 演出总时长：{seconds} 秒");
        if (statistics.MedalRounds.Count > 0) await destination.WriteLineAsync("  - 获奖章幕次：" + string.Join("、", statistics.MedalRounds));
        if (statistics.TotalCoinsConsumed is { } consumed) await destination.WriteLineAsync("  - 消耗「幻剧之花」（战斗统计）：" + EscapeMarkdown(consumed));
        if (statistics.BackupAvatars.Count > 0) await destination.WriteLineAsync("  - 候补角色：" + string.Join("、", statistics.BackupAvatars.Select(DescribeParticipant)));
        if (statistics.ShowBattleStats == false)
        {
            await destination.WriteLineAsync("  - 官方未开放本次战斗统计。");
            return;
        }
        (string Label, TheaterAvatarStatistic? Value)[] metrics =
        [
            ("最多击破", statistics.MaxDefeat), ("最强一击", statistics.MaxDamage),
            ("承受伤害", statistics.MaxTakeDamage),
        ];
        foreach (var metric in metrics.Where(metric => metric.Value is not null))
            await destination.WriteLineAsync($"  - {metric.Label}：{DescribeParticipant(metric.Value!.Participant)} = {EscapeMarkdown(metric.Value.Value ?? "未知")}");
        foreach (var metric in statistics.ShortestAvatars)
            await destination.WriteLineAsync($"  - 最快演出：{DescribeParticipant(metric.Participant)} = {EscapeMarkdown(metric.Value ?? "未知")}");
    }
}

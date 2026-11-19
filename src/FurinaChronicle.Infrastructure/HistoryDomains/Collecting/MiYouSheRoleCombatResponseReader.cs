// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Maps the Imaginarium Theater schedule into one mode observation. Rounds
/// carry their own finish time and medal flag; the schedule supplies the
/// season, difficulty and medal total.
/// </summary>
public sealed class MiYouSheRoleCombatResponseReader(TimeProvider timeProvider)
{
    private const int MaxResponseBytes = 2 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        MaxDepth = 32,
    };

    public async Task<ChallengeModeSourceResult> ReadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!response.IsSuccessStatusCode)
        {
            return new(
                MapHttpStatus(response.StatusCode),
                RetryAfter: GetRetryDelay(response),
                HttpStatus: (int)response.StatusCode);
        }

        int? providerCode = null;
        try
        {
            await response.Content.LoadIntoBufferAsync(
                MaxResponseBytes, cancellationToken);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();
            await using Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            Envelope? envelope = await JsonSerializer.DeserializeAsync<Envelope>(
                body, JsonOptions, cancellationToken);
            providerCode = envelope?.Retcode;
            return Map(envelope, fetchedAt) with
            {
                HttpStatus = (int)response.StatusCode,
                ProviderCode = providerCode,
            };
        }
        catch (JsonException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse, HttpStatus: (int)response.StatusCode, ProviderCode: providerCode);
        }
        catch (ArgumentException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse, HttpStatus: (int)response.StatusCode, ProviderCode: providerCode);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse, HttpStatus: (int)response.StatusCode, ProviderCode: providerCode);
        }
    }

    internal static SpiralAbyssCollectionStatus MapHttpStatus(
        HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized => SpiralAbyssCollectionStatus.AuthenticationRequired,
        HttpStatusCode.Forbidden => SpiralAbyssCollectionStatus.AccessDenied,
        HttpStatusCode.TooManyRequests => SpiralAbyssCollectionStatus.RateLimited,
        HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError =>
            SpiralAbyssCollectionStatus.Unavailable,
        _ => SpiralAbyssCollectionStatus.SourceRejected,
    };

    private TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        TimeSpan? delay = retryAfter?.Delta ??
            (retryAfter?.Date - timeProvider.GetUtcNow());
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static ChallengeModeSourceResult Map(
        Envelope? envelope,
        DateTimeOffset fetchedAt)
    {
        if (envelope?.Retcode is not int retcode)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        if (retcode != 0)
        {
            return new(
                MapBusinessCode(retcode),
                ProviderCode: retcode);
        }
        if (envelope.Data is not { } root)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        Entry? entry = root.Data?.FirstOrDefault(candidate => candidate?.HasData == true);
        if (entry is null)
        {
            return new(SpiralAbyssCollectionStatus.Empty);
        }
        if (entry.Schedule is not { ScheduleId: > 0 } schedule)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }

        var stages = new List<ChallengeStageResult>();
        foreach (Round? round in entry.Detail?.RoundsData ?? [])
        {
            if (round is not { RoundId: > 0 })
            {
                return new(SpiralAbyssCollectionStatus.InvalidResponse);
            }

            var participants = new List<ChallengeParticipant>();
            foreach (Avatar? avatar in round.Avatars ?? [])
            {
                if (avatar is not { AvatarId: > 0 } ||
                    avatar.Level is < 0 ||
                    avatar.Rarity is < 0)
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }
                participants.Add(ReadAvatar(avatar));
            }

            var buffs = new List<string>();
            var buffDetails = new List<TheaterBuff>();
            foreach (var (buff, kind) in (round.ChoiceCards ?? []).Select(buff => (buff, TheaterBuffKind.ChoiceCard))
                .Concat((round.Buffs ?? []).Select(buff => (buff, TheaterBuffKind.Buff))))
            {
                if (!string.IsNullOrWhiteSpace(buff?.Name))
                {
                    buffs.Add(DescribeBuff(buff.Name, buff.Description));
                    buffDetails.Add(new TheaterBuff(kind, buff.Name, buff.Id, buff.Description, buff.Icon,
                        isEnhanced: buff.IsEnhanced));
                }
            }

            if (round.SplendourBuff?.Summary?.Description is { } summary &&
                !string.IsNullOrWhiteSpace(summary))
            {
                buffs.Add(DescribeBuff("祝福总览", summary));
            }
            foreach (SplendourEntry? blessing in round.SplendourBuff?.Buffs ?? [])
            {
                if (string.IsNullOrWhiteSpace(blessing?.Name))
                {
                    continue;
                }

                string name = blessing.Level is int level && level >= 0
                    ? $"{blessing.Name} Lv.{level.ToString(CultureInfo.InvariantCulture)}"
                    : blessing.Name;
                string effects = string.Join("\n", (blessing.LevelEffect ?? [])
                    .Where(effect => !string.IsNullOrWhiteSpace(effect?.Description))
                    .Select(effect => effect!.Description));
                buffs.Add(DescribeBuff(name, effects));
                buffDetails.Add(new TheaterBuff(TheaterBuffKind.Splendour, blessing.Name!,
                    iconUrl: blessing.Icon, level: blessing.Level,
                    effects: (blessing.LevelEffect ?? []).Where(effect => !string.IsNullOrWhiteSpace(effect?.Description))
                        .Select(effect => effect!.Description!).ToArray()));
            }

            var enemies = new List<ChallengeEnemy>();
            foreach (Enemy? enemy in round.Enemies ?? [])
            {
                if (enemy is null || enemy.Level is < 0)
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }
                enemies.Add(new ChallengeEnemy(
                    enemy.Name, enemy.Level, enemy.Icon));
            }

            stages.Add(new ChallengeStageResult(
                // Tarot challenges can share the source round number with ordinary rounds.
                // Qualify every Tarot key consistently; never rename existing stored stages.
                round.IsTarot == true
                    ? $"tarot:{round.RoundId.Value.ToString(CultureInfo.InvariantCulture)}:{round.TarotSerialNo?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}"
                    : round.RoundId.Value.ToString(CultureInfo.InvariantCulture),
                round.IsGetMedal is null ? null : round.IsGetMedal.Value ? 1 : 0,
                round.FinishTime is > 0
                    ? OccurrenceTime.At(
                        DateTimeOffset.FromUnixTimeSeconds(round.FinishTime.Value),
                        TimePrecision.Second)
                    : OccurrenceTime.Unknown,
                participants,
                buffs,
                enemies,
                round.IsTarot.HasValue || round.TarotSerialNo.HasValue || buffDetails.Count > 0 ||
                    !string.IsNullOrWhiteSpace(round.SplendourBuff?.Summary?.Description)
                    // The official API uses -1 for ordinary (non-Tarot) rounds. It means
                    // not applicable, not an invalid negative Tarot challenge ordinal.
                    ? new TheaterRoundDetails(round.IsTarot,
                        round.IsTarot != true && round.TarotSerialNo == -1 ? null : round.TarotSerialNo, buffDetails,
                        round.SplendourBuff?.Summary?.Description?.Replace("\\n", "\n", StringComparison.Ordinal)) : null));
        }

        if (stages.Select(stage => stage.StageId).Distinct(StringComparer.Ordinal).Count() != stages.Count)
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        return new(
            SpiralAbyssCollectionStatus.Received,
            new ChallengeModeSnapshot(
                ChallengeMode.ImaginariumTheater,
                schedule.ScheduleId.Value.ToString(CultureInfo.InvariantCulture),
                ReadUnixTime(schedule.StartTime),
                ReadUnixTime(schedule.EndTime),
                DataCompleteness.Partial,
                stages,
                entry.Stat?.DifficultyId,
                entry.Stat?.MedalNum,
                fetchedAt,
                ReadStatistics(entry)));
    }

    private static ChallengeParticipant ReadAvatar(Avatar? avatar)
    {
        if (avatar is not { AvatarId: > 0 } || avatar.Level is < 0 || avatar.Rarity is < 0)
            throw new ArgumentException("The Theater roster contains an invalid avatar.");
        return new ChallengeParticipant(
            avatar.AvatarId.Value.ToString(CultureInfo.InvariantCulture),
            avatar.Name, avatar.Image, avatar.Level, avatar.Rarity,
            avatar.AvatarType is int type ? (ChallengeParticipantOrigin)type : null);
    }

    private static TheaterStatistics? ReadStatistics(Entry entry)
    {
        FightStatistics? fight = entry.Detail?.FightStatisic;
        Stat? stat = entry.Stat;
        bool hasTotals = stat is not null && (stat.MaxRoundId.HasValue || stat.Heraldry.HasValue ||
            stat.CoinNum.HasValue || stat.AvatarBonusNum.HasValue || stat.RentCnt.HasValue ||
            stat.TarotFinishedCnt.HasValue || stat.GetMedalRoundList is not null);
        if (!hasTotals && fight is null && entry.Detail?.BackupAvatars is null) return null;
        return new TheaterStatistics(
            stat?.MaxRoundId, stat?.Heraldry, stat?.CoinNum, stat?.AvatarBonusNum,
            stat?.RentCnt, stat?.TarotFinishedCnt, fight?.TotalUseTime, fight?.IsShowBattleStats,
            stat?.GetMedalRoundList,
            entry.Detail?.BackupAvatars?.Select(ReadAvatar).ToArray(),
            ReadAvatarStatistic(fight?.MaxDefeatAvatar), ReadAvatarStatistic(fight?.MaxDamageAvatar),
            ReadAvatarStatistic(fight?.MaxTakeDamageAvatar), ReadValue(fight?.TotalCoinConsumed?.Value ?? default),
            (fight?.ShortestAvatarList ?? []).Select(value => ReadAvatarStatistic(value)
                ?? throw new ArgumentException("A shortest-time avatar is missing its identity.")).ToArray());
    }

    private static TheaterAvatarStatistic? ReadAvatarStatistic(AvatarStatistic? value)
    {
        if (value is null || value.AvatarId is null or 0) return null;
        if (value.AvatarId < 0) throw new ArgumentException("A Theater statistic avatar is invalid.");
        return new TheaterAvatarStatistic(
            new ChallengeParticipant(value.AvatarId.Value.ToString(CultureInfo.InvariantCulture),
                null, value.AvatarIcon, null, value.Rarity), ReadValue(value.Value));
    }

    private static string? ReadValue(JsonElement value)
    {
        string? text = value.ValueKind switch
        {
            JsonValueKind.Undefined or JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new ArgumentException("A Theater statistic value is invalid."),
        };
        if (text is null) return null;
        if (text.Length == 0) return "0";
        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
            ? number.ToString("G29", CultureInfo.InvariantCulture) : text;
    }

    private static string DescribeBuff(string name, string? description) =>
        string.IsNullOrWhiteSpace(description)
            ? name
            : $"{name}：{description.Replace("\\n", "\n", StringComparison.Ordinal).Trim()}";

    internal static SpiralAbyssCollectionStatus MapBusinessCode(int retcode) =>
        retcode switch
        {
            -100 or 1004 or 10001 => SpiralAbyssCollectionStatus.AuthenticationRequired,
            1034 => SpiralAbyssCollectionStatus.VerificationRequired,
            -110 or 10101 => SpiralAbyssCollectionStatus.RateLimited,
            5003 or 10102 => SpiralAbyssCollectionStatus.AccessDenied,
            _ => SpiralAbyssCollectionStatus.SourceRejected,
        };

    private static DateTimeOffset? ReadUnixTime(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    private sealed record Envelope(int? Retcode, Root? Data);

    private sealed record Root(Entry?[]? Data);

    private sealed record Entry(
        Schedule? Schedule,
        Stat? Stat,
        Detail? Detail,
        bool? HasData);

    private sealed record Schedule(int? ScheduleId, long? StartTime, long? EndTime);

    private sealed record Stat(int? DifficultyId, int? MedalNum, int? MaxRoundId, int? Heraldry,
        int? CoinNum, int? AvatarBonusNum, int? RentCnt, int? TarotFinishedCnt, int[]? GetMedalRoundList);

    private sealed record Detail(Round?[]? RoundsData, Avatar?[]? BackupAvatars, FightStatistics? FightStatisic);
    private sealed record FightStatistics(AvatarStatistic? MaxDefeatAvatar, AvatarStatistic? MaxDamageAvatar,
        AvatarStatistic? MaxTakeDamageAvatar, AvatarStatistic? TotalCoinConsumed,
        AvatarStatistic?[]? ShortestAvatarList, int? TotalUseTime, bool? IsShowBattleStats);
    private sealed record AvatarStatistic(long? AvatarId, string? AvatarIcon, JsonElement Value, int? Rarity);

    private sealed record Round(
        int? RoundId,
        bool? IsGetMedal,
        long? FinishTime,
        Avatar?[]? Avatars,
        Buff?[]? Buffs,
        Buff?[]? ChoiceCards,
        Enemy?[]? Enemies,
        SplendourGroup? SplendourBuff,
        bool? IsTarot,
        int? TarotSerialNo);

    private sealed record Avatar(
        int? AvatarId,
        string? Name,
        string? Image,
        int? Level,
        int? Rarity,
        int? AvatarType);

    private sealed record Buff(string? Name, [property: JsonPropertyName("desc")] string? Description,
        long? Id, string? Icon, bool? IsEnhanced);

    private sealed record SplendourGroup(SplendourDescription? Summary, SplendourEntry?[]? Buffs);
    private sealed record SplendourEntry(string? Name, int? Level, SplendourDescription?[]? LevelEffect, string? Icon);
    private sealed record SplendourDescription([property: JsonPropertyName("desc")] string? Description);

    private sealed record Enemy(string? Name, string? Icon, int? Level);
}

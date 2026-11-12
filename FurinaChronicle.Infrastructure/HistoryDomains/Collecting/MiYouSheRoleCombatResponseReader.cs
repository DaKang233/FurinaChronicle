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

        try
        {
            await response.Content.LoadIntoBufferAsync(
                MaxResponseBytes, cancellationToken);
            DateTimeOffset fetchedAt = timeProvider.GetUtcNow();
            await using Stream body = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            Envelope? envelope = await JsonSerializer.DeserializeAsync<Envelope>(
                body, JsonOptions, cancellationToken);
            return Map(envelope, fetchedAt) with
            {
                HttpStatus = (int)response.StatusCode,
            };
        }
        catch (JsonException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        catch (ArgumentException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
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
        Entry? entry = root.Data?.FirstOrDefault(candidate => candidate.HasData == true);
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
                participants.Add(new ChallengeParticipant(
                    avatar.AvatarId.Value.ToString(CultureInfo.InvariantCulture),
                    avatar.Name,
                    avatar.Image,
                    avatar.Level,
                    avatar.Rarity));
            }

            var buffs = new List<string>();
            foreach (Buff? buff in (round.ChoiceCards ?? [])
                .Concat(round.Buffs ?? []))
            {
                if (!string.IsNullOrWhiteSpace(buff?.Name))
                {
                    buffs.Add(buff.Name);
                }
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
                round.RoundId.Value.ToString(CultureInfo.InvariantCulture),
                round.IsGetMedal is null ? null : round.IsGetMedal.Value ? 1 : 0,
                round.FinishTime is > 0
                    ? OccurrenceTime.At(
                        DateTimeOffset.FromUnixTimeSeconds(round.FinishTime.Value),
                        TimePrecision.Second)
                    : OccurrenceTime.Unknown,
                participants,
                buffs,
                enemies));
        }

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
                fetchedAt));
    }

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

    private sealed record Stat(int? DifficultyId, int? MedalNum);

    private sealed record Detail(Round?[]? RoundsData);

    private sealed record Round(
        int? RoundId,
        bool? IsGetMedal,
        long? FinishTime,
        Avatar?[]? Avatars,
        Buff?[]? Buffs,
        Buff?[]? ChoiceCards,
        Enemy?[]? Enemies);

    private sealed record Avatar(
        int? AvatarId,
        string? Name,
        string? Image,
        int? Level,
        int? Rarity);

    private sealed record Buff(string? Name);

    private sealed record Enemy(string? Name, string? Icon, int? Level);
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Maps the Stygian onslaught schedule into one mode observation. Each entry is
/// one monster challenge with its official best time; the response does not
/// expose a per-challenge completion timestamp, so it stays Unknown.
/// </summary>
public sealed class MiYouSheHardChallengeResponseReader(TimeProvider timeProvider)
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
                MiYouSheRoleCombatResponseReader.MapHttpStatus(response.StatusCode),
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
                MiYouSheRoleCombatResponseReader.MapBusinessCode(retcode),
                ProviderCode: retcode);
        }
        if (envelope.Data is not { } root)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }
        Entry? entry = root.Data?.FirstOrDefault(candidate =>
            candidate.Single?.HasData == true &&
            candidate.Single.Challenge is { Length: > 0 });
        if (entry is null)
        {
            return new(SpiralAbyssCollectionStatus.Empty);
        }
        if (entry.Schedule is not { ScheduleId: > 0 } schedule)
        {
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        }

        var stages = new List<ChallengeStageResult>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Stage? challenge in entry.Single!.Challenge ?? [])
        {
            if (challenge is not { Second: >= 0 } ||
                string.IsNullOrWhiteSpace(challenge.Name))
            {
                return new(SpiralAbyssCollectionStatus.InvalidResponse);
            }
            string stageId = challenge.Name.Trim();
            if (!used.Add(stageId))
            {
                int suffix = 2;
                while (!used.Add($"{stageId} #{suffix}"))
                {
                    suffix++;
                }
                stageId = $"{challenge.Name.Trim()} #{suffix}";
            }
            var participants = new List<ChallengeParticipant>();
            foreach (TeamAvatar? avatar in challenge.Teams ?? [])
            {
                if (avatar is not { AvatarId: > 0 } ||
                    avatar.Level is < 0 ||
                    avatar.Rank is < 0)
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }
                participants.Add(new ChallengeParticipant(
                    avatar.AvatarId.Value.ToString(CultureInfo.InvariantCulture),
                    null,
                    avatar.SideIcon,
                    avatar.Level,
                    null));
            }

            var enemies = new List<ChallengeEnemy>();
            if (challenge.Monster is { } monster)
            {
                if (monster.Level is < 0)
                {
                    return new(SpiralAbyssCollectionStatus.InvalidResponse);
                }
                enemies.Add(new ChallengeEnemy(
                    monster.Name, monster.Level, monster.Icon));
            }

            stages.Add(new ChallengeStageResult(
                stageId,
                challenge.Second,
                OccurrenceTime.Unknown,
                participants,
                buffs: null,
                enemies));
        }

        return new(
            SpiralAbyssCollectionStatus.Received,
            new ChallengeModeSnapshot(
                ChallengeMode.StygianOnslaught,
                schedule.ScheduleId.Value.ToString(CultureInfo.InvariantCulture),
                ReadUnixTime(schedule.StartTime),
                ReadUnixTime(schedule.EndTime),
                DataCompleteness.Partial,
                stages,
                entry.Single.Best?.Difficulty,
                MedalCount: null,
                fetchedAt));
    }

    private static DateTimeOffset? ReadUnixTime(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    private sealed record Envelope(int? Retcode, Root? Data);

    private sealed record Root(Entry?[]? Data);

    private sealed record Entry(
        Schedule? Schedule,
        SinglePlayer? Single);

    private sealed record Schedule(int? ScheduleId, long? StartTime, long? EndTime);

    private sealed record SinglePlayer(
        Best? Best,
        Stage?[]? Challenge,
        bool? HasData);

    private sealed record Best(int? Difficulty, int? Second);

    private sealed record Stage(
        string? Name,
        int? Second,
        TeamAvatar?[]? Teams,
        StageMonster? Monster);

    private sealed record TeamAvatar(
        int? AvatarId,
        string? SideIcon,
        int? Level,
        int? Rank);

    private sealed record StageMonster(string? Name, int? Level, string? Icon);
}

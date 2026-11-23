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
        var snapshots = new List<ChallengeModeSnapshot>();
        foreach (Entry? candidate in root.Data ?? [])
        {
            if (!(candidate?.Single?.HasData == true || candidate?.Mp?.HasData == true || candidate?.Blings is { Length: > 0 })) continue;
            ChallengeModeSourceResult result = MapEntry(candidate!, fetchedAt);
            if (result.Status != SpiralAbyssCollectionStatus.Received || result.Snapshot is null) return result;
            snapshots.Add(result.Snapshot);
        }
        if (snapshots.Count == 0) return new(SpiralAbyssCollectionStatus.Empty);
        if (snapshots.Count > 2 || snapshots.Select(item => item.SeasonId).Distinct(StringComparer.Ordinal).Count() != snapshots.Count)
            return new(SpiralAbyssCollectionStatus.InvalidResponse);
        return new(SpiralAbyssCollectionStatus.Received, snapshots[0])
        { AdditionalSnapshots = snapshots.Skip(1).ToArray() };
    }

    private static ChallengeModeSourceResult MapEntry(Entry entry, DateTimeOffset fetchedAt)
    {
        if (entry.Schedule is not { ScheduleId: > 0 } schedule)
            return new(SpiralAbyssCollectionStatus.InvalidResponse);

        (StygianRunSummary? single, IReadOnlyList<ChallengeStageResult> stages) = ReadRun(entry.Single);
        (StygianRunSummary? multiplayer, IReadOnlyList<ChallengeStageResult> multiplayerStages) = ReadRun(entry.Mp);
        var blings = new List<StygianBlingAvatar>();
        foreach (BlingAvatar? avatar in entry.Blings ?? [])
        {
            if (avatar is not { AvatarId: > 0 }) throw new ArgumentException("An invalid bling avatar was returned.");
            blings.Add(new(ReadParticipant(avatar.AvatarId, avatar.Name, avatar.SideIcon ?? avatar.Image,
                null, avatar.Rarity, null), avatar.IsPlus, avatar.Element));
        }
        return new(SpiralAbyssCollectionStatus.Received,
            new ChallengeModeSnapshot(ChallengeMode.StygianOnslaught,
                schedule.ScheduleId.Value.ToString(CultureInfo.InvariantCulture),
                ReadUnixTime(schedule.StartTime), ReadUnixTime(schedule.EndTime),
                DataCompleteness.Complete, stages, single?.Difficulty, null, fetchedAt,
                StygianStatistics: new(single, multiplayer, blings), MultiplayerStages: multiplayerStages));
    }

    private static (StygianRunSummary?, IReadOnlyList<ChallengeStageResult>) ReadRun(PlayerRun? run)
    {
        if (run is null || run is { HasData: null, Best: null } && run.Challenge is null or { Length: 0 })
            return (null, []);
        if (run.HasData is not bool hasData) throw new ArgumentException("A run is missing its data flag.");
        if (!hasData)
        {
            if (run.Challenge is { Length: > 0 }) throw new ArgumentException("An empty run contains stages.");
            return (new(false), []);
        }
        var stages = new List<ChallengeStageResult>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (Stage? challenge in run.Challenge ?? [])
        {
            if (challenge is null || challenge.Second is < 0 || string.IsNullOrWhiteSpace(challenge.Name))
                throw new ArgumentException("An invalid Stygian stage was returned.");
            string stageId = challenge.Name.Trim();
            if (!used.Add(stageId))
            {
                int suffix = 2;
                while (!used.Add($"{stageId} #{suffix}")) suffix++;
                stageId = $"{challenge.Name.Trim()} #{suffix}";
            }
            var participants = new List<ChallengeParticipant>();
            foreach (TeamAvatar? avatar in challenge.Teams ?? [])
            {
                if (avatar is not { AvatarId: > 0 }) throw new ArgumentException("An invalid team avatar was returned.");
                participants.Add(ReadParticipant(avatar.AvatarId, avatar.Name, avatar.SideIcon ?? avatar.Image,
                    avatar.Level, avatar.Rarity, avatar.Rank));
            }
            var performances = new List<StygianAvatarPerformance>();
            foreach (BestAvatar? avatar in challenge.BestAvatar ?? [])
            {
                if (avatar is { AvatarId: 0, Dps: null or 0, Type: null or 0 }) continue;
                if (avatar is not { AvatarId: > 0 }) throw new ArgumentException("An invalid performance avatar was returned.");
                performances.Add(new(ReadParticipant(avatar.AvatarId, null, avatar.SideIcon, null, null, null),
                    avatar.Dps, avatar.Type));
            }
            var enemies = new List<ChallengeEnemy>();
            StageMonster? monster = challenge.Monster;
            if (monster is not null)
            {
                if (monster.MonsterId is < 0) throw new ArgumentOutOfRangeException(nameof(monster.MonsterId));
                enemies.Add(new(monster.Name, monster.Level, monster.Icon));
            }
            StygianStageDetails? details = monster?.MonsterId is > 0 ||
                monster?.Desc is { Length: > 0 } || monster?.Tags is { Length: > 0 } ||
                performances.Count > 0
                ? new(monster?.MonsterId is > 0 ? monster.MonsterId.Value.ToString(CultureInfo.InvariantCulture) : null,
                    monster?.Desc, (monster?.Tags ?? []).Select(tag => tag is null
                        ? throw new ArgumentException("An enemy tag cannot be null.")
                        : new StygianMonsterTag(tag.Type, tag.Desc)).ToArray(), performances)
                : null;
            stages.Add(new(stageId, challenge.Second, OccurrenceTime.Unknown, participants,
                buffs: null, enemies, stygianDetails: details));
        }
        return (new(true, run.Best?.Difficulty, run.Best?.Second, run.Best?.Icon), stages);
    }

    private static ChallengeParticipant ReadParticipant(
        long? id, string? name, string? icon, int? level, int? rarity, int? constellation) =>
        new(id!.Value.ToString(CultureInfo.InvariantCulture), name, icon, level, rarity,
            constellation: constellation);

    private static DateTimeOffset? ReadUnixTime(long? seconds) =>
        seconds is > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds.Value) : null;

    private sealed record Envelope(int? Retcode, Root? Data);

    private sealed record Root(Entry?[]? Data);

    private sealed record Entry(Schedule? Schedule, PlayerRun? Single, PlayerRun? Mp, BlingAvatar?[]? Blings);
    private sealed record Schedule(int? ScheduleId, long? StartTime, long? EndTime);
    private sealed record PlayerRun(Best? Best, Stage?[]? Challenge, bool? HasData);
    private sealed record Best(int? Difficulty, int? Second, string? Icon);
    private sealed record Stage(
        string? Name, int? Second, TeamAvatar?[]? Teams, StageMonster? Monster, BestAvatar?[]? BestAvatar);
    private sealed record TeamAvatar(
        long? AvatarId, string? SideIcon, string? Image, string? Name, int? Level, int? Rank, int? Rarity);
    private sealed record BestAvatar(long? AvatarId, string? SideIcon, long? Dps, int? Type);
    private sealed record StageMonster(
        string? Name, int? Level, string? Icon, long? MonsterId, string[]? Desc, MonsterTag?[]? Tags);
    private sealed record MonsterTag(int? Type, string? Desc);
    private sealed record BlingAvatar(
        long? AvatarId, string? Name, string? Element, string? Image, int? Rarity, bool? IsPlus, string? SideIcon);
}

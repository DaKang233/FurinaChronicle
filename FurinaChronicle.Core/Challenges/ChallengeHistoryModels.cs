// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Core.Challenges;

public enum ChallengeMode
{
    SpiralAbyss = 1,
    ImaginariumTheater = 2,
    StygianOnslaught = 3,
}

public readonly record struct ChallengeObservationId
{
    public ChallengeObservationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Challenge observation ID cannot be empty.",
                nameof(value));
        }
        Value = value;
    }

    public Guid Value { get; }

    public static ChallengeObservationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public sealed record SpiralAbyssChamberResult
{
    public SpiralAbyssChamberResult(
        int chamber,
        int stars,
        OccurrenceTime completedAt)
    {
        if (chamber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chamber));
        }
        if (stars is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(stars));
        }
        ArgumentNullException.ThrowIfNull(completedAt);

        Chamber = chamber;
        Stars = stars;
        CompletedAt = completedAt;
    }

    public int Chamber { get; }

    public int Stars { get; }

    public OccurrenceTime CompletedAt { get; }
}

public sealed record SpiralAbyssFloorResult
{
    public SpiralAbyssFloorResult(
        int floor,
        DataCompleteness completeness,
        IEnumerable<SpiralAbyssChamberResult> chambers)
    {
        if (floor <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(floor));
        }
        if (!Enum.IsDefined(completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness));
        }
        SpiralAbyssChamberResult[] normalized = chambers
            .OrderBy(chamber => chamber.Chamber)
            .ToArray();
        if (normalized.Select(chamber => chamber.Chamber).Distinct().Count() !=
            normalized.Length)
        {
            throw new ArgumentException(
                "A floor cannot contain duplicate chambers.",
                nameof(chambers));
        }

        Floor = floor;
        Completeness = completeness;
        Chambers = normalized;
    }

    public int Floor { get; }

    public DataCompleteness Completeness { get; }

    public IReadOnlyList<SpiralAbyssChamberResult> Chambers { get; }

    public int Stars => Chambers.Sum(chamber => chamber.Stars);
}

public sealed record ChallengeParticipant
{
    public ChallengeParticipant(
        string id,
        string? name,
        string? iconUrl,
        int? level,
        int? rarity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (level is < 0 || rarity is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "Participant level and rarity cannot be negative.");
        }
        Id = id.Trim();
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
        Level = level;
        Rarity = rarity;
    }

    public string Id { get; }

    public string? Name { get; }

    public string? IconUrl { get; }

    public int? Level { get; }

    public int? Rarity { get; }
}

public sealed record ChallengeEnemy
{
    public ChallengeEnemy(string? name, int? level, string? iconUrl)
    {
        if (level is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                "Enemy level cannot be negative.");
        }
        Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        Level = level;
        IconUrl = string.IsNullOrWhiteSpace(iconUrl) ? null : iconUrl.Trim();
    }

    public string? Name { get; }

    public int? Level { get; }

    public string? IconUrl { get; }
}

public sealed record ChallengeStageResult
{
    public ChallengeStageResult(
        string stageId,
        int? score,
        OccurrenceTime completedAt,
        IEnumerable<ChallengeParticipant>? participants = null,
        IEnumerable<string>? buffs = null,
        IEnumerable<ChallengeEnemy>? enemies = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stageId);
        if (score is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(score),
                "Challenge stage score cannot be negative.");
        }
        ArgumentNullException.ThrowIfNull(completedAt);

        StageId = stageId.Trim();
        Score = score;
        CompletedAt = completedAt;
        Participants = (participants ?? []).ToArray();
        Buffs = (buffs ?? [])
            .Where(buff => !string.IsNullOrWhiteSpace(buff))
            .Select(buff => buff.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Enemies = (enemies ?? []).ToArray();
    }

    /// <summary>Official round id or a stable challenge name.</summary>
    public string StageId { get; }

    /// <summary>Medal flag for the theater or challenge seconds otherwise.</summary>
    public int? Score { get; }

    public OccurrenceTime CompletedAt { get; }

    public IReadOnlyList<ChallengeParticipant> Participants { get; }

    public IReadOnlyList<string> Buffs { get; }

    public IReadOnlyList<ChallengeEnemy> Enemies { get; }
}

public sealed record ChallengeObservation
{
    public ChallengeObservation(
        ChallengeObservationId observationId,
        Guid gameAccountId,
        ChallengeMode mode,
        string seasonId,
        DateTimeOffset? periodStart,
        DateTimeOffset? periodEnd,
        DataCompleteness completeness,
        IEnumerable<SpiralAbyssFloorResult> spiralAbyssFloors,
        RecordProvenance provenance,
        IEnumerable<ChallengeStageResult>? stages = null,
        int? difficulty = null,
        int? medalCount = null)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(seasonId);
        if (periodStart is not null && periodEnd is not null &&
            periodEnd.Value.ToUniversalTime() <
            periodStart.Value.ToUniversalTime())
        {
            throw new ArgumentOutOfRangeException(
                nameof(periodEnd),
                "Challenge period end cannot precede its start.");
        }
        if (!Enum.IsDefined(completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(completeness));
        }
        ArgumentNullException.ThrowIfNull(provenance);
        SpiralAbyssFloorResult[] normalized = spiralAbyssFloors
            .OrderBy(floor => floor.Floor)
            .ToArray();
        if (normalized.Select(floor => floor.Floor).Distinct().Count() !=
            normalized.Length)
        {
            throw new ArgumentException(
                "A challenge observation cannot contain duplicate floors.",
                nameof(spiralAbyssFloors));
        }
        if (mode != ChallengeMode.SpiralAbyss && normalized.Length > 0)
        {
            throw new ArgumentException(
                "Spiral Abyss floors cannot be attached to another challenge mode.",
                nameof(spiralAbyssFloors));
        }
        if (difficulty is < 0 || medalCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(difficulty),
                "Challenge difficulty and medal counts cannot be negative.");
        }
        ChallengeStageResult[] normalizedStages = (stages ?? [])
            .OrderBy(stage => stage.StageId, StringComparer.Ordinal)
            .ToArray();
        if (normalizedStages.Select(stage => stage.StageId)
            .Distinct(StringComparer.Ordinal).Count() != normalizedStages.Length)
        {
            throw new ArgumentException(
                "A challenge observation cannot contain duplicate stages.",
                nameof(stages));
        }
        if (mode == ChallengeMode.SpiralAbyss && normalizedStages.Length > 0)
        {
            throw new ArgumentException(
                "Generic stages cannot be attached to Spiral Abyss.",
                nameof(stages));
        }

        ObservationId = observationId;
        GameAccountId = gameAccountId;
        Mode = mode;
        SeasonId = seasonId.Trim();
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Completeness = completeness;
        SpiralAbyssFloors = normalized;
        Stages = normalizedStages;
        Difficulty = difficulty;
        MedalCount = medalCount;
        Provenance = provenance;
    }

    public ChallengeObservationId ObservationId { get; }

    public Guid GameAccountId { get; }

    public ChallengeMode Mode { get; }

    public string SeasonId { get; }

    public DateTimeOffset? PeriodStart { get; }

    public DateTimeOffset? PeriodEnd { get; }

    public DataCompleteness Completeness { get; }

    public IReadOnlyList<SpiralAbyssFloorResult> SpiralAbyssFloors { get; }

    /// <summary>Mode-specific rounds or challenges for non-Abyss modes.</summary>
    public IReadOnlyList<ChallengeStageResult> Stages { get; }

    public int? Difficulty { get; }

    public int? MedalCount { get; }

    public RecordProvenance Provenance { get; }
}

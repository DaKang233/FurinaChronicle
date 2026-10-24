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
        RecordProvenance provenance)
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

        ObservationId = observationId;
        GameAccountId = gameAccountId;
        Mode = mode;
        SeasonId = seasonId.Trim();
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
        Completeness = completeness;
        SpiralAbyssFloors = normalized;
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

    public RecordProvenance Provenance { get; }
}

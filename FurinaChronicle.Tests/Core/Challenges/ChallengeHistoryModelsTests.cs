// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Tests.Core.Challenges;

public sealed class ChallengeHistoryModelsTests
{
    [Fact]
    public void UnknownCompletionTimeStaysUnknown()
    {
        var result = new SpiralAbyssChamberResult(
            chamber: 3,
            stars: 3,
            OccurrenceTime.Unknown);

        Assert.Equal(OccurrenceTimeKind.Unknown, result.CompletedAt.Kind);
    }

    [Fact]
    public void CanonicalFloorOrderDoesNotDependOnSourceOrder()
    {
        SpiralAbyssFloorResult floor12 = Floor(12, stars: 9);
        SpiralAbyssFloorResult floor11 = Floor(11, stars: 8);

        var observation = new ChallengeObservation(
            ChallengeObservationId.New(),
            Guid.NewGuid(),
            ChallengeMode.SpiralAbyss,
            "2026-10-upper",
            periodStart: null,
            periodEnd: null,
            DataCompleteness.Partial,
            [floor12, floor11],
            RecordProvenance.Unknown);

        Assert.Equal([11, 12], observation.SpiralAbyssFloors
            .Select(floor => floor.Floor));
    }

    [Fact]
    public void SyntheticTheaterShapeCannotCarryAbyssFloors()
    {
        Assert.Throws<ArgumentException>(() => new ChallengeObservation(
            ChallengeObservationId.New(),
            Guid.NewGuid(),
            ChallengeMode.ImaginariumTheater,
            "synthetic",
            null,
            null,
            DataCompleteness.Unknown,
            [Floor(1, 3)],
            RecordProvenance.Unknown));
    }

    private static SpiralAbyssFloorResult Floor(int floor, int stars) =>
        new(
            floor,
            DataCompleteness.Complete,
            [new SpiralAbyssChamberResult(
                1,
                Math.Min(3, stars),
                OccurrenceTime.Unknown),
             new SpiralAbyssChamberResult(
                2,
                Math.Min(3, Math.Max(0, stars - 3)),
                OccurrenceTime.Unknown),
             new SpiralAbyssChamberResult(
                3,
                Math.Min(3, Math.Max(0, stars - 6)),
                OccurrenceTime.Unknown)]);
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Core.Chronicle;

public sealed class ChronicleModelsTests
{
    [Fact]
    public void EntryIdentity_IsDeterministicAndContentIndependent()
    {
        Guid accountId = Guid.Parse("11111111-1111-4111-8111-111111111111");

        ChronicleEntryId first = ChronicleEntryId.Create(
            accountId,
            ChronicleDomain.Gacha,
            ChronicleEntryKind.GachaAcquisition,
            "gacha-record",
            "10001",
            1);
        ChronicleEntryId repeated = ChronicleEntryId.Create(
            accountId,
            ChronicleDomain.Gacha,
            ChronicleEntryKind.GachaAcquisition,
            "gacha-record",
            "10001",
            1);
        ChronicleEntryId anotherAccount = ChronicleEntryId.Create(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            ChronicleDomain.Gacha,
            ChronicleEntryKind.GachaAcquisition,
            "gacha-record",
            "10001",
            1);

        Assert.Equal(first, repeated);
        Assert.NotEqual(first, anotherAccount);
    }

    [Fact]
    public void Time_UsesUncertainUpperBoundWithoutInventingOccurrence()
    {
        DateTimeOffset earlier = new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(8));
        DateTimeOffset later = earlier.AddDays(1);
        var time = new ChronicleTime(
            OccurrenceTime.Within(earlier, later, TimePrecision.Second),
            observedAt: later.AddHours(1),
            fetchedAt: later.AddHours(2));

        Assert.Equal(ChronicleSortBasis.Occurred, time.SortBasis);
        Assert.Equal(later.ToUniversalTime(), time.SortAtUtc);
        Assert.True(time.Overlaps(earlier.AddHours(12), later.AddHours(12)));
    }

    [Fact]
    public void Time_WithNoFactOrObservation_RemainsUndated()
    {
        var time = new ChronicleTime(null, null, null);

        Assert.True(time.IsUndated);
        Assert.Null(time.SortAtUtc);
        Assert.Equal(ChronicleSortBasis.Unknown, time.SortBasis);
    }

    [Fact]
    public void Profile_StateChangesRequireExplicitSwitch()
    {
        ChronicleEntry entry = Entry(
            ChronicleEntryKind.StateChange,
            ChronicleTimeline.Character,
            ChronicleGranularity.Default);

        Assert.False(ChronicleProfileRules.IsVisible(
            entry,
            new ChronicleProfileRequest(ChronicleTimeline.Character)));
        Assert.True(ChronicleProfileRules.IsVisible(
            entry,
            new ChronicleProfileRequest(
                ChronicleTimeline.Character,
                IncludeStateChanges: true)));
    }

    private static ChronicleEntry Entry(
        ChronicleEntryKind kind,
        ChronicleTimeline timelines,
        ChronicleGranularity minimumGranularity) => new(
            ChronicleEntryId.Create(
                Guid.Parse("11111111-1111-4111-8111-111111111111"),
                ChronicleDomain.Inventory,
                kind,
                "fixture",
                "one",
                1),
            portableEntityKey: null,
            projectionVersion: 1,
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            ChronicleDomain.Inventory,
            kind,
            timelines,
            minimumGranularity,
            milestone: null,
            new ChronicleTime(null, null, null),
            DataOrigin.LocalObservation,
            DataOrigin.Derived,
            DataConfidence.Unknown,
            DataCompleteness.Unknown,
            "fixture",
            "one",
            "chronicle.fixture",
            new Dictionary<string, string>(),
            ChronicleImportance.Normal,
            ChronicleEvidence.Unknown("fixture"));
}

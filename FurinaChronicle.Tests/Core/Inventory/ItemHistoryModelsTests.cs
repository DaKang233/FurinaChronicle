// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Tests.Core.Inventory;

public sealed class ItemHistoryModelsTests
{
    [Fact]
    public void WeaponWithoutInstanceIdRequiresAggregateQuantity()
    {
        Assert.Throws<ArgumentException>(() => new InventoryStateEntry(
            InventoryItemKind.Weapon,
            "WEAPON_1001",
            instanceId: null,
            quantity: null,
            level: 90,
            progression: 1,
            InventoryFieldCoverage.Level |
                InventoryFieldCoverage.Progression,
            DataCompleteness.Partial));
    }

    [Fact]
    public void Compare_PartialObservationDoesNotInventRemoval()
    {
        Guid accountId = Guid.NewGuid();
        ItemStateObservation before = Observation(
            accountId,
            DataCompleteness.Complete,
            Entry("1001", level: 80),
            Entry("1002", level: 70));
        ItemStateObservation after = Observation(
            accountId,
            DataCompleteness.Partial,
            Entry("1001", level: 90));

        IReadOnlyList<InventoryStateChange> changes =
            ItemStateHistoryComparer.Compare(before, after);

        InventoryStateChange changed = Assert.Single(changes);
        Assert.Equal(InventoryStateChangeKind.Changed, changed.ChangeKind);
        Assert.DoesNotContain(
            changes,
            change => change.ChangeKind == InventoryStateChangeKind.Removed);
    }

    [Fact]
    public void Compare_NewItemAfterPartialBaselineIsUncertain()
    {
        Guid accountId = Guid.NewGuid();
        ItemStateObservation before = Observation(
            accountId,
            DataCompleteness.Partial,
            Entry("1001", level: 80));
        ItemStateObservation after = Observation(
            accountId,
            DataCompleteness.Complete,
            Entry("1001", level: 80),
            Entry("1002", level: 1));

        InventoryStateChange added = Assert.Single(
            ItemStateHistoryComparer.Compare(before, after));

        Assert.Equal(InventoryStateChangeKind.Added, added.ChangeKind);
        Assert.Equal(
            InventoryStateChangeCertainty.Uncertain,
            added.Certainty);
    }

    [Fact]
    public void WeekUsesMondayBoundaryAndPreservesSourceOffset()
    {
        OccurrenceTime time = ManualAcquisitionTime.ForWeek(
            new DateOnly(2026, 10, 18),
            TimeSpan.FromHours(8));

        Assert.Equal(TimePrecision.Week, time.Precision);
        Assert.Equal(DayOfWeek.Monday, time.Start!.Value.DayOfWeek);
        Assert.Equal(TimeSpan.FromHours(8), time.Start.Value.Offset);
        Assert.Equal(time.Start.Value.AddDays(7), time.End);
    }

    [Theory]
    [InlineData(TimePrecision.Second)]
    [InlineData(TimePrecision.Minute)]
    [InlineData(TimePrecision.Hour)]
    [InlineData(TimePrecision.Day)]
    [InlineData(TimePrecision.Week)]
    public void ManualAcquisitionSupportsAcceptedPrecision(
        TimePrecision precision)
    {
        OccurrenceTime occurrence = precision == TimePrecision.Week
            ? ManualAcquisitionTime.ForWeek(
                new DateOnly(2026, 10, 18),
                TimeSpan.FromHours(8))
            : OccurrenceTime.At(
                new DateTimeOffset(
                    2026,
                    10,
                    18,
                    1,
                    2,
                    3,
                    TimeSpan.FromHours(8)),
                precision);

        var fact = new ManualAcquisition(
            ManualAcquisitionId.New(),
            Guid.NewGuid(),
            InventoryItemKind.Character,
            "CHAR_1001",
            occurrence,
            ManualAcquisitionReason.Other,
            "manual fixture",
            new RecordProvenance(DataOrigin.UserEntered));

        Assert.Equal(precision, fact.OccurredAt.Precision);
    }

    private static ItemStateObservation Observation(
        Guid accountId,
        DataCompleteness completeness,
        params InventoryStateEntry[] entries) =>
        new(
            ItemObservationId.New(),
            accountId,
            InventoryItemKind.Character,
            completeness,
            entries,
            RecordProvenance.Unknown);

    private static InventoryStateEntry Entry(string typeId, int level) =>
        new(
            InventoryItemKind.Character,
            typeId,
            instanceId: null,
            quantity: 1,
            level,
            progression: null,
            InventoryFieldCoverage.Quantity |
                InventoryFieldCoverage.Level,
            DataCompleteness.Partial);
}

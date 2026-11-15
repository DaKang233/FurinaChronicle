// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Tests.Core.Gacha;

public sealed class GachaRecordChangeClassifierTests
{
    [Fact]
    public void Compare_IdenticalRecords_ReturnsNoOp()
    {
        GachaRecord record = CreateRecord();

        GachaRecordChange change =
            GachaRecordChangeClassifier.Compare(record, record);

        Assert.Equal(GachaRecordChangeKind.NoOp, change.Kind);
        Assert.Empty(change.ChangedFields);
    }

    [Fact]
    public void Compare_OnlyFetchedAtAndBatchChanged_ReturnsAcquisitionMetadataOnly()
    {
        GachaRecord current = CreateRecord();
        GachaRecord proposed = current with
        {
            Provenance = new RecordProvenance(
                current.Provenance.Origin,
                current.Provenance.Timestamps with
                {
                    FetchedAt = DateTimeOffset.UtcNow
                },
                current.Provenance.Source,
                AcquisitionBatchId.New())
        };

        GachaRecordChange change =
            GachaRecordChangeClassifier.Compare(current, proposed);

        Assert.Equal(
            GachaRecordChangeKind.AcquisitionMetadataOnly,
            change.Kind);
        Assert.Equal(
            ["fetched_at", "acquisition_batch_id"],
            change.ChangedFields);
    }

    [Fact]
    public void Compare_FactAndOriginChanged_ReturnsSubstantive()
    {
        GachaRecord current = CreateRecord();
        GachaRecord proposed = current with
        {
            ItemName = "Corrected",
            Provenance = new RecordProvenance(
                DataOrigin.OfficialApi,
                current.Provenance.Timestamps)
        };

        GachaRecordChange change =
            GachaRecordChangeClassifier.Compare(current, proposed);

        Assert.Equal(GachaRecordChangeKind.Substantive, change.Kind);
        Assert.Contains("item_name", change.ChangedFields);
        Assert.Contains("origin", change.ChangedFields);
    }

    [Fact]
    public void Compare_DifferentStableReference_Throws()
    {
        GachaRecord current = CreateRecord();
        GachaRecord proposed = current with
        {
            ExternalRecordId = "different"
        };

        Assert.Throws<ArgumentException>(
            () => GachaRecordChangeClassifier.Compare(current, proposed));
    }

    private static GachaRecord CreateRecord()
    {
        return new GachaRecord(
            Guid.NewGuid(),
            "100",
            "Item",
            5,
            new DateTimeOffset(
                2026,
                1,
                1,
                8,
                0,
                0,
                TimeSpan.FromHours(8)))
        {
            ItemId = "item-1",
            ItemType = "Avatar",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(
                DataOrigin.StandardImport,
                new RecordTimestamps(
                    ImportedAt: DateTimeOffset.UtcNow))
        };
    }
}

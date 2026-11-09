// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Gacha;

public readonly record struct GachaFactReference
{
    public GachaFactReference(
        Guid gameAccountId,
        string externalRecordId)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(externalRecordId);

        GameAccountId = gameAccountId;
        ExternalRecordId = externalRecordId;
    }

    public Guid GameAccountId { get; }

    public string ExternalRecordId { get; }

    public override string ToString() =>
        $"{GameAccountId:D}/{ExternalRecordId}";
}

public enum GachaRecordChangeKind
{
    NoOp = 0,
    AcquisitionMetadataOnly = 1,
    Substantive = 2,
}

public sealed record GachaRecordChange(
    GachaFactReference Reference,
    GachaRecordChangeKind Kind,
    IReadOnlyList<string> ChangedFields);

public static class GachaRecordChangeClassifier
{
    public static GachaRecordChange Compare(
        GachaRecord current,
        GachaRecord proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);

        var currentReference = new GachaFactReference(
            current.GameAccountId,
            current.ExternalRecordId);
        var proposedReference = new GachaFactReference(
            proposed.GameAccountId,
            proposed.ExternalRecordId);
        if (currentReference != proposedReference)
        {
            throw new ArgumentException(
                "Gacha records must have the same stable fact reference.",
                nameof(proposed));
        }

        var substantive = new List<string>();
        AddDifference(substantive, "item_name", current.ItemName, proposed.ItemName);
        AddDifference(substantive, "item_id", current.ItemId, proposed.ItemId);
        AddDifference(substantive, "item_type", current.ItemType, proposed.ItemType);
        AddDifference(substantive, "gacha_type", current.GachaType, proposed.GachaType);
        AddDifference(
            substantive,
            "uigf_gacha_type",
            current.UigfGachaType,
            proposed.UigfGachaType);
        AddDifference(substantive, "rank_type", current.RankType, proposed.RankType);
        AddDifference(substantive, "count", current.Count, proposed.Count);
        AddDifference(
            substantive,
            "occurred_at_utc_ticks",
            current.Time.UtcDateTime.Ticks,
            proposed.Time.UtcDateTime.Ticks);
        AddDifference(
            substantive,
            "occurred_at_offset",
            current.Time.Offset,
            proposed.Time.Offset);
        AddDifference(
            substantive,
            "origin",
            current.Provenance.Origin,
            proposed.Provenance.Origin);
        AddDifference(
            substantive,
            "observed_at",
            current.Provenance.Timestamps.ObservedAt,
            proposed.Provenance.Timestamps.ObservedAt);
        AddDifference(
            substantive,
            "imported_at",
            current.Provenance.Timestamps.ImportedAt,
            proposed.Provenance.Timestamps.ImportedAt);
        AddDifference(
            substantive,
            "source",
            current.Provenance.Source,
            proposed.Provenance.Source);

        var acquisition = new List<string>();
        AddDifference(
            acquisition,
            "fetched_at",
            current.Provenance.Timestamps.FetchedAt,
            proposed.Provenance.Timestamps.FetchedAt);
        AddDifference(
            acquisition,
            "acquisition_batch_id",
            current.Provenance.AcquisitionBatchId,
            proposed.Provenance.AcquisitionBatchId);

        IReadOnlyList<string> fields = [.. substantive, .. acquisition];
        GachaRecordChangeKind kind = substantive.Count > 0
            ? GachaRecordChangeKind.Substantive
            : acquisition.Count > 0
                ? GachaRecordChangeKind.AcquisitionMetadataOnly
                : GachaRecordChangeKind.NoOp;
        return new GachaRecordChange(currentReference, kind, fields);
    }

    private static void AddDifference<T>(
        ICollection<string> fields,
        string field,
        T current,
        T proposed)
    {
        if (!EqualityComparer<T>.Default.Equals(current, proposed))
        {
            fields.Add(field);
        }
    }
}

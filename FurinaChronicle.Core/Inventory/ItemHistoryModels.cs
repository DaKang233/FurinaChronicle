// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Core.Inventory;

public enum InventoryItemKind
{
    Character = 1,
    Weapon = 2,
}

[Flags]
public enum InventoryFieldCoverage
{
    None = 0,
    Quantity = 1,
    Level = 2,
    Progression = 4,
}

public readonly record struct ItemObservationId
{
    public ItemObservationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Item observation ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ItemObservationId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public sealed record InventoryStateEntry
{
    public InventoryStateEntry(
        InventoryItemKind kind,
        string typeId,
        string? instanceId,
        int? quantity,
        int? level,
        int? progression,
        InventoryFieldCoverage fieldCoverage,
        DataCompleteness fieldCompleteness)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        if (quantity is < 0 || level is < 0 || progression is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                "Inventory numeric values cannot be negative.");
        }
        if (!Enum.IsDefined(fieldCompleteness))
        {
            throw new ArgumentOutOfRangeException(nameof(fieldCompleteness));
        }
        if (kind == InventoryItemKind.Character && instanceId is not null)
        {
            throw new ArgumentException(
                "Character observations do not use instance IDs.",
                nameof(instanceId));
        }
        if (kind == InventoryItemKind.Weapon &&
            string.IsNullOrWhiteSpace(instanceId) && quantity is null)
        {
            throw new ArgumentException(
                "A weapon without a reliable instance ID must carry an aggregate quantity.",
                nameof(quantity));
        }

        Kind = kind;
        TypeId = typeId.Trim();
        InstanceId = string.IsNullOrWhiteSpace(instanceId)
            ? null
            : instanceId.Trim();
        Quantity = quantity;
        Level = level;
        Progression = progression;
        FieldCoverage = fieldCoverage;
        FieldCompleteness = fieldCompleteness;
    }

    public InventoryItemKind Kind { get; }

    public string TypeId { get; }

    public string? InstanceId { get; }

    public int? Quantity { get; }

    public int? Level { get; }

    /// <summary>Character constellation or weapon refinement, as declared by Kind.</summary>
    public int? Progression { get; }

    public InventoryFieldCoverage FieldCoverage { get; }

    public DataCompleteness FieldCompleteness { get; }

    public string StableItemKey => InstanceId is null
        ? $"{(int)Kind}:{TypeId}"
        : $"{(int)Kind}:{TypeId}:{InstanceId}";
}

public sealed record ItemStateObservation
{
    public ItemStateObservation(
        ItemObservationId observationId,
        Guid gameAccountId,
        InventoryItemKind kind,
        DataCompleteness collectionCompleteness,
        IEnumerable<InventoryStateEntry> entries,
        RecordProvenance provenance)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        if (!Enum.IsDefined(collectionCompleteness))
        {
            throw new ArgumentOutOfRangeException(
                nameof(collectionCompleteness));
        }
        ArgumentNullException.ThrowIfNull(provenance);
        InventoryStateEntry[] normalized = entries.ToArray();
        if (normalized.Any(entry => entry.Kind != kind))
        {
            throw new ArgumentException(
                "Every observation entry must match the observation kind.",
                nameof(entries));
        }
        if (normalized.Select(entry => entry.StableItemKey)
            .Distinct(StringComparer.Ordinal)
            .Count() != normalized.Length)
        {
            throw new ArgumentException(
                "An observation cannot contain duplicate stable item keys.",
                nameof(entries));
        }

        ObservationId = observationId;
        GameAccountId = gameAccountId;
        Kind = kind;
        CollectionCompleteness = collectionCompleteness;
        Entries = normalized;
        Provenance = provenance;
    }

    public ItemObservationId ObservationId { get; }

    public Guid GameAccountId { get; }

    public InventoryItemKind Kind { get; }

    public DataCompleteness CollectionCompleteness { get; }

    public IReadOnlyList<InventoryStateEntry> Entries { get; }

    public RecordProvenance Provenance { get; }
}

public enum ManualAcquisitionReason
{
    Unknown = 0,
    GachaRecord = 1,
    TemperedTraining = 2,
    AnniversarySelection = 3,
    EventSelection = 4,
    Other = 5,
}

public readonly record struct ManualAcquisitionId
{
    public ManualAcquisitionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException(
                "Manual acquisition ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static ManualAcquisitionId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("D");
}

public sealed record ManualAcquisition
{
    public ManualAcquisition(
        ManualAcquisitionId acquisitionId,
        Guid gameAccountId,
        InventoryItemKind kind,
        string typeId,
        OccurrenceTime occurredAt,
        ManualAcquisitionReason reason,
        string? reasonDetail,
        RecordProvenance provenance)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(occurredAt);
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.Origin != DataOrigin.UserEntered)
        {
            throw new ArgumentException(
                "Manual acquisition current facts must use UserEntered provenance.",
                nameof(provenance));
        }

        AcquisitionId = acquisitionId;
        GameAccountId = gameAccountId;
        Kind = kind;
        TypeId = typeId.Trim();
        OccurredAt = occurredAt;
        Reason = reason;
        ReasonDetail = string.IsNullOrWhiteSpace(reasonDetail)
            ? null
            : reasonDetail.Trim();
        Provenance = provenance;
    }

    public ManualAcquisitionId AcquisitionId { get; }

    public Guid GameAccountId { get; }

    public InventoryItemKind Kind { get; }

    public string TypeId { get; }

    public OccurrenceTime OccurredAt { get; }

    public ManualAcquisitionReason Reason { get; }

    public string? ReasonDetail { get; }

    public RecordProvenance Provenance { get; }
}

public static class ManualAcquisitionTime
{
    public static OccurrenceTime ForWeek(
        DateOnly date,
        TimeSpan sourceOffset)
    {
        if (sourceOffset < TimeSpan.FromHours(-14) ||
            sourceOffset > TimeSpan.FromHours(14))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceOffset));
        }

        int daysFromMonday = ((int)date.DayOfWeek + 6) % 7;
        DateOnly monday = date.AddDays(-daysFromMonday);
        var start = new DateTimeOffset(
            monday.ToDateTime(TimeOnly.MinValue),
            sourceOffset);
        return OccurrenceTime.ForInterval(
            start,
            start.AddDays(7),
            TimePrecision.Week);
    }
}

public enum InventoryStateChangeKind
{
    Added = 1,
    Changed = 2,
    Removed = 3,
}

public enum InventoryStateChangeCertainty
{
    Exact = 1,
    Uncertain = 2,
}

public sealed record InventoryStateChange(
    string StableItemKey,
    InventoryStateChangeKind ChangeKind,
    InventoryStateChangeCertainty Certainty,
    InventoryStateEntry? Before,
    InventoryStateEntry? After);

public static class ItemStateHistoryComparer
{
    public static IReadOnlyList<InventoryStateChange> Compare(
        ItemStateObservation before,
        ItemStateObservation after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.GameAccountId != after.GameAccountId ||
            before.Kind != after.Kind)
        {
            throw new ArgumentException(
                "State observations must describe the same account and item kind.");
        }

        Dictionary<string, InventoryStateEntry> earlier =
            before.Entries.ToDictionary(
                entry => entry.StableItemKey,
                StringComparer.Ordinal);
        Dictionary<string, InventoryStateEntry> later =
            after.Entries.ToDictionary(
                entry => entry.StableItemKey,
                StringComparer.Ordinal);
        var changes = new List<InventoryStateChange>();
        foreach ((string key, InventoryStateEntry current) in later)
        {
            if (!earlier.TryGetValue(key, out InventoryStateEntry? previous))
            {
                changes.Add(new InventoryStateChange(
                    key,
                    InventoryStateChangeKind.Added,
                    before.CollectionCompleteness == DataCompleteness.Complete
                        ? InventoryStateChangeCertainty.Exact
                        : InventoryStateChangeCertainty.Uncertain,
                    null,
                    current));
            }
            else if (previous != current)
            {
                changes.Add(new InventoryStateChange(
                    key,
                    InventoryStateChangeKind.Changed,
                    InventoryStateChangeCertainty.Exact,
                    previous,
                    current));
            }
        }

        if (after.CollectionCompleteness == DataCompleteness.Complete)
        {
            foreach ((string key, InventoryStateEntry previous) in earlier)
            {
                if (!later.ContainsKey(key))
                {
                    changes.Add(new InventoryStateChange(
                        key,
                        InventoryStateChangeKind.Removed,
                        before.CollectionCompleteness == DataCompleteness.Complete
                            ? InventoryStateChangeCertainty.Exact
                            : InventoryStateChangeCertainty.Uncertain,
                        previous,
                        null));
                }
            }
        }

        return changes.OrderBy(change => change.StableItemKey).ToArray();
    }
}

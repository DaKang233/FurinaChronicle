// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Chronicle;

public sealed class InventoryChronicleContributor
{
    public IReadOnlyList<ChronicleEntry> Project(
        Guid gameAccountId,
        IReadOnlyList<ItemStateObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (gameAccountId == Guid.Empty ||
            observations.Any(item => item.GameAccountId != gameAccountId))
        {
            throw new ArgumentException("Inventory observations must belong to the projected account.");
        }

        var entries = new List<ChronicleEntry>();
        foreach (IGrouping<InventoryItemKind, ItemStateObservation> group in
                 observations.GroupBy(item => item.Kind))
        {
            ItemStateObservation[] ordered = group
                .OrderBy(Anchor)
                .ThenBy(item => item.ObservationId.Value)
                .ToArray();
            foreach (ItemStateObservation observation in ordered)
            {
                entries.Add(ObservationEntry(observation));
            }
            for (int index = 1; index < ordered.Length; index++)
            {
                entries.AddRange(Changes(ordered[index - 1], ordered[index]));
            }
        }
        return entries;
    }

    private static ChronicleEntry ObservationEntry(ItemStateObservation value)
    {
        string sourceId = value.ObservationId.ToString();
        return Create(
            value,
            ChronicleEntryKind.StateObservation,
            sourceId,
            new ChronicleTime(null, value.Provenance.Timestamps.ObservedAt,
                value.Provenance.Timestamps.FetchedAt),
            "chronicle.inventory.observation",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["item_kind"] = value.Kind.ToString(),
                ["entry_count"] = value.Entries.Count.ToString(),
            },
            new ChronicleEvidence(
                ChronicleEvidenceStatus.Known,
                ChronicleEvidenceScope.LocalArchiveRecords,
                "chronicle.evidence.saved-observation",
                [$"item-observation:{sourceId}"]),
            value.Provenance.Origin,
            value.CollectionCompleteness);
    }

    private static IEnumerable<ChronicleEntry> Changes(
        ItemStateObservation before,
        ItemStateObservation after)
    {
        Dictionary<string, InventoryStateEntry> left = before.Entries
            .ToDictionary(item => item.StableItemKey, StringComparer.Ordinal);
        Dictionary<string, InventoryStateEntry> right = after.Entries
            .ToDictionary(item => item.StableItemKey, StringComparer.Ordinal);
        foreach (string key in left.Keys.Union(right.Keys, StringComparer.Ordinal)
                     .Order(StringComparer.Ordinal))
        {
            left.TryGetValue(key, out InventoryStateEntry? oldValue);
            right.TryGetValue(key, out InventoryStateEntry? newValue);
            bool bothComplete = before.CollectionCompleteness == DataCompleteness.Complete &&
                after.CollectionCompleteness == DataCompleteness.Complete;
            string? change = oldValue is null && bothComplete ? "added" :
                newValue is null && bothComplete ? "removed" :
                oldValue is not null && newValue is not null &&
                HasCoveredDifference(oldValue, newValue) ? "changed" : null;
            if (change is null)
            {
                continue;
            }

            string sourceId = $"{before.ObservationId}:{after.ObservationId}:{key}:{change}";
            InventoryStateEntry item = newValue ?? oldValue!;
            DateTimeOffset? start = Anchor(before);
            DateTimeOffset? end = Anchor(after);
            OccurrenceTime? occurred = start is not null && end is not null && end >= start
                ? OccurrenceTime.Within(start.Value, end.Value, TimePrecision.Second)
                : null;
            yield return Create(
                after,
                ChronicleEntryKind.StateChange,
                sourceId,
                new ChronicleTime(occurred, after.Provenance.Timestamps.ObservedAt,
                    after.Provenance.Timestamps.FetchedAt),
                "chronicle.inventory.state-change",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["item_key"] = key,
                    ["change"] = change,
                },
                new ChronicleEvidence(
                    ChronicleEvidenceStatus.Known,
                    ChronicleEvidenceScope.LocalArchiveRecords,
                    bothComplete
                        ? "chronicle.evidence.complete-adjacent-observations"
                        : "chronicle.evidence.covered-adjacent-fields",
                    [$"item-observation:{before.ObservationId}",
                     $"item-observation:{after.ObservationId}"]),
                DataOrigin.Derived,
                after.CollectionCompleteness,
                item.TypeId);
        }
    }

    private static bool HasCoveredDifference(
        InventoryStateEntry left,
        InventoryStateEntry right)
    {
        InventoryFieldCoverage shared = left.FieldCoverage & right.FieldCoverage;
        return shared.HasFlag(InventoryFieldCoverage.Quantity) && left.Quantity != right.Quantity ||
            shared.HasFlag(InventoryFieldCoverage.Level) && left.Level != right.Level ||
            shared.HasFlag(InventoryFieldCoverage.Progression) && left.Progression != right.Progression;
    }

    private static ChronicleEntry Create(
        ItemStateObservation observation,
        ChronicleEntryKind kind,
        string sourceId,
        ChronicleTime time,
        string title,
        IReadOnlyDictionary<string, string> arguments,
        ChronicleEvidence evidence,
        DataOrigin annotationOrigin,
        DataCompleteness completeness,
        string? itemId = null) => new(
            ChronicleEntryId.Create(observation.GameAccountId, ChronicleDomain.Inventory,
                kind, "item-observation", sourceId, ChronicleVersions.Projection),
            null,
            ChronicleVersions.Projection,
            observation.GameAccountId,
            ChronicleDomain.Inventory,
            kind,
            observation.Kind == InventoryItemKind.Character
                ? ChronicleTimeline.Character
                : ChronicleTimeline.Weapon,
            ChronicleGranularity.Detailed,
            null,
            time,
            observation.Provenance.Origin,
            annotationOrigin,
            DataConfidence.Unknown,
            completeness,
            "item-observation",
            sourceId,
            title,
            arguments,
            ChronicleImportance.Minor,
            evidence,
            itemId);

    private static DateTimeOffset? Anchor(ItemStateObservation value) =>
        value.Provenance.Timestamps.ObservedAt?.ToUniversalTime() ??
        value.Provenance.Timestamps.FetchedAt?.ToUniversalTime();
}

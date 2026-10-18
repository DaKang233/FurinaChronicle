// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Chronicle;

public sealed class GachaAcquisitionChronicleContributor
{
    public IReadOnlyList<ChronicleEntry> Project(
        Guid gameAccountId,
        IReadOnlyList<GachaRecord> gachaRecords,
        IReadOnlyList<ManualAcquisitionState> manualAcquisitions,
        IChronicleItemCatalogSnapshot itemCatalog,
        ChronicleProjectionEvidence evidence)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException(
                "Game account ID cannot be empty.",
                nameof(gameAccountId));
        }
        ArgumentNullException.ThrowIfNull(gachaRecords);
        ArgumentNullException.ThrowIfNull(manualAcquisitions);
        ArgumentNullException.ThrowIfNull(itemCatalog);
        ArgumentNullException.ThrowIfNull(evidence);
        if (gachaRecords.Any(item => item.GameAccountId != gameAccountId) ||
            manualAcquisitions.Any(item =>
                item.Acquisition.GameAccountId != gameAccountId))
        {
            throw new ArgumentException(
                "Every acquisition must belong to the projected account.");
        }

        Dictionary<string, PityEvidence> pity = BuildPityEvidence(
            gachaRecords,
            evidence.DeclaresCompleteGachaHistory);
        List<AcquisitionCandidate> candidates = [
            .. gachaRecords.Select(record => FromGacha(record, itemCatalog, pity)),
            .. manualAcquisitions
                .Where(item => item.Version.Value > 0)
                .Select(item => FromManual(item.Acquisition, itemCatalog)),
        ];
        var ordinals = new Dictionary<AcquisitionCandidate, OrdinalEvidence>();
        foreach (IGrouping<string, AcquisitionCandidate> group in candidates
                     .Where(item => item.ItemId is not null)
                     .GroupBy(
                         item => $"{(int?)item.Kind}:{item.ItemId}",
                         StringComparer.Ordinal))
        {
            AssignOrdinals(
                group.ToArray(),
                evidence.DeclaresCompleteGachaHistory,
                ordinals);
        }

        var entries = new List<ChronicleEntry>();
        foreach (AcquisitionCandidate candidate in candidates)
        {
            ordinals.TryGetValue(candidate, out OrdinalEvidence? ordinal);
            ChronicleEntry? entry = BuildEntry(
                gameAccountId,
                candidate,
                ordinal);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries
            .OrderByDescending(item => item.Time.SortAtUtc is not null)
            .ThenByDescending(item => item.Time.SortAtUtc)
            .ThenByDescending(item => item.EntryId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static AcquisitionCandidate FromGacha(
        GachaRecord record,
        IChronicleItemCatalogSnapshot catalog,
        IReadOnlyDictionary<string, PityEvidence> pity)
    {
        string? itemId = Normalize(record.ItemId);
        ChronicleItemDescriptor? descriptor = itemId is null
            ? null
            : catalog.Find(itemId);
        InventoryItemKind? kind = descriptor?.Kind ??
            ParseStableItemKind(record.ItemType);
        int? rank = record.RankType ?? descriptor?.RankType;
        pity.TryGetValue(record.ExternalRecordId, out PityEvidence? pityResult);
        return new AcquisitionCandidate(
            SourceEntityType: "gacha-record",
            record.ExternalRecordId,
            PortableEntityKey: null,
            itemId,
            kind,
            rank,
            Math.Max(1, record.Count),
            new ChronicleTime(
                OccurrenceTime.At(record.Time, TimePrecision.Second),
                record.Provenance.Timestamps.ObservedAt,
                record.Provenance.Timestamps.FetchedAt),
            record.Provenance.Origin,
            IsGacha: true,
            pityResult);
    }

    private static AcquisitionCandidate FromManual(
        ManualAcquisition acquisition,
        IChronicleItemCatalogSnapshot catalog)
    {
        ChronicleItemDescriptor? descriptor = catalog.Find(acquisition.TypeId);
        return new AcquisitionCandidate(
            SourceEntityType: "manual-acquisition",
            acquisition.AcquisitionId.ToString(),
            $"manual:{acquisition.AcquisitionId}",
            acquisition.TypeId,
            acquisition.Kind,
            descriptor?.RankType,
            Count: 1,
            new ChronicleTime(
                acquisition.OccurredAt,
                acquisition.Provenance.Timestamps.ObservedAt,
                acquisition.Provenance.Timestamps.FetchedAt),
            acquisition.Provenance.Origin,
            IsGacha: false,
            Pity: null);
    }

    private static ChronicleEntry? BuildEntry(
        Guid accountId,
        AcquisitionCandidate candidate,
        OrdinalEvidence? ordinal)
    {
        bool isFiveStarGacha = candidate.IsGacha && candidate.RankType == 5;
        ChronicleTimeline timelines = isFiveStarGacha
            ? ChronicleTimeline.Main
            : ChronicleTimeline.None;
        ChronicleGranularity granularity = ChronicleGranularity.Detailed;
        string? milestone = null;
        ChronicleEvidence acquisitionEvidence = ordinal?.Evidence ??
            ChronicleEvidence.Unknown(
                candidate.ItemId is null
                    ? "chronicle.evidence.item-id-unknown"
                    : "chronicle.evidence.order-unknown",
                $"{candidate.SourceEntityType}:{candidate.SourceEntityId}");

        if (candidate.Kind == InventoryItemKind.Character)
        {
            timelines |= ChronicleTimeline.Character;
            if (ordinal?.Ordinal is 1 or 7)
            {
                granularity = ChronicleGranularity.Default;
                milestone = ordinal.Milestone;
                if (candidate.RankType == 4)
                {
                    timelines |= ChronicleTimeline.Main;
                }
            }
        }
        else if (candidate.Kind == InventoryItemKind.Weapon)
        {
            timelines |= ChronicleTimeline.Weapon;
            if (ordinal?.Ordinal == 1)
            {
                granularity = ChronicleGranularity.Default;
                milestone = ordinal.Milestone;
            }
            else if (candidate.RankType is null or <= 3)
            {
                timelines &= ~ChronicleTimeline.Weapon;
            }
        }

        if (timelines == ChronicleTimeline.None)
        {
            return null;
        }

        var arguments = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["source_scope"] = candidate.IsGacha
                ? "local-gacha-records"
                : "local-archive-records",
        };
        if (candidate.ItemId is not null)
        {
            arguments["item_id"] = candidate.ItemId;
        }
        if (candidate.RankType is int rank)
        {
            arguments["rank"] = rank.ToString();
        }
        if (ordinal?.Ordinal is int ordinalValue)
        {
            arguments["ordinal"] = ordinalValue.ToString();
        }

        ChronicleDomain domain = candidate.IsGacha
            ? ChronicleDomain.Gacha
            : ChronicleDomain.Acquisition;
        ChronicleEntryKind kind = candidate.IsGacha
            ? ChronicleEntryKind.GachaAcquisition
            : ChronicleEntryKind.ManualAcquisition;
        return new ChronicleEntry(
            ChronicleEntryId.Create(
                accountId,
                domain,
                kind,
                candidate.SourceEntityType,
                candidate.SourceEntityId,
                ChronicleVersions.Projection),
            candidate.PortableEntityKey,
            ChronicleVersions.Projection,
            accountId,
            domain,
            kind,
            timelines,
            granularity,
            milestone,
            candidate.Time,
            candidate.BaseOrigin,
            ordinal is null && candidate.Pity is null
                ? candidate.BaseOrigin
                : DataOrigin.Derived,
            DataConfidence.Unknown,
            DataCompleteness.Unknown,
            candidate.SourceEntityType,
            candidate.SourceEntityId,
            candidate.IsGacha
                ? "chronicle.gacha.acquisition"
                : "chronicle.manual.acquisition",
            arguments,
            isFiveStarGacha || timelines.HasFlag(ChronicleTimeline.Main)
                ? ChronicleImportance.Major
                : ChronicleImportance.Normal,
            acquisitionEvidence,
            candidate.ItemId,
            candidate.RankType,
            localRecordedPulls: candidate.Pity?.LocalRecordedPulls,
            exactPity: candidate.Pity?.ExactPity);
    }

    private static void AssignOrdinals(
        IReadOnlyList<AcquisitionCandidate> source,
        bool declaredComplete,
        IDictionary<AcquisitionCandidate, OrdinalEvidence> target)
    {
        AcquisitionCandidate[] ordered = source
            .OrderBy(item => item.Time.Occurred?.UtcStart)
            .ThenBy(item => item.Time.Occurred?.UtcEnd)
            .ThenBy(item => item.SourceEntityType, StringComparer.Ordinal)
            .ThenBy(item => item.SourceEntityId, StringComparer.Ordinal)
            .ToArray();
        bool definite = ordered.All(item =>
                item.Count == 1 && item.Time.Occurred is not null) &&
            ordered.Zip(ordered.Skip(1), IsStrictlyBefore).All(value => value);
        if (!definite)
        {
            foreach (AcquisitionCandidate candidate in ordered)
            {
                target[candidate] = new OrdinalEvidence(
                    Ordinal: null,
                    Milestone: null,
                    new ChronicleEvidence(
                        ChronicleEvidenceStatus.Ambiguous,
                        ChronicleEvidenceScope.LocalArchiveRecords,
                        "chronicle.evidence.acquisition-order-ambiguous",
                        ordered.Select(item =>
                            $"{item.SourceEntityType}:{item.SourceEntityId}")
                            .ToArray()));
            }
            return;
        }

        for (int index = 0; index < ordered.Length; index++)
        {
            int ordinal = index + 1;
            string? milestone = ordinal switch
            {
                1 => declaredComplete
                    ? "first-acquisition"
                    : "local-first-record",
                7 => declaredComplete
                    ? "seventh-acquisition"
                    : "local-seventh-record",
                _ => null,
            };
            target[ordered[index]] = new OrdinalEvidence(
                ordinal,
                milestone,
                new ChronicleEvidence(
                    ChronicleEvidenceStatus.Known,
                    declaredComplete
                        ? ChronicleEvidenceScope.DeclaredCompleteHistory
                        : ordered[index].IsGacha
                            ? ChronicleEvidenceScope.LocalGachaRecords
                            : ChronicleEvidenceScope.LocalArchiveRecords,
                    declaredComplete
                        ? "chronicle.evidence.declared-complete-order"
                        : "chronicle.evidence.local-record-order",
                    ordered.Take(ordinal).Select(item =>
                        $"{item.SourceEntityType}:{item.SourceEntityId}")
                        .ToArray()));
        }
    }

    private static bool IsStrictlyBefore(
        AcquisitionCandidate earlier,
        AcquisitionCandidate later)
    {
        OccurrenceTime? left = earlier.Time.Occurred;
        OccurrenceTime? right = later.Time.Occurred;
        if (left is null || right is null)
        {
            return false;
        }
        DateTimeOffset leftEnd = left.Kind == OccurrenceTimeKind.Instant
            ? left.UtcStart!.Value
            : left.UtcEnd!.Value;
        DateTimeOffset rightStart = right.UtcStart!.Value;
        return leftEnd < rightStart ||
            left.Kind != OccurrenceTimeKind.Instant && leftEnd == rightStart;
    }

    private static Dictionary<string, PityEvidence> BuildPityEvidence(
        IReadOnlyList<GachaRecord> records,
        bool declaredComplete)
    {
        var results = new Dictionary<string, PityEvidence>(StringComparer.Ordinal);
        foreach (IGrouping<GachaPoolGroup, GachaRecord> group in records
                     .GroupBy(GachaPoolGroupResolver.Resolve))
        {
            GachaRecord[] ordered = group
                .OrderBy(record => record.Time.UtcDateTime)
                .ThenBy(record => record.ExternalRecordId, StringComparer.Ordinal)
                .ToArray();
            int pulls = 0;
            bool hasBoundary = false;
            bool boundaryReliable = false;
            bool sequenceReliable = group.Key != GachaPoolGroup.Unknown;
            DateTimeOffset? previousTime = null;
            foreach (GachaRecord record in ordered)
            {
                if (previousTime == record.Time.ToUniversalTime() ||
                    record.Count != 1)
                {
                    sequenceReliable = false;
                }
                previousTime = record.Time.ToUniversalTime();
                pulls += Math.Max(1, record.Count);
                if (record.RankType != 5)
                {
                    continue;
                }
                results[record.ExternalRecordId] = new PityEvidence(
                    hasBoundary && boundaryReliable && sequenceReliable
                        ? pulls
                        : null,
                    hasBoundary && boundaryReliable && sequenceReliable &&
                        declaredComplete
                        ? pulls
                        : null);
                hasBoundary = true;
                boundaryReliable = sequenceReliable;
                pulls = 0;
                sequenceReliable = group.Key != GachaPoolGroup.Unknown;
                previousTime = null;
            }
        }
        return results;
    }

    private static InventoryItemKind? ParseStableItemKind(string? value) =>
        value?.Trim() switch
        {
            "Character" or "CHARACTER" => InventoryItemKind.Character,
            "Weapon" or "WEAPON" => InventoryItemKind.Weapon,
            _ => null,
        };

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record AcquisitionCandidate(
        string SourceEntityType,
        string SourceEntityId,
        string? PortableEntityKey,
        string? ItemId,
        InventoryItemKind? Kind,
        int? RankType,
        int Count,
        ChronicleTime Time,
        DataOrigin BaseOrigin,
        bool IsGacha,
        PityEvidence? Pity);

    private sealed record OrdinalEvidence(
        int? Ordinal,
        string? Milestone,
        ChronicleEvidence Evidence);

    private sealed record PityEvidence(
        int? LocalRecordedPulls,
        int? ExactPity);
}

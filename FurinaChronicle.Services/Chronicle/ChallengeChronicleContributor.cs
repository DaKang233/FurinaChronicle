// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Chronicle;

public sealed class ChallengeChronicleContributor
{
    public IReadOnlyList<ChronicleEntry> Project(
        Guid gameAccountId,
        IReadOnlyList<ChallengeObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (gameAccountId == Guid.Empty ||
            observations.Any(item => item.GameAccountId != gameAccountId))
        {
            throw new ArgumentException("Challenge observations must belong to the projected account.");
        }

        var result = observations.Select(ObservationEntry).ToList();
        foreach (IGrouping<string, ChallengeObservation> season in observations
                     .Where(item => item.Mode == ChallengeMode.SpiralAbyss)
                     .GroupBy(item => item.SeasonId, StringComparer.Ordinal))
        {
            var candidates = season.Select(item => (Item: item, Target: Target(item)))
                .Where(item => item.Target is not null)
                .OrderBy(item => item.Target!.CompletedAt.UtcStart)
                .ThenBy(item => item.Item.Provenance.Timestamps.ObservedAt)
                .ThenBy(item => item.Item.ObservationId.Value)
                .ToArray();
            if (candidates.Length > 0)
            {
                result.Add(CompletionEntry(candidates[0].Item, candidates[0].Target!));
            }
        }
        return result;
    }

    private static ChronicleEntry ObservationEntry(ChallengeObservation value)
    {
        string id = value.ObservationId.ToString();
        return Entry(value, ChronicleEntryKind.ChallengeObservation, id,
            ChronicleTimeline.Challenge, ChronicleGranularity.Detailed, null,
            new ChronicleTime(null, value.Provenance.Timestamps.ObservedAt,
                value.Provenance.Timestamps.FetchedAt),
            "chronicle.challenge.observation",
            new ChronicleEvidence(ChronicleEvidenceStatus.Known,
                ChronicleEvidenceScope.LocalArchiveRecords,
                "chronicle.evidence.saved-observation",
                [$"challenge-observation:{id}"]),
            value.Provenance.Origin, ChronicleImportance.Normal);
    }

    private static ChronicleEntry CompletionEntry(
        ChallengeObservation value,
        SpiralAbyssChamberResult target)
    {
        string stableId = $"{(int)value.Mode}:{value.SeasonId}";
        return Entry(value, ChronicleEntryKind.ChallengeCompleted, stableId,
            ChronicleTimeline.Main | ChronicleTimeline.Challenge,
            ChronicleGranularity.Default, "challenge-completed",
            new ChronicleTime(target.CompletedAt,
                value.Provenance.Timestamps.ObservedAt,
                value.Provenance.Timestamps.FetchedAt),
            "chronicle.challenge.completed",
            new ChronicleEvidence(ChronicleEvidenceStatus.Known,
                ChronicleEvidenceScope.LocalArchiveRecords,
                ChronicleVersions.SpiralAbyssCompletionRule,
                [$"challenge-observation:{value.ObservationId}:floor-12:chamber-3"]),
            DataOrigin.Derived, ChronicleImportance.Major);
    }

    private static SpiralAbyssChamberResult? Target(ChallengeObservation value) =>
        value.SpiralAbyssFloors.FirstOrDefault(item => item.Floor == 12)?
            .Chambers.FirstOrDefault(item => item.Chamber == 3);

    private static ChronicleEntry Entry(
        ChallengeObservation value,
        ChronicleEntryKind kind,
        string sourceId,
        ChronicleTimeline timelines,
        ChronicleGranularity granularity,
        string? milestone,
        ChronicleTime time,
        string title,
        ChronicleEvidence evidence,
        DataOrigin annotation,
        ChronicleImportance importance) => new(
            ChronicleEntryId.Create(value.GameAccountId, ChronicleDomain.Challenge,
                kind, "challenge", sourceId, ChronicleVersions.Projection),
            null, ChronicleVersions.Projection, value.GameAccountId,
            ChronicleDomain.Challenge, kind, timelines, granularity, milestone,
            time, value.Provenance.Origin, annotation, DataConfidence.Unknown,
            value.Completeness, "challenge", sourceId, title,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["mode"] = value.Mode.ToString(),
                ["season_id"] = value.SeasonId,
            },
            importance, evidence,
            correlationId: $"challenge:{(int)value.Mode}:{value.SeasonId}");
}

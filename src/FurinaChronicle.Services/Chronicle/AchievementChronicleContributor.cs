// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Chronicle;

public sealed record ChronicleAchievementFact(
    Guid GameAccountId,
    string AchievementId,
    OccurrenceTime? UnlockedAt,
    RecordProvenance Provenance);

public sealed record ChronicleAchievementDescriptor(
    string AchievementId,
    bool IsImportant);

public sealed class AchievementChronicleContributor
{
    public IReadOnlyList<ChronicleEntry> Project(
        Guid gameAccountId,
        IReadOnlyList<ChronicleAchievementFact> facts,
        IReadOnlyDictionary<string, ChronicleAchievementDescriptor> catalog)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(catalog);
        if (gameAccountId == Guid.Empty || facts.Any(item => item.GameAccountId != gameAccountId))
        {
            throw new ArgumentException("Achievement facts must belong to the projected account.");
        }

        return facts.Where(item => catalog.TryGetValue(item.AchievementId, out var descriptor) &&
                descriptor.IsImportant)
            .Select(item => new ChronicleEntry(
                ChronicleEntryId.Create(gameAccountId, ChronicleDomain.Achievement,
                    ChronicleEntryKind.AchievementUnlocked, "achievement",
                    item.AchievementId, ChronicleVersions.Projection),
                null, ChronicleVersions.Projection, gameAccountId,
                ChronicleDomain.Achievement, ChronicleEntryKind.AchievementUnlocked,
                ChronicleTimeline.Main, ChronicleGranularity.Default,
                "important-achievement",
                new ChronicleTime(item.UnlockedAt,
                    item.Provenance.Timestamps.ObservedAt,
                    item.Provenance.Timestamps.FetchedAt),
                item.Provenance.Origin, item.Provenance.Origin,
                DataConfidence.Unknown, DataCompleteness.Unknown,
                "achievement", item.AchievementId,
                "chronicle.achievement.unlocked",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["achievement_id"] = item.AchievementId,
                },
                ChronicleImportance.Major,
                new ChronicleEvidence(ChronicleEvidenceStatus.Known,
                    ChronicleEvidenceScope.LocalArchiveRecords,
                    "chronicle.evidence.important-achievement-catalog",
                    [$"achievement:{item.AchievementId}"])))
            .ToArray();
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.History;

public static class UndoRetentionPolicy
{
    public const int DefaultLimit = 3;
    public const int MaximumLimit = 1000;

    public static int ValidateLimit(int value)
    {
        if (value is < 0 or > MaximumLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Undo limit must be between 0 and {MaximumLimit}.");
        }

        return value;
    }

    public static bool ShouldCaptureNewMaterial(
        IEnumerable<int> archiveLimits)
    {
        int[] limits = archiveLimits
            .Select(ValidateLimit)
            .ToArray();
        if (limits.Length == 0)
        {
            throw new ArgumentException(
                "At least one archive limit is required.",
                nameof(archiveLimits));
        }

        return limits.All(limit => limit > 0);
    }

    public static IReadOnlyList<Guid> FindCapacityEvictions(
        Guid archiveId,
        int newLimit,
        IEnumerable<UndoHistoryCandidate> candidates)
    {
        if (archiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "Archive ID cannot be empty.",
                nameof(archiveId));
        }
        ValidateLimit(newLimit);

        // Setting zero only disables future capture and deliberately retains
        // all existing material.
        if (newLimit == 0)
        {
            return [];
        }

        return candidates
            .Where(candidate =>
                candidate.IsEligible &&
                candidate.ArchiveIds.Contains(archiveId))
            .OrderByDescending(candidate => candidate.CommittedAt)
            .ThenByDescending(candidate => candidate.ChangeSetId)
            .Skip(newLimit)
            .Select(candidate => candidate.ChangeSetId)
            .Distinct()
            .ToArray();
    }
}

public sealed class UndoHistoryCandidate
{
    public UndoHistoryCandidate(
        Guid changeSetId,
        DateTimeOffset committedAt,
        IEnumerable<Guid> archiveIds,
        bool isEligible)
    {
        if (changeSetId == Guid.Empty)
        {
            throw new ArgumentException(
                "Change set ID cannot be empty.",
                nameof(changeSetId));
        }

        HashSet<Guid> normalizedArchiveIds = archiveIds.ToHashSet();
        if (normalizedArchiveIds.Count == 0 ||
            normalizedArchiveIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "Undo history must reference valid archives.",
                nameof(archiveIds));
        }

        ChangeSetId = changeSetId;
        CommittedAt = committedAt;
        ArchiveIds = normalizedArchiveIds;
        IsEligible = isEligible;
    }

    public Guid ChangeSetId { get; }

    public DateTimeOffset CommittedAt { get; }

    public IReadOnlySet<Guid> ArchiveIds { get; }

    public bool IsEligible { get; }
}

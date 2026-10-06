// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Core.History;

public enum DataChangeOperationKind
{
    Import = 1,
    Refresh = 2,
    Correction = 3,
    Delete = 4,
    Undo = 5,
    IrreversibleDelete = 6,
}

public enum EntityChangeKind
{
    Insert = 1,
    Update = 2,
    Delete = 3,
}

public enum ChangeExecutionStatus
{
    Applied = 1,
    NoOp = 2,
    Conflict = 3,
    NeedsConfirmation = 4,
    AlreadyCommitted = 5,
    Suppressed = 6,
}

public enum UndoIneligibilityReason
{
    None = 0,
    Disabled = 1,
    CapacityEvicted = 2,
    AlreadyUndone = 3,
    Conflict = 4,
    IrreversibleDeletion = 5,
    MissingMaterial = 6,
    InverseOperation = 7,
}

public readonly record struct FactVersion
{
    public FactVersion(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "Fact version must be positive.");
        }

        Value = value;
    }

    public long Value { get; }

    public FactVersion Next()
    {
        if (Value == long.MaxValue)
        {
            throw new InvalidOperationException(
                "Fact version cannot advance beyond Int64.MaxValue.");
        }

        return new FactVersion(Value + 1);
    }
}

public sealed class EntityChange
{
    public EntityChange(
        string entityKind,
        string entityReference,
        EntityChangeKind changeKind,
        FactVersion? beforeVersion,
        FactVersion? afterVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityReference);
        if (!Enum.IsDefined(changeKind))
        {
            throw new ArgumentOutOfRangeException(nameof(changeKind));
        }
        if (changeKind == EntityChangeKind.Insert && beforeVersion is not null ||
            changeKind == EntityChangeKind.Delete && afterVersion is not null ||
            changeKind == EntityChangeKind.Update &&
            (beforeVersion is null || afterVersion is null))
        {
            throw new ArgumentException(
                "Entity change versions do not match the change kind.");
        }

        EntityKind = entityKind;
        EntityReference = entityReference;
        ChangeKind = changeKind;
        BeforeVersion = beforeVersion;
        AfterVersion = afterVersion;
    }

    public string EntityKind { get; }

    public string EntityReference { get; }

    public EntityChangeKind ChangeKind { get; }

    public FactVersion? BeforeVersion { get; }

    public FactVersion? AfterVersion { get; }
}

public sealed class DataChangeSet
{
    public DataChangeSet(
        Guid changeSetId,
        OperationId operationId,
        DataChangeOperationKind operationKind,
        IEnumerable<Guid> archiveIds,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        DataOrigin origin,
        string summary,
        int affectedRecordCount,
        Guid? undoOfChangeSetId = null)
    {
        if (changeSetId == Guid.Empty)
        {
            throw new ArgumentException(
                "Change set ID cannot be empty.",
                nameof(changeSetId));
        }
        if (!Enum.IsDefined(operationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(operationKind));
        }
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        if (committedAt < startedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(committedAt));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (affectedRecordCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(affectedRecordCount));
        }

        Guid[] normalizedArchiveIds = archiveIds
            .Distinct()
            .ToArray();
        if (normalizedArchiveIds.Length == 0 ||
            normalizedArchiveIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException(
                "A change set must reference at least one valid archive.",
                nameof(archiveIds));
        }
        if (operationKind == DataChangeOperationKind.Undo &&
            undoOfChangeSetId is null)
        {
            throw new ArgumentException(
                "Undo change sets must reference the original change set.",
                nameof(undoOfChangeSetId));
        }

        ChangeSetId = changeSetId;
        OperationId = operationId;
        OperationKind = operationKind;
        ArchiveIds = normalizedArchiveIds;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Origin = origin;
        Summary = summary;
        AffectedRecordCount = affectedRecordCount;
        UndoOfChangeSetId = undoOfChangeSetId;
    }

    public Guid ChangeSetId { get; }

    public OperationId OperationId { get; }

    public DataChangeOperationKind OperationKind { get; }

    public IReadOnlyList<Guid> ArchiveIds { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CommittedAt { get; }

    public DataOrigin Origin { get; }

    public string Summary { get; }

    public int AffectedRecordCount { get; }

    public Guid? UndoOfChangeSetId { get; }

    public bool CanBeUndoCandidate =>
        OperationKind != DataChangeOperationKind.Undo &&
        OperationKind != DataChangeOperationKind.IrreversibleDelete;
}

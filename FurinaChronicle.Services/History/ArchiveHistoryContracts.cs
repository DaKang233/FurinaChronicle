// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;

namespace FurinaChronicle.Services.History;

public sealed record HistoryStorageCapacity(long? AvailableBytes)
{
    public bool IsKnown => AvailableBytes is not null;
}

public interface IHistoryStorageCapacityProvider
{
    HistoryStorageCapacity GetCapacity();
}

public sealed record HistoryCleanupCandidate(
    Guid ChangeSetId,
    IReadOnlyList<Guid> ArchiveIds,
    long MaterialBytes);

public sealed record HistoryCleanupPlan(
    Guid ConfirmationId,
    long RequiredBytes,
    long AvailableBytes,
    long ReclaimableBytes,
    IReadOnlyList<HistoryCleanupCandidate> Candidates);

public sealed record OperationHistoryItem(
    Guid ChangeSetId,
    DataChangeOperationKind OperationKind,
    DateTimeOffset CommittedAt,
    string Summary,
    int AffectedRecordCount,
    bool IsUndoEligible,
    UndoIneligibilityReason IneligibilityReason,
    Guid? UndoneByChangeSetId,
    IReadOnlyList<Guid> ArchiveIds);

public sealed record UndoLimitChangeResult(
    int PreviousLimit,
    int CurrentLimit,
    IReadOnlyList<Guid> EvictedChangeSetIds);

public sealed record ArchiveOperationResult(
    ChangeExecutionStatus Status,
    Guid? ChangeSetId,
    int AffectedRecordCount,
    string? ConflictReason = null,
    ChangeExecutionStatus? OriginalStatus = null,
    HistoryCleanupPlan? CleanupPlan = null);

public sealed class ArchiveUndoRequest
{
    public ArchiveUndoRequest(
        OperationId operationId,
        Guid archiveId,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        string summary = "Undo latest archive change")
    {
        if (archiveId == Guid.Empty)
        {
            throw new ArgumentException(
                "Archive ID cannot be empty.",
                nameof(archiveId));
        }
        if (committedAt < startedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(committedAt));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        OperationId = operationId;
        ArchiveId = archiveId;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Summary = summary;
    }

    public OperationId OperationId { get; }

    public Guid ArchiveId { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CommittedAt { get; }

    public string Summary { get; }
}

public sealed record ArchiveScopePurgeRequest(
    OperationId OperationId,
    Guid ScopeId,
    DateTimeOffset StartedAt,
    DateTimeOffset CommittedAt,
    string Summary);

public interface IArchiveOperationHistoryStore
{
    Task<ArchiveOperationResult> UndoLatestAsync(
        ArchiveUndoRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> PurgeAccountAsync(
        ArchiveScopePurgeRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> PurgeArchiveAsync(
        ArchiveScopePurgeRequest request,
        CancellationToken cancellationToken = default);

    Task<int> GetUndoLimitAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default);

    Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int undoLimit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OperationHistoryItem>> GetHistoryAsync(
        Guid archiveId,
        int count,
        CancellationToken cancellationToken = default);
}

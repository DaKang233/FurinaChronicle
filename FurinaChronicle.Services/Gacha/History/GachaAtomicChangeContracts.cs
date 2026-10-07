// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.Gacha.History;

public sealed record GachaFactState(
    GachaRecord Record,
    FactVersion Version,
    Guid ArchiveId);

public sealed record GachaFactMutation(
    GachaFactReference Reference,
    GachaRecord? ProposedRecord,
    FactVersion? ExpectedVersion = null)
{
    public bool IsDelete => ProposedRecord is null;
}

public sealed class GachaAtomicChangeRequest
{
    public GachaAtomicChangeRequest(
        OperationId operationId,
        DataChangeOperationKind operationKind,
        DataOrigin origin,
        string summary,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        IEnumerable<GachaFactMutation> mutations,
        bool captureUndo = true,
        Guid? undoOfChangeSetId = null,
        Guid? cleanupConfirmationId = null,
        IEnumerable<TombstoneReintroductionConfirmation>?
            reintroductionConfirmations = null)
    {
        if (!Enum.IsDefined(operationKind))
        {
            throw new ArgumentOutOfRangeException(nameof(operationKind));
        }
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (committedAt < startedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(committedAt));
        }

        GachaFactMutation[] normalized = mutations.ToArray();
        if (normalized.Length == 0)
        {
            throw new ArgumentException(
                "At least one Gacha mutation is required.",
                nameof(mutations));
        }
        if (normalized.Select(mutation => mutation.Reference)
            .Distinct()
            .Count() != normalized.Length)
        {
            throw new ArgumentException(
                "A request cannot mutate the same Gacha fact twice.",
                nameof(mutations));
        }
        foreach (GachaFactMutation mutation in normalized)
        {
            if (mutation.ProposedRecord is not null &&
                new GachaFactReference(
                    mutation.ProposedRecord.GameAccountId,
                    mutation.ProposedRecord.ExternalRecordId) !=
                mutation.Reference)
            {
                throw new ArgumentException(
                    "A proposed record must match its stable fact reference.",
                    nameof(mutations));
            }
        }
        if (operationKind == DataChangeOperationKind.Undo &&
            undoOfChangeSetId is null)
        {
            throw new ArgumentException(
                "Undo requests must identify the original change set.",
                nameof(undoOfChangeSetId));
        }
        if (operationKind is DataChangeOperationKind.Undo or
            DataChangeOperationKind.IrreversibleDelete)
        {
            throw new ArgumentException(
                "Undo and irreversible deletion must use their dedicated commands.",
                nameof(operationKind));
        }

        OperationId = operationId;
        OperationKind = operationKind;
        Origin = origin;
        Summary = summary;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Mutations = normalized;
        CaptureUndo = captureUndo;
        UndoOfChangeSetId = undoOfChangeSetId;
        CleanupConfirmationId = cleanupConfirmationId;
        ReintroductionConfirmations =
            reintroductionConfirmations?.ToArray() ?? [];
    }

    public OperationId OperationId { get; }

    public DataChangeOperationKind OperationKind { get; }

    public DataOrigin Origin { get; }

    public string Summary { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CommittedAt { get; }

    public IReadOnlyList<GachaFactMutation> Mutations { get; }

    public bool CaptureUndo { get; }

    public Guid? UndoOfChangeSetId { get; }

    public Guid? CleanupConfirmationId { get; }

    public IReadOnlyList<TombstoneReintroductionConfirmation>
        ReintroductionConfirmations { get; }
}

public sealed record GachaAtomicChangeResult(
    ChangeExecutionStatus Status,
    Guid? ChangeSetId,
    int AffectedRecordCount,
    string? ConflictReason = null,
    ChangeExecutionStatus? OriginalStatus = null,
    HistoryCleanupPlan? CleanupPlan = null,
    TombstoneReintroductionWarning? ReintroductionWarning = null);

public sealed record TombstoneReintroductionConfirmation(
    string TombstoneKey,
    long TombstoneVersion,
    GachaFactReference Reference);

public sealed record TombstoneReintroductionWarning(
    string TombstoneKey,
    long TombstoneVersion,
    GachaFactReference Reference,
    Guid ArchiveId);

public sealed class GachaIrreversibleDeleteRequest
{
    public GachaIrreversibleDeleteRequest(
        OperationId operationId,
        GachaFactReference reference,
        FactVersion expectedVersion,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        string summary = "Irreversibly delete local Gacha fact")
    {
        if (committedAt < startedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(committedAt));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        OperationId = operationId;
        Reference = reference;
        ExpectedVersion = expectedVersion;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Summary = summary;
    }

    public OperationId OperationId { get; }

    public GachaFactReference Reference { get; }

    public FactVersion ExpectedVersion { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset CommittedAt { get; }

    public string Summary { get; }
}

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
    Guid? UndoneByChangeSetId);

public sealed record UndoLimitChangeResult(
    int PreviousLimit,
    int CurrentLimit,
    IReadOnlyList<Guid> EvictedChangeSetIds);

public sealed class GachaUndoRequest
{
    public GachaUndoRequest(
        OperationId operationId,
        Guid archiveId,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        string summary = "Undo latest Gacha change")
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

public interface IGachaAtomicChangeStore
{
    Task<GachaFactState?> GetCurrentAsync(
        GachaFactReference reference,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TombstoneReintroductionWarning>>
        FindActiveTombstonesAsync(
            IReadOnlyCollection<GachaFactReference> references,
            CancellationToken cancellationToken = default);

    Task<GachaAtomicChangeResult> CommitAsync(
        GachaAtomicChangeRequest request,
        CancellationToken cancellationToken = default);

    Task<GachaAtomicChangeResult> UndoLatestAsync(
        GachaUndoRequest request,
        CancellationToken cancellationToken = default);

    Task<GachaAtomicChangeResult> IrreversiblyDeleteAsync(
        GachaIrreversibleDeleteRequest request,
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

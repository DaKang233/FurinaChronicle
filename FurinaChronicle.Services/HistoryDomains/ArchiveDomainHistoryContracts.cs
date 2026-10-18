// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Services.HistoryDomains;

public sealed class ItemObservationCommitRequest
{
    public ItemObservationCommitRequest(
        OperationId operationId,
        DataChangeOperationKind operationKind,
        DataOrigin origin,
        string summary,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        IEnumerable<ItemStateObservation> observations,
        bool captureUndo = true,
        Guid? cleanupConfirmationId = null)
    {
        ValidateOperation(
            operationKind,
            summary,
            startedAt,
            committedAt);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        ItemStateObservation[] normalized = observations.ToArray();
        if (normalized.Length == 0 ||
            normalized.Select(item => item.ObservationId).Distinct().Count() !=
            normalized.Length)
        {
            throw new ArgumentException(
                "At least one unique item observation is required.",
                nameof(observations));
        }

        OperationId = operationId;
        OperationKind = operationKind;
        Origin = origin;
        Summary = summary;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Observations = normalized;
        CaptureUndo = captureUndo;
        CleanupConfirmationId = cleanupConfirmationId;
    }

    public OperationId OperationId { get; }
    public DataChangeOperationKind OperationKind { get; }
    public DataOrigin Origin { get; }
    public string Summary { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CommittedAt { get; }
    public IReadOnlyList<ItemStateObservation> Observations { get; }
    public bool CaptureUndo { get; }
    public Guid? CleanupConfirmationId { get; }

    internal static void ValidateOperation(
        DataChangeOperationKind kind,
        string summary,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt)
    {
        if (!Enum.IsDefined(kind) || kind is DataChangeOperationKind.Undo or
            DataChangeOperationKind.IrreversibleDelete)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        if (committedAt < startedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(committedAt));
        }
    }
}

public sealed record ManualAcquisitionState(
    ManualAcquisition Acquisition,
    FactVersion Version,
    Guid ArchiveId);

public sealed record ManualAcquisitionMutation(
    ManualAcquisitionId AcquisitionId,
    Guid GameAccountId,
    ManualAcquisition? Proposed,
    FactVersion? ExpectedVersion = null)
{
    public bool IsDelete => Proposed is null;
}

public sealed class ManualAcquisitionCommitRequest
{
    public ManualAcquisitionCommitRequest(
        OperationId operationId,
        DataChangeOperationKind operationKind,
        string summary,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        IEnumerable<ManualAcquisitionMutation> mutations,
        bool captureUndo = true,
        Guid? cleanupConfirmationId = null)
    {
        ItemObservationCommitRequest.ValidateOperation(
            operationKind,
            summary,
            startedAt,
            committedAt);
        ManualAcquisitionMutation[] normalized = mutations.ToArray();
        if (normalized.Length == 0 ||
            normalized.Select(item => item.AcquisitionId).Distinct().Count() !=
            normalized.Length)
        {
            throw new ArgumentException(
                "At least one unique manual acquisition mutation is required.",
                nameof(mutations));
        }
        foreach (ManualAcquisitionMutation mutation in normalized)
        {
            if (mutation.GameAccountId == Guid.Empty ||
                mutation.Proposed is not null &&
                (mutation.Proposed.AcquisitionId != mutation.AcquisitionId ||
                 mutation.Proposed.GameAccountId != mutation.GameAccountId))
            {
                throw new ArgumentException(
                    "Manual acquisition mutations must preserve stable identity and account ownership.",
                    nameof(mutations));
            }
        }

        OperationId = operationId;
        OperationKind = operationKind;
        Summary = summary;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Mutations = normalized;
        CaptureUndo = captureUndo;
        CleanupConfirmationId = cleanupConfirmationId;
    }

    public OperationId OperationId { get; }
    public DataChangeOperationKind OperationKind { get; }
    public string Summary { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CommittedAt { get; }
    public IReadOnlyList<ManualAcquisitionMutation> Mutations { get; }
    public bool CaptureUndo { get; }
    public Guid? CleanupConfirmationId { get; }
}

public sealed class ChallengeObservationCommitRequest
{
    public ChallengeObservationCommitRequest(
        OperationId operationId,
        DataChangeOperationKind operationKind,
        DataOrigin origin,
        string summary,
        DateTimeOffset startedAt,
        DateTimeOffset committedAt,
        IEnumerable<ChallengeObservation> observations,
        bool captureUndo = true,
        Guid? cleanupConfirmationId = null)
    {
        ItemObservationCommitRequest.ValidateOperation(
            operationKind,
            summary,
            startedAt,
            committedAt);
        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }
        ChallengeObservation[] normalized = observations.ToArray();
        if (normalized.Length == 0 ||
            normalized.Select(item => item.ObservationId).Distinct().Count() !=
            normalized.Length)
        {
            throw new ArgumentException(
                "At least one unique challenge observation is required.",
                nameof(observations));
        }

        OperationId = operationId;
        OperationKind = operationKind;
        Origin = origin;
        Summary = summary;
        StartedAt = startedAt;
        CommittedAt = committedAt;
        Observations = normalized;
        CaptureUndo = captureUndo;
        CleanupConfirmationId = cleanupConfirmationId;
    }

    public OperationId OperationId { get; }
    public DataChangeOperationKind OperationKind { get; }
    public DataOrigin Origin { get; }
    public string Summary { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset CommittedAt { get; }
    public IReadOnlyList<ChallengeObservation> Observations { get; }
    public bool CaptureUndo { get; }
    public Guid? CleanupConfirmationId { get; }
}

public sealed record DomainRevisionItem(
    Guid RevisionId,
    Guid ChangeSetId,
    string EntityKind,
    string EntityReference,
    EntityChangeKind ChangeKind,
    FactVersion? BeforeVersion,
    FactVersion? AfterVersion,
    string? BeforeSnapshotJson,
    string? AfterSnapshotJson,
    DateTimeOffset CreatedAt);

public interface IArchiveDomainHistoryStore
{
    Task<ArchiveOperationResult> CommitItemObservationsAsync(
        ItemObservationCommitRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> CommitManualAcquisitionsAsync(
        ManualAcquisitionCommitRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveOperationResult> CommitChallengeObservationsAsync(
        ChallengeObservationCommitRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ItemStateObservation>> GetItemObservationsAsync(
        Guid gameAccountId,
        InventoryItemKind? kind = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ManualAcquisitionState>> GetManualAcquisitionsAsync(
        Guid gameAccountId,
        bool includeDeleted = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChallengeObservation>> GetChallengeObservationsAsync(
        Guid gameAccountId,
        ChallengeMode? mode = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DomainRevisionItem>> GetDomainRevisionsAsync(
        Guid changeSetId,
        CancellationToken cancellationToken = default);
}

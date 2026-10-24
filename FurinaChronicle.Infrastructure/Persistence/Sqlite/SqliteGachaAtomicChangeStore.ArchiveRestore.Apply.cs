// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using SQLite;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore : IArchiveAtomicRestorer
{
    public async Task<ArchiveRestoreApplyResult> ApplyAsync(
        ArchiveRestoreApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Package);
        ArgumentNullException.ThrowIfNull(request.Plan);
        if (request.CommittedAt < request.StartedAt)
        {
            throw new ArgumentException(
                "Commit time cannot be earlier than start time.",
                nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Summary);
        if (!request.Plan.CanApply ||
            request.Plan.Mode != ArchiveRestoreMode.Merge ||
            !string.Equals(
                request.Package.PackageFingerprint,
                request.Plan.PackageFingerprint,
                StringComparison.Ordinal) ||
            !ArchiveRestorePlanFingerprint.IsValid(
                request.Package,
                request.Plan))
        {
            return ArchiveRestoreConflict(
                "The Archive package or RestorePlan changed after preview.");
        }

        await database.InitializeAsync(cancellationToken);
        ArchiveRestoreReceipt? previous = await FindReceiptAsync(
            request.OperationId,
            cancellationToken);
        if (previous is not null)
        {
            return ReceiptMatches(previous, request)
                ? new ArchiveRestoreApplyResult(
                    ChangeExecutionStatus.AlreadyCommitted,
                    previous)
                : ArchiveRestoreConflict(
                    "The operation ID is already bound to another Archive restore.");
        }

        IReadOnlyList<MaterializedArchiveFact> materialized =
            await MaterializeArchiveFactsAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        ArchiveRestoreApplyResult? result = null;
        try
        {
            await database.Connection.RunInTransactionAsync(connection =>
            {
                ArchiveRestoreReceipt? committed = ReadArchiveRestoreReceipt(
                    connection,
                    request.OperationId);
                if (committed is not null)
                {
                    result = ReceiptMatches(committed, request)
                        ? new ArchiveRestoreApplyResult(
                            ChangeExecutionStatus.AlreadyCommitted,
                            committed)
                        : ArchiveRestoreConflict(
                            "The operation ID is already bound to another Archive restore.");
                    return;
                }
                if (ReadCommitResult(connection, request.OperationId) is not null)
                {
                    result = ArchiveRestoreConflict(
                        "The operation ID is already used by another local operation.");
                    return;
                }
                if (connection.ExecuteScalar<int>("PRAGMA user_version;") !=
                        request.Plan.TargetSchemaVersion ||
                    request.Plan.TargetSchemaVersion != ArchiveRestoreSchemaVersion)
                {
                    result = ArchiveRestoreConflict(
                        "The target database Schema changed after preview.");
                    return;
                }
                if (!string.Equals(
                        ComputeTransactionStateToken(connection),
                        request.Plan.TargetStateToken,
                        StringComparison.Ordinal))
                {
                    result = ArchiveRestoreConflict(
                        "The target database changed after preview; create a new RestorePlan.");
                    return;
                }

                PreparedArchiveRestore prepared = PrepareArchiveRestore(
                    connection,
                    request,
                    materialized);
                HistoryCleanupPlan? cleanupPlan = BuildCleanupPlan(
                    connection,
                    request.OperationId,
                    prepared.UndoCandidate,
                    prepared.ArchiveIds,
                    prepared.CapacityMutationCount,
                    prepared.SnapshotBytes);
                if (cleanupPlan is not null)
                {
                    if (cleanupPlan.Candidates.Count == 0 ||
                        cleanupPlan.AvailableBytes +
                        cleanupPlan.ReclaimableBytes <
                        cleanupPlan.RequiredBytes)
                    {
                        throw new IOException(
                            "There is not enough storage for the atomic Archive restore, even after eligible undo material cleanup.");
                    }
                    if (request.CleanupConfirmationId !=
                        cleanupPlan.ConfirmationId)
                    {
                        result = new ArchiveRestoreApplyResult(
                            ChangeExecutionStatus.NeedsConfirmation,
                            CleanupPlan: cleanupPlan);
                        return;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (prepared.IsNoOp)
                {
                    InsertArchiveRestoreReceipt(
                        connection,
                        request,
                        Guid.NewGuid(),
                        changeSetId: null,
                        ChangeExecutionStatus.NoOp,
                        affectedRecordCount: 0,
                        factCount: 0);
                    InsertArchiveRestoreCommitResult(
                        connection,
                        request,
                        changeSetId: null,
                        ChangeExecutionStatus.NoOp,
                        affectedRecordCount: 0);
                    result = new ArchiveRestoreApplyResult(
                        ChangeExecutionStatus.NoOp,
                        ReadArchiveRestoreReceipt(
                            connection,
                            request.OperationId));
                    return;
                }
                if (cleanupPlan is not null)
                {
                    EvictChangeSets(
                        connection,
                        cleanupPlan.Candidates.Select(
                            item => item.ChangeSetId));
                }

                WriteArchiveRestoreParents(connection, prepared);
                archiveRestoreFailureInjector.ThrowIfRequested(
                    ArchiveRestoreFailurePoint.AfterParents);
                Guid changeSetId = Guid.NewGuid();
                InsertArchiveRestoreChangeSet(
                    connection,
                    request,
                    changeSetId,
                    prepared.ArchiveIds,
                    prepared.AppliedFactCount);
                WriteArchiveRestoreAliases(
                    connection,
                    changeSetId,
                    prepared.AliasesToInsert);

                long materialBytes = WriteArchiveRestoreFacts(
                    connection,
                    request,
                    prepared,
                    changeSetId);
                InsertSourceRevisions(
                    connection,
                    changeSetId,
                    prepared.SourceRevisions);
                archiveRestoreFailureInjector.ThrowIfRequested(
                    ArchiveRestoreFailurePoint.AfterSourceRevisions);
                ApplyArchiveRestoreTombstones(
                    connection,
                    changeSetId,
                    prepared.Tombstones);
                ApplyArchiveRestoreSettings(connection, request, prepared);
                InvalidateArchiveRestoreChronicle(connection, prepared);

                connection.Execute(
                    """
                    INSERT INTO OperationHistory
                        (ChangeSetId, IsUndoEligible, IneligibilityReason,
                         UndoneByChangeSetId, MaterialBytes)
                    VALUES (?, ?, ?, NULL, ?);
                    """,
                    changeSetId.ToString("D"),
                    prepared.UndoEligible,
                    (int)(prepared.UndoEligible
                        ? UndoIneligibilityReason.None
                        : UndoIneligibilityReason.Disabled),
                    materialBytes);

                archiveRestoreFailureInjector.ThrowIfRequested(
                    ArchiveRestoreFailurePoint.BeforeReceipt);
                InsertArchiveRestoreReceipt(
                    connection,
                    request,
                    Guid.NewGuid(),
                    changeSetId,
                    ChangeExecutionStatus.Applied,
                    prepared.AppliedFactCount,
                    prepared.AppliedFactCount);
                InsertArchiveRestoreCommitResult(
                    connection,
                    request,
                    changeSetId,
                    ChangeExecutionStatus.Applied,
                    prepared.AppliedFactCount);
                archiveRestoreFailureInjector.ThrowIfRequested(
                    ArchiveRestoreFailurePoint.AfterReceipt);
                ApplyCapacityLimits(connection, prepared.ArchiveIds);
                archiveRestoreFailureInjector.ThrowIfRequested(
                    ArchiveRestoreFailurePoint.BeforeCommit);
                result = new ArchiveRestoreApplyResult(
                    ChangeExecutionStatus.Applied,
                    ReadArchiveRestoreReceipt(connection, request.OperationId));
            });
        }
        catch (ArchiveRestoreApplyConflictException exception)
        {
            return ArchiveRestoreConflict(exception.Message);
        }

        return result ?? throw new InvalidOperationException(
            "The Archive restore transaction produced no result.");
    }

    public async Task<ArchiveRestoreReceipt?> FindReceiptAsync(
        OperationId operationId,
        CancellationToken cancellationToken = default)
    {
        await database.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ArchiveRestoreReceipt? result = null;
        await database.Connection.RunInTransactionAsync(connection =>
        {
            result = ReadArchiveRestoreReceipt(connection, operationId);
        });
        return result;
    }

    private static ArchiveRestoreApplyResult ArchiveRestoreConflict(
        string reason) =>
        new(ChangeExecutionStatus.Conflict, ConflictReason: reason);
}

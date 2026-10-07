// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Services.Gacha.Writing;

public sealed record CommitGachaRecordsRequest(
    OperationId OperationId,
    DataChangeOperationKind OperationKind,
    DataOrigin Origin,
    string Summary,
    DateTimeOffset StartedAt,
    DateTimeOffset CommittedAt,
    IReadOnlyCollection<GachaRecord> Records,
    GachaRecordConflictPolicy ConflictPolicy,
    bool SuppressTombstonesAndContinue,
    bool CaptureUndo = true,
    Guid? CleanupConfirmationId = null,
    PlayerArchive? ArchiveToCreate = null,
    IReadOnlyCollection<GameAccount>? AccountsToCreate = null);

public sealed record CommitGachaRecordsResult(
    ChangeExecutionStatus Status,
    Guid? ChangeSetId,
    int InsertedCount,
    int UpdatedCount,
    int DuplicateCount,
    int ConflictCount,
    int SuppressedCount,
    string? ConflictReason = null,
    HistoryCleanupPlan? CleanupPlan = null);

public interface ICommitGachaRecords
{
    Task<CommitGachaRecordsResult> ExecuteAsync(
        CommitGachaRecordsRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class CommitGachaRecords(IGachaAtomicChangeStore store)
    : ICommitGachaRecords
{
    public async Task<CommitGachaRecordsResult> ExecuteAsync(
        CommitGachaRecordsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Records);
        if (request.Records.Count == 0)
        {
            return Empty(ChangeExecutionStatus.NoOp);
        }

        var unique = new Dictionary<GachaFactReference, GachaRecord>();
        IReadOnlyCollection<GameAccount> requestedAccounts =
            request.AccountsToCreate ?? [];
        var proposedAccounts = requestedAccounts.ToDictionary(
            account => account.Id);
        int duplicates = 0;
        foreach (GachaRecord record in request.Records)
        {
            var reference = new GachaFactReference(
                record.GameAccountId,
                record.ExternalRecordId);
            if (!unique.TryAdd(reference, record))
            {
                if (!SameBusinessFact(unique[reference], record))
                {
                    return new CommitGachaRecordsResult(
                        ChangeExecutionStatus.Conflict,
                        ChangeSetId: null,
                        InsertedCount: 0,
                        UpdatedCount: 0,
                        duplicates,
                        ConflictCount: 1,
                        SuppressedCount: 0,
                        $"Source contains different facts for {reference}.");
                }

                duplicates++;
            }
        }

        var mutations = new List<GachaFactMutation>(unique.Count);
        var insertionReferences = new List<GachaFactReference>();
        int inserted = 0;
        int updated = 0;
        int conflicts = 0;
        foreach ((GachaFactReference reference, GachaRecord incoming) in unique)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GachaFactState? current = proposedAccounts.ContainsKey(
                    reference.GameAccountId)
                ? null
                : await store.GetCurrentAsync(
                    reference,
                    cancellationToken);
            if (current is null)
            {
                mutations.Add(new GachaFactMutation(reference, incoming));
                insertionReferences.Add(reference);
                inserted++;
                continue;
            }

            GachaRecord merged;
            if (request.ConflictPolicy ==
                GachaRecordConflictPolicy.PreserveExisting)
            {
                if (HasNonEmptyFactConflict(current.Record, incoming))
                {
                    conflicts++;
                    continue;
                }

                merged = MergePreserving(current.Record, incoming);
            }
            else
            {
                merged = MergeReplacing(current.Record, incoming);
            }

            if (GachaRecordChangeClassifier.Compare(
                    current.Record,
                    merged).Kind == GachaRecordChangeKind.NoOp)
            {
                duplicates++;
                continue;
            }

            mutations.Add(new GachaFactMutation(
                reference,
                merged,
                current.Version));
            updated++;
        }

        if (conflicts > 0)
        {
            return new CommitGachaRecordsResult(
                ChangeExecutionStatus.Conflict,
                ChangeSetId: null,
                InsertedCount: 0,
                UpdatedCount: 0,
                duplicates,
                conflicts,
                SuppressedCount: 0,
                "One or more existing records contain different non-empty facts.");
        }

        IReadOnlyList<TombstoneReintroductionWarning> tombstones =
            await store.FindActiveTombstonesAsync(
                insertionReferences
                    .Where(reference =>
                        !proposedAccounts.ContainsKey(
                            reference.GameAccountId))
                    .ToArray(),
                cancellationToken);
        if (tombstones.Count > 0 &&
            !request.SuppressTombstonesAndContinue)
        {
            return new CommitGachaRecordsResult(
                ChangeExecutionStatus.Suppressed,
                ChangeSetId: null,
                InsertedCount: 0,
                UpdatedCount: 0,
                duplicates,
                ConflictCount: 0,
                tombstones.Count,
                "One or more records were irreversibly deleted on this device.");
        }

        var suppressedReferences = tombstones
            .Select(warning => warning.Reference)
            .ToHashSet();
        mutations.RemoveAll(mutation =>
            suppressedReferences.Contains(mutation.Reference));
        inserted -= suppressedReferences.Count;
        int suppressed = suppressedReferences.Count;

        while (mutations.Count > 0)
        {
            Guid[] referencedAccountIds = mutations
                .Select(mutation => mutation.Reference.GameAccountId)
                .Distinct()
                .ToArray();
            GameAccount[] accountsToCreate = requestedAccounts
                .Where(account => referencedAccountIds.Contains(account.Id))
                .ToArray();
            var atomicRequest = new GachaAtomicChangeRequest(
                request.OperationId,
                request.OperationKind,
                request.Origin,
                request.Summary,
                request.StartedAt,
                request.CommittedAt,
                mutations,
                accountsToCreate.Length == 0 && request.CaptureUndo,
                cleanupConfirmationId: request.CleanupConfirmationId,
                archiveToCreate: accountsToCreate.Length == 0
                    ? null
                    : request.ArchiveToCreate,
                accountsToCreate: accountsToCreate);
            GachaAtomicChangeResult result = await store.CommitAsync(
                atomicRequest,
                cancellationToken);
            if (result.Status != ChangeExecutionStatus.Suppressed ||
                result.ReintroductionWarning is not { } warning ||
                !request.SuppressTombstonesAndContinue)
            {
                return new CommitGachaRecordsResult(
                    result.Status,
                    result.ChangeSetId,
                    inserted,
                    updated,
                    duplicates,
                    ConflictCount: 0,
                    suppressed,
                    result.ConflictReason,
                    result.CleanupPlan);
            }

            int removed = mutations.RemoveAll(mutation =>
                mutation.Reference == warning.Reference);
            if (removed == 0)
            {
                throw new InvalidOperationException(
                    "The atomic store suppressed an unrelated Gacha fact.");
            }

            inserted -= removed;
            suppressed += removed;
        }

        return new CommitGachaRecordsResult(
            ChangeExecutionStatus.NoOp,
            ChangeSetId: null,
            InsertedCount: 0,
            UpdatedCount: 0,
            duplicates,
            ConflictCount: 0,
            suppressed);
    }

    private static CommitGachaRecordsResult Empty(
        ChangeExecutionStatus status) =>
        new(status, null, 0, 0, 0, 0, 0);

    private static GachaRecord MergePreserving(
        GachaRecord current,
        GachaRecord incoming) =>
        current with
        {
            ItemName = PreferNonBlank(current.ItemName, incoming.ItemName),
            ItemId = PreferNonBlank(current.ItemId, incoming.ItemId),
            ItemType = PreferNonBlank(current.ItemType, incoming.ItemType),
            GachaType = PreferNonBlank(current.GachaType, incoming.GachaType),
            UigfGachaType = PreferNonBlank(
                current.UigfGachaType,
                incoming.UigfGachaType),
            RankType = current.RankType ?? incoming.RankType,
        };

    private static GachaRecord MergeReplacing(
        GachaRecord current,
        GachaRecord incoming) =>
        incoming with
        {
            GameAccountId = current.GameAccountId,
            ExternalRecordId = current.ExternalRecordId,
            ItemName = PreferNonBlank(incoming.ItemName, current.ItemName),
            ItemId = PreferNonBlank(incoming.ItemId, current.ItemId),
            ItemType = PreferNonBlank(incoming.ItemType, current.ItemType),
            GachaType = PreferNonBlank(incoming.GachaType, current.GachaType),
            UigfGachaType = PreferNonBlank(
                incoming.UigfGachaType,
                current.UigfGachaType),
            RankType = incoming.RankType ?? current.RankType,
        };

    private static bool HasNonEmptyFactConflict(
        GachaRecord current,
        GachaRecord incoming) =>
        DifferentNonBlank(current.ItemName, incoming.ItemName) ||
        DifferentNonBlank(current.ItemId, incoming.ItemId) ||
        DifferentNonBlank(current.ItemType, incoming.ItemType) ||
        DifferentNonBlank(current.GachaType, incoming.GachaType) ||
        DifferentNonBlank(current.UigfGachaType, incoming.UigfGachaType) ||
        current.RankType is int currentRank &&
            incoming.RankType is int incomingRank &&
            currentRank != incomingRank ||
        current.Count != incoming.Count ||
        current.Time != incoming.Time;

    private static bool SameBusinessFact(
        GachaRecord first,
        GachaRecord second) =>
        !HasNonEmptyFactConflict(first, second) &&
        !HasNonEmptyFactConflict(second, first);

    private static bool DifferentNonBlank(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) &&
        !string.IsNullOrWhiteSpace(second) &&
        !string.Equals(first, second, StringComparison.Ordinal);

    private static string? PreferNonBlank(
        string? preferred,
        string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static async Task PlanDomainAsync(
        IArchiveRestoreStagedPackage source,
        IArchiveRestoreTargetSnapshot target,
        ArchiveRestoreAccountMapping account,
        Guid targetArchiveId,
        Guid targetAccountId,
        bool targetExists,
        ICollection<ArchiveRestoreFactPlan> facts,
        ICollection<ArchiveRestoreTombstoneProbe> probes,
        ICollection<ArchiveRestoreConflict> conflicts,
        CancellationToken cancellationToken)
    {
        await foreach (ItemStateObservation value in source.ReadItemObservationsAsync(
            account.SourceAccountReference,
            cancellationToken))
        {
            ArchiveIndexedFact<ItemStateObservation>? existing = targetExists
                ? await target.FindItemObservationAsync(
                    targetAccountId, value.ObservationId, cancellationToken)
                : null;
            AddComparison(
                account, targetArchiveId, targetAccountId,
                ArchiveRestoreFactKind.ItemObservation,
                value.ObservationId.ToString(), value.ObservationId.ToString(),
                ArchiveRestoreSemanticDigest.ForItemObservation(value),
                existing?.SemanticDigest, facts, probes, conflicts);
        }
        await foreach (ManualAcquisitionState value in source
            .ReadManualAcquisitionsAsync(
                account.SourceAccountReference,
                cancellationToken))
        {
            string stable = value.Acquisition.AcquisitionId.ToString();
            ArchiveIndexedFact<ManualAcquisitionState>? existing = targetExists
                ? await target.FindManualAcquisitionAsync(
                    targetAccountId, value.Acquisition.AcquisitionId,
                    cancellationToken)
                : null;
            AddComparison(
                account, targetArchiveId, targetAccountId,
                ArchiveRestoreFactKind.ManualAcquisition,
                stable, stable,
                ArchiveRestoreSemanticDigest.ForManualAcquisition(value),
                existing?.SemanticDigest, facts, probes, conflicts);
        }
        await foreach (ManualAcquisitionRevisionItem value in source
            .ReadManualAcquisitionRevisionsAsync(
                account.SourceAccountReference,
                cancellationToken))
        {
            string? tombstoneReference = value.After?.AcquisitionId.ToString() ??
                value.Before?.AcquisitionId.ToString();
            if (tombstoneReference is null ||
                value.Before is not null && value.After is not null &&
                value.Before.AcquisitionId != value.After.AcquisitionId)
            {
                conflicts.Add(new ArchiveRestoreConflict(
                    ArchiveRestoreConflictCode.RevisionDifference,
                    "A manual acquisition Revision changes or omits its stable fact identity.",
                    account.SourceArchiveReference,
                    account.SourceAccountReference,
                    ArchiveRestoreFactKind.ManualAcquisitionRevision,
                    value.RevisionId.ToString("D")));
                continue;
            }
            ArchiveIndexedFact<ManualAcquisitionRevisionItem>? existing = targetExists
                ? await target.FindManualAcquisitionRevisionAsync(
                    targetAccountId, value.RevisionId, cancellationToken)
                : null;
            AddComparison(
                account, targetArchiveId, targetAccountId,
                ArchiveRestoreFactKind.ManualAcquisitionRevision,
                value.RevisionId.ToString("D"), tombstoneReference,
                ArchiveRestoreSemanticDigest.ForManualRevision(value),
                existing?.SemanticDigest, facts, probes, conflicts);
        }
        await foreach (ChallengeObservation value in source
            .ReadChallengeObservationsAsync(
                account.SourceAccountReference,
                cancellationToken))
        {
            string stable = value.ObservationId.ToString();
            ArchiveIndexedFact<ChallengeObservation>? existing = targetExists
                ? await target.FindChallengeObservationAsync(
                    targetAccountId, value.ObservationId, cancellationToken)
                : null;
            AddComparison(
                account, targetArchiveId, targetAccountId,
                ArchiveRestoreFactKind.ChallengeObservation,
                stable, stable,
                ArchiveRestoreSemanticDigest.ForChallenge(value),
                existing?.SemanticDigest, facts, probes, conflicts);
        }
    }

    private static void AddComparison(
        ArchiveRestoreAccountMapping account,
        Guid targetArchiveId,
        Guid targetAccountId,
        ArchiveRestoreFactKind kind,
        string stableReference,
        string tombstoneReference,
        string sourceDigest,
        string? targetDigest,
        ICollection<ArchiveRestoreFactPlan> facts,
        ICollection<ArchiveRestoreTombstoneProbe> probes,
        ICollection<ArchiveRestoreConflict> conflicts)
    {
        ArchiveRestoreFactAction action = targetDigest is null
            ? ArchiveRestoreFactAction.Add
            : string.Equals(sourceDigest, targetDigest, StringComparison.Ordinal)
                ? ArchiveRestoreFactAction.Skip
                : ArchiveRestoreFactAction.Conflict;
        facts.Add(new ArchiveRestoreFactPlan(
            account.SourceArchiveReference,
            account.SourceAccountReference,
            targetArchiveId,
            targetAccountId,
            kind,
            stableReference,
            tombstoneReference,
            sourceDigest,
            targetDigest,
            action));
        if (action != ArchiveRestoreFactAction.Skip)
        {
            probes.Add(new ArchiveRestoreTombstoneProbe(
                kind,
                targetArchiveId,
                targetAccountId,
                account.TargetRoleIdentityId,
                tombstoneReference));
        }
        if (action == ArchiveRestoreFactAction.Conflict)
        {
            conflicts.Add(new ArchiveRestoreConflict(
                kind is ArchiveRestoreFactKind.GachaRevision or
                    ArchiveRestoreFactKind.ManualAcquisitionRevision
                    ? ArchiveRestoreConflictCode.RevisionDifference
                    : ArchiveRestoreConflictCode.CurrentFactDifference,
                "The target contains the same stable fact ID with different portable semantics.",
                account.SourceArchiveReference,
                account.SourceAccountReference,
                kind,
                stableReference));
        }
    }
}

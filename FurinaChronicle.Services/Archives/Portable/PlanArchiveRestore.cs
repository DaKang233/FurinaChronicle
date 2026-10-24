// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore(
    IArchiveRestoreTargetSnapshotFactory targetSnapshotFactory,
    TimeProvider timeProvider) : IArchiveRestorePlanner
{
    public async Task<ArchiveRestorePlan> PlanAsync(
        ArchiveRestorePlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Package);
        ValidateDecisions(request);
        await using IArchiveRestoreTargetSnapshot target =
            await targetSnapshotFactory.OpenAsync(cancellationToken);
        var conflicts = new List<ArchiveRestoreConflict>();
        IReadOnlyList<ArchiveRestoreArchiveMapping> archiveMappings =
            PlanArchives(request, target, conflicts);
        IReadOnlyList<ArchiveRestoreAccountMapping> accountMappings =
            PlanAccounts(request, target, archiveMappings, conflicts);
        var facts = new List<ArchiveRestoreFactPlan>();
        var probes = new List<ArchiveRestoreTombstoneProbe>();

        foreach (ArchiveRestoreAccountMapping account in accountMappings.Where(item =>
            item.Kind is ArchiveRestoreAccountMappingKind.MapExisting or
                ArchiveRestoreAccountMappingKind.CreateNew &&
            item.TargetArchiveId is not null &&
            (item.TargetAccountId is not null || item.ProposedAccountId is not null)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Guid targetArchiveId = account.TargetArchiveId!.Value;
            Guid targetAccountId = account.TargetAccountId ??
                account.ProposedAccountId!.Value;
            bool targetExists = account.Kind ==
                ArchiveRestoreAccountMappingKind.MapExisting;
            await PlanGachaAsync(
                request.Package, target, account, targetArchiveId, targetAccountId,
                targetExists, facts, probes, conflicts, cancellationToken);
            await PlanDomainAsync(
                request.Package, target, account, targetArchiveId, targetAccountId,
                targetExists, facts, probes, conflicts, cancellationToken);
        }

        IReadOnlyList<ArchiveRestoreActiveTombstone> activeTombstones =
            await target.FindActiveTombstonesAsync(probes, cancellationToken);
        IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones = BuildTombstoneHits(
            request, facts, activeTombstones);
        facts = ApplyTombstoneActions(facts, tombstones);
        ArchiveRestoreSettingsPlan settings = PlanSettings(
            request, target, archiveMappings);
        ArchiveRestoreUndoAssessment undo = AssessUndo(
            archiveMappings, accountMappings, facts, tombstones, settings,
            request.Package.Summary.IsComplete);
        IReadOnlyList<ArchiveRestoreConfirmationRequirement> confirmations =
            BuildConfirmationRequirements(
                request, archiveMappings, accountMappings, tombstones, settings, undo);
        ArchiveRestorePreview preview = BuildPreview(
            archiveMappings, accountMappings, facts, conflicts, tombstones);
        var resources = new ArchiveRestoreResourceEstimate(
            request.Package.ContainerLength,
            request.Package.StagingLength,
            Math.Max(
                request.Package.StagingLength,
                facts.LongCount(item => item.Action is
                    ArchiveRestoreFactAction.Add or
                    ArchiveRestoreFactAction.Reintroduce) * 512L),
            undo.IsEligibleCandidate
                ? facts.LongCount(item => item.Action == ArchiveRestoreFactAction.Add) *
                    512L
                : 0);
        string fingerprint = ComputePlanFingerprint(
            request, target, archiveMappings, accountMappings, facts, tombstones,
            settings, confirmations, undo, resources);
        return new ArchiveRestorePlan(
            request.Package.PackageFingerprint,
            target.StateToken,
            target.SchemaVersion,
            timeProvider.GetUtcNow(),
            request.Mode,
            archiveMappings,
            accountMappings,
            facts,
            preview,
            conflicts,
            tombstones,
            settings,
            confirmations,
            undo,
            resources,
            fingerprint);
    }

    public async Task<ArchiveRestorePlanFreshness> CheckFreshnessAsync(
        ArchiveRestorePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        await using IArchiveRestoreTargetSnapshot target =
            await targetSnapshotFactory.OpenAsync(cancellationToken);
        return new ArchiveRestorePlanFreshness(
            string.Equals(plan.TargetStateToken, target.StateToken,
                StringComparison.Ordinal) &&
            plan.TargetSchemaVersion == target.SchemaVersion,
            plan.TargetStateToken,
            target.StateToken,
            plan.TargetSchemaVersion,
            target.SchemaVersion);
    }

    private static void ValidateDecisions(ArchiveRestorePlanRequest request)
    {
        if (request.Mode != ArchiveRestoreMode.Merge)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request), "Archive v1 planning supports Merge only.");
        }
        EnsureUnique(
            request.ArchiveDecisions?.Select(item => item.SourceArchiveReference) ?? [],
            "archive decision");
        EnsureUnique(
            request.AccountDecisions?.Select(item => item.SourceAccountReference) ?? [],
            "account decision");
    }

    private static void EnsureUnique(IEnumerable<Guid> values, string label)
    {
        Guid[] ids = values.ToArray();
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException(
                $"Each {label} must have a unique non-empty source ID.");
        }
    }
}

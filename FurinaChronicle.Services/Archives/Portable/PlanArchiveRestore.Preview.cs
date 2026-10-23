// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static ArchiveRestorePreview BuildPreview(
        IReadOnlyList<ArchiveRestoreArchiveMapping> archives,
        IReadOnlyList<ArchiveRestoreAccountMapping> accounts,
        IReadOnlyList<ArchiveRestoreFactPlan> facts,
        IReadOnlyList<ArchiveRestoreConflict> conflicts,
        IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones)
    {
        ArchiveRestoreDatasetPreview[] datasets = Enum
            .GetValues<ArchiveRestoreFactKind>()
            .Select(kind => new ArchiveRestoreDatasetPreview(
                kind,
                facts.LongCount(item => item.Kind == kind && item.Action is
                    ArchiveRestoreFactAction.Add or
                    ArchiveRestoreFactAction.Reintroduce),
                facts.LongCount(item => item.Kind == kind &&
                    item.Action == ArchiveRestoreFactAction.Skip),
                facts.LongCount(item => item.Kind == kind &&
                    item.Action == ArchiveRestoreFactAction.Conflict),
                tombstones.LongCount(item => item.FactKind == kind)))
            .ToArray();
        return new ArchiveRestorePreview(
            archives.Count(item => item.Kind ==
                ArchiveRestoreArchiveMappingKind.CreateNew),
            archives.Count(item => item.Kind ==
                ArchiveRestoreArchiveMappingKind.MapExisting),
            accounts.Count(item => item.Kind ==
                ArchiveRestoreAccountMappingKind.CreateNew),
            accounts.Count(item => item.Kind ==
                ArchiveRestoreAccountMappingKind.MapExisting),
            datasets,
            conflicts.Count,
            tombstones.Count);
    }

    private static string ComputePlanFingerprint(
        ArchiveRestorePlanRequest request,
        IArchiveRestoreTargetSnapshot target,
        IReadOnlyList<ArchiveRestoreArchiveMapping> archives,
        IReadOnlyList<ArchiveRestoreAccountMapping> accounts,
        IReadOnlyList<ArchiveRestoreFactPlan> facts,
        IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones,
        ArchiveRestoreSettingsPlan settings,
        IReadOnlyList<ArchiveRestoreConfirmationRequirement> confirmations,
        ArchiveRestoreUndoAssessment undo,
        ArchiveRestoreResourceEstimate resources)
    {
        return ArchiveRestorePlanFingerprint.Compute(
            request.Package.PackageFingerprint,
            request.Package.Summary.Manifest.FormatVersion,
            target.StateToken,
            target.SchemaVersion,
            request.Mode,
            archives,
            accounts,
            facts,
            tombstones,
            settings,
            confirmations,
            undo,
            resources);
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static IReadOnlyList<ArchiveRestoreTombstoneHit> BuildTombstoneHits(
        ArchiveRestorePlanRequest request,
        IReadOnlyList<ArchiveRestoreFactPlan> facts,
        IReadOnlyList<ArchiveRestoreActiveTombstone> active)
    {
        var result = new List<ArchiveRestoreTombstoneHit>();
        foreach (ArchiveRestoreActiveTombstone marker in active)
        {
            foreach (ArchiveRestoreFactPlan fact in facts.Where(item =>
                item.Kind == marker.Probe.Kind &&
                item.TargetArchiveId == marker.Probe.TargetArchiveId &&
                item.TargetAccountId == marker.Probe.TargetAccountId &&
                string.Equals(
                    item.TombstoneReference,
                    marker.Probe.StableReference,
                    StringComparison.Ordinal)))
            {
                string reference = ConfirmationReference(fact);
                bool confirmed = HasConfirmation(
                    request,
                    ArchiveRestoreConfirmationKind.TombstoneReintroduction,
                    reference,
                    marker.TombstoneKey,
                    marker.TombstoneVersion);
                result.Add(new ArchiveRestoreTombstoneHit(
                    fact.SourceArchiveReference,
                    fact.SourceAccountReference,
                    fact.Kind,
                    fact.StableReference,
                    marker.TombstoneKey,
                    marker.TombstoneVersion,
                    confirmed));
            }
        }
        return result
            .Distinct()
            .OrderBy(item => item.SourceArchiveReference)
            .ThenBy(item => item.SourceAccountReference)
            .ThenBy(item => item.FactKind)
            .ThenBy(item => item.StableReference, StringComparer.Ordinal)
            .ToArray();
    }

    private static List<ArchiveRestoreFactPlan> ApplyTombstoneActions(
        IReadOnlyList<ArchiveRestoreFactPlan> facts,
        IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones)
    {
        HashSet<(Guid, Guid, ArchiveRestoreFactKind, string)> hits = tombstones
            .Select(item => (
                item.SourceArchiveReference,
                item.SourceAccountReference,
                item.FactKind,
                item.StableReference))
            .ToHashSet();
        return facts.Select(item =>
            item.Action == ArchiveRestoreFactAction.Add && hits.Contains((
                item.SourceArchiveReference,
                item.SourceAccountReference,
                item.Kind,
                item.StableReference))
                ? item with { Action = ArchiveRestoreFactAction.Reintroduce }
                : item).ToList();
    }

    private static ArchiveRestoreSettingsPlan PlanSettings(
        ArchiveRestorePlanRequest request,
        IArchiveRestoreTargetSnapshot target,
        IReadOnlyList<ArchiveRestoreArchiveMapping> archives)
    {
        var differences = new List<ArchiveRestoreSettingDifference>();
        if (request.Package.Settings.RequireUniqueArchiveNames !=
            target.Settings.RequireUniqueArchiveNames)
        {
            differences.Add(new ArchiveRestoreSettingDifference(
                "RequireUniqueArchiveNames",
                null,
                request.Package.Settings.RequireUniqueArchiveNames.ToString(),
                target.Settings.RequireUniqueArchiveNames.ToString(),
                request.ApplyPortableSettings));
        }
        IReadOnlyDictionary<Guid, ArchivePortableArchiveSetting> sourceSettings =
            request.Package.Settings.Archives.ToDictionary(
                item => item.ArchiveReference);
        IReadOnlyDictionary<Guid, ArchivePortableArchiveSetting> targetSettings =
            target.Settings.Archives.ToDictionary(item => item.ArchiveReference);
        foreach (ArchiveRestoreArchiveMapping mapping in archives.Where(item =>
            item.Kind != ArchiveRestoreArchiveMappingKind.RequiresSelection))
        {
            int sourceLimit = sourceSettings[mapping.SourceArchiveReference].UndoLimit;
            int? targetLimit = mapping.TargetArchiveId is Guid targetArchiveId &&
                targetSettings.TryGetValue(targetArchiveId, out var targetSetting)
                ? targetSetting.UndoLimit
                : null;
            if (targetLimit != sourceLimit)
            {
                differences.Add(new ArchiveRestoreSettingDifference(
                    "UndoLimit",
                    mapping.SourceArchiveReference,
                    sourceLimit.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    targetLimit?.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    targetLimit is null || request.ApplyPortableSettings));
            }
        }
        return new ArchiveRestoreSettingsPlan(
            request.ApplyPortableSettings,
            differences);
    }

    private static ArchiveRestoreUndoAssessment AssessUndo(
        IReadOnlyList<ArchiveRestoreArchiveMapping> archives,
        IReadOnlyList<ArchiveRestoreAccountMapping> accounts,
        IReadOnlyList<ArchiveRestoreFactPlan> facts,
        IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones,
        ArchiveRestoreSettingsPlan settings,
        bool completePackage)
    {
        var reasons = new List<string>();
        if (archives.Any(item => item.Kind !=
                ArchiveRestoreArchiveMappingKind.MapExisting) ||
            accounts.Any(item => item.Kind !=
                ArchiveRestoreAccountMappingKind.MapExisting))
            reasons.Add("Parent objects would be created or remain unresolved.");
        if (accounts.Any(item => item.IdentityKind ==
            ArchiveRestoreIdentityMappingKind.AliasSourceToTarget))
            reasons.Add("At least one source identity is mapped as an alias.");
        if (facts.Any(item => item.Kind is ArchiveRestoreFactKind.GachaRevision or
            ArchiveRestoreFactKind.ManualAcquisitionRevision))
            reasons.Add("The source contains retained Revision history.");
        if (facts.Any(item => item.Action == ArchiveRestoreFactAction.Conflict))
            reasons.Add("The plan contains fact conflicts.");
        if (tombstones.Count > 0)
            reasons.Add("The plan would explicitly reintroduce a Tombstone fact.");
        if (settings.Differences.Any(item => item.WillApply))
            reasons.Add("The plan would change portable settings.");
        if (!completePackage)
            reasons.Add("The source has unsupported optional datasets.");
        return new ArchiveRestoreUndoAssessment(reasons.Count == 0, reasons);
    }

    private static IReadOnlyList<ArchiveRestoreConfirmationRequirement>
        BuildConfirmationRequirements(
            ArchiveRestorePlanRequest request,
            IReadOnlyList<ArchiveRestoreArchiveMapping> archives,
            IReadOnlyList<ArchiveRestoreAccountMapping> accounts,
            IReadOnlyList<ArchiveRestoreTombstoneHit> tombstones,
            ArchiveRestoreSettingsPlan settings,
            ArchiveRestoreUndoAssessment undo)
    {
        var result = new List<ArchiveRestoreConfirmationRequirement>();
        if (!request.Package.Summary.IsComplete)
        {
            Add(ArchiveRestoreConfirmationKind.PartialOptionalDatasets,
                request.Package.PackageFingerprint,
                "The Archive contains unsupported optional datasets; restoration is partial.");
        }
        foreach (ArchiveRestoreArchiveMapping archive in archives.Where(item =>
            item.Candidates.Any(candidate => !candidate.IsExactMatch) &&
            item.Kind != ArchiveRestoreArchiveMappingKind.RequiresSelection))
        {
            Add(ArchiveRestoreConfirmationKind.SimilarArchiveName,
                archive.SourceArchiveReference.ToString("D"),
                "A similar target Archive name exists and the explicit mapping must be confirmed.");
        }
        foreach (ArchiveRestoreAccountMapping account in accounts.Where(item =>
            item.IdentityKind == ArchiveRestoreIdentityMappingKind.Unresolved &&
            item.Kind != ArchiveRestoreAccountMappingKind.RequiresSelection))
        {
            Add(ArchiveRestoreConfirmationKind.UnresolvedAccountMapping,
                account.SourceAccountReference.ToString("D"),
                "An Unresolved account mapping must be confirmed explicitly.");
        }
        foreach (ArchiveRestoreTombstoneHit tombstone in tombstones)
        {
            string reference = $"{tombstone.SourceArchiveReference:D}:" +
                $"{tombstone.SourceAccountReference:D}:{(int)tombstone.FactKind}:" +
                tombstone.StableReference;
            result.Add(new ArchiveRestoreConfirmationRequirement(
                ArchiveRestoreConfirmationKind.TombstoneReintroduction,
                reference,
                "A locally and irreversibly deleted fact would be reintroduced.",
                tombstone.Confirmed,
                tombstone.TombstoneKey,
                tombstone.TombstoneVersion));
        }
        if (settings.Differences.Any(item => item.WillApply &&
            item.TargetValue is not null))
        {
            Add(ArchiveRestoreConfirmationKind.ApplyPortableSettings,
                request.Package.PackageFingerprint,
                "Existing target portable settings would change.");
        }
        if (!undo.IsEligibleCandidate)
        {
            Add(ArchiveRestoreConfirmationKind.NonUndoableOperation,
                request.Package.PackageFingerprint,
                "This restore plan is not eligible for ordinary Undo.");
        }
        return result;

        void Add(
            ArchiveRestoreConfirmationKind kind,
            string reference,
            string message)
        {
            result.Add(new ArchiveRestoreConfirmationRequirement(
                kind,
                reference,
                message,
                HasConfirmation(request, kind, reference)));
        }
    }

    private static bool HasConfirmation(
        ArchiveRestorePlanRequest request,
        ArchiveRestoreConfirmationKind kind,
        string reference,
        string? markerKey = null,
        long? markerVersion = null) =>
        (request.Confirmations ?? []).Any(item =>
            item.Kind == kind &&
            string.Equals(item.Reference, reference, StringComparison.Ordinal) &&
            (markerKey is null || string.Equals(
                item.MarkerKey, markerKey, StringComparison.Ordinal)) &&
            (markerVersion is null || item.MarkerVersion == markerVersion));

    private static string ConfirmationReference(ArchiveRestoreFactPlan fact) =>
        $"{fact.SourceArchiveReference:D}:{fact.SourceAccountReference:D}:" +
        $"{(int)fact.Kind}:{fact.StableReference}";
}

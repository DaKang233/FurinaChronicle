// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text.Json;

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
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.Package.PackageFingerprint,
            request.Package.Summary.Manifest.FormatVersion,
            target.StateToken,
            target.SchemaVersion,
            request.Mode,
            Archives = archives.OrderBy(item => item.SourceArchiveReference),
            Accounts = accounts.OrderBy(item => item.SourceAccountReference),
            Facts = facts.OrderBy(item => item.SourceArchiveReference)
                .ThenBy(item => item.SourceAccountReference)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.StableReference, StringComparer.Ordinal),
            Tombstones = tombstones.OrderBy(
                item => item.TombstoneKey,
                StringComparer.Ordinal),
            settings,
            Confirmations = confirmations.OrderBy(item => item.Kind)
                .ThenBy(item => item.Reference, StringComparer.Ordinal),
            undo,
            resources,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}

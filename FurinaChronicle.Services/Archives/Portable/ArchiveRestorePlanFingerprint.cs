// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text.Json;

namespace FurinaChronicle.Services.Archives.Portable;

public static class ArchiveRestorePlanFingerprint
{
    public static bool IsValid(
        IArchiveRestoreStagedPackage package,
        ArchiveRestorePlan plan)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(plan);
        return string.Equals(
            plan.PlanFingerprint,
            Compute(
                package.PackageFingerprint,
                package.Summary.Manifest.FormatVersion,
                plan.TargetStateToken,
                plan.TargetSchemaVersion,
                plan.Mode,
                plan.Archives,
                plan.Accounts,
                plan.Facts,
                plan.Tombstones,
                plan.Settings,
                plan.Confirmations,
                plan.Undo,
                plan.Resources),
            StringComparison.Ordinal);
    }

    internal static string Compute(
        string packageFingerprint,
        string formatVersion,
        string targetStateToken,
        int targetSchemaVersion,
        ArchiveRestoreMode mode,
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
            PackageFingerprint = packageFingerprint,
            FormatVersion = formatVersion,
            TargetStateToken = targetStateToken,
            TargetSchemaVersion = targetSchemaVersion,
            Mode = mode,
            Archives = archives.OrderBy(item => item.SourceArchiveReference),
            Accounts = accounts.OrderBy(item => item.SourceAccountReference),
            Facts = facts.OrderBy(item => item.SourceArchiveReference)
                .ThenBy(item => item.SourceAccountReference)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.StableReference, StringComparer.Ordinal),
            Tombstones = tombstones.OrderBy(
                item => item.TombstoneKey,
                StringComparer.Ordinal),
            Settings = settings,
            Confirmations = confirmations.OrderBy(item => item.Kind)
                .ThenBy(item => item.Reference, StringComparer.Ordinal),
            Undo = undo,
            Resources = resources,
        }, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        });
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }
}

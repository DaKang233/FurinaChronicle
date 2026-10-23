// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static IReadOnlyList<ArchiveRestoreAccountMapping> PlanAccounts(
        ArchiveRestorePlanRequest request,
        IArchiveRestoreTargetSnapshot target,
        IReadOnlyList<ArchiveRestoreArchiveMapping> archives,
        ICollection<ArchiveRestoreConflict> conflicts)
    {
        var decisions = (request.AccountDecisions ?? [])
            .ToDictionary(item => item.SourceAccountReference);
        IReadOnlyDictionary<Guid, ArchiveCatalogRoleIdentity> sourceRoles =
            request.Package.Catalog.RoleIdentities.ToDictionary(
                item => item.RoleIdentityReference);
        IReadOnlyDictionary<Guid, ArchiveCatalogRoleIdentity> targetRoles =
            target.Catalog.RoleIdentities.ToDictionary(
                item => item.RoleIdentityReference);
        var result = new List<ArchiveRestoreAccountMapping>();
        foreach (ArchiveRestoreArchiveMapping archive in archives)
        {
            ArchiveCatalogArchive sourceArchive = request.Package.Catalog.Archives
                .Single(item => item.ArchiveReference ==
                    archive.SourceArchiveReference);
            HashSet<string> duplicateNaturalKeys = sourceArchive.Accounts
                .Where(item => item.RoleIdentityReference is not null)
                .Select(item => sourceRoles[item.RoleIdentityReference!.Value]
                    .NaturalIdentity.ToDeterministicName())
                .GroupBy(item => item, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            ArchiveCatalogArchive? targetArchive = archive.TargetArchiveId is Guid targetId
                ? target.Catalog.Archives.SingleOrDefault(item =>
                    item.ArchiveReference == targetId)
                : null;
            foreach (ArchiveCatalogAccount sourceAccount in sourceArchive.Accounts
                .OrderBy(item => item.AccountReference))
            {
                if (archive.Kind == ArchiveRestoreArchiveMappingKind.RequiresSelection)
                {
                    result.Add(RequiresAccount(sourceArchive, sourceAccount, null));
                    continue;
                }
                Guid targetArchiveId = archive.TargetArchiveId ??
                    archive.ProposedArchiveId!.Value;
                ArchiveCatalogRoleIdentity? sourceRole =
                    sourceAccount.RoleIdentityReference is Guid roleReference
                        ? sourceRoles[roleReference]
                        : null;
                string? naturalKey = sourceRole?.NaturalIdentity.ToDeterministicName();
                if (naturalKey is not null && duplicateNaturalKeys.Contains(naturalKey))
                {
                    conflicts.Add(new ArchiveRestoreConflict(
                        ArchiveRestoreConflictCode.DuplicateSourceIdentity,
                        "A source Archive contains more than one account with the same natural identity.",
                        sourceArchive.ArchiveReference,
                        sourceAccount.AccountReference));
                    result.Add(CollisionAccount(
                        sourceArchive, sourceAccount, targetArchiveId, sourceRole,
                        "Duplicate source natural identity."));
                    continue;
                }
                if (sourceRole is not null &&
                    targetRoles.TryGetValue(
                        sourceRole.RoleIdentityReference,
                        out ArchiveCatalogRoleIdentity? sameIdRole) &&
                    sameIdRole.NaturalIdentity != sourceRole.NaturalIdentity)
                {
                    conflicts.Add(new ArchiveRestoreConflict(
                        ArchiveRestoreConflictCode.IdentityCollision,
                        "The source shared identity GUID maps to a different target natural identity.",
                        sourceArchive.ArchiveReference,
                        sourceAccount.AccountReference));
                    result.Add(CollisionAccount(
                        sourceArchive, sourceAccount, targetArchiveId, sourceRole,
                        "Shared identity GUID collision."));
                    continue;
                }
                ArchiveCatalogRoleIdentity[] sharedIdentityMatches = sourceRole is null
                    ? []
                    : targetRoles.Values.Where(item =>
                        item.NaturalIdentity == sourceRole.NaturalIdentity).ToArray();
                if (sharedIdentityMatches.Length > 1)
                {
                    conflicts.Add(new ArchiveRestoreConflict(
                        ArchiveRestoreConflictCode.IdentityCollision,
                        "The target contains multiple shared identity GUIDs for the same natural identity.",
                        sourceArchive.ArchiveReference,
                        sourceAccount.AccountReference));
                    result.Add(CollisionAccount(
                        sourceArchive, sourceAccount, targetArchiveId, sourceRole,
                        "Multiple target shared identities match the natural identity."));
                    continue;
                }
                ArchiveCatalogAccount[] naturalMatches = sourceRole is null ||
                    targetArchive is null
                    ? []
                    : targetArchive.Accounts.Where(candidate =>
                    {
                        if (candidate.RoleIdentityReference is not Guid roleId ||
                            !targetRoles.TryGetValue(roleId, out var candidateRole))
                        {
                            return false;
                        }
                        return candidateRole.NaturalIdentity == sourceRole.NaturalIdentity;
                    }).ToArray();
                if (decisions.TryGetValue(sourceAccount.AccountReference, out var decision))
                {
                    if (decision.Kind == ArchiveRestoreAccountDecisionKind.MapExisting)
                    {
                        ArchiveCatalogAccount? selected = targetArchive?.Accounts
                            .SingleOrDefault(item => item.AccountReference ==
                                decision.TargetAccountId);
                        if (selected is null ||
                            !AccountCompatible(sourceRole, selected, targetRoles))
                        {
                            conflicts.Add(new ArchiveRestoreConflict(
                                ArchiveRestoreConflictCode.AccountMapping,
                                "The selected target account does not exist in the mapped Archive or has a different natural identity.",
                                sourceArchive.ArchiveReference,
                                sourceAccount.AccountReference));
                            result.Add(CollisionAccount(
                                sourceArchive, sourceAccount, targetArchiveId, sourceRole,
                                "Explicit target account is incompatible."));
                            continue;
                        }
                        result.Add(MapExistingAccount(
                            sourceArchive, sourceAccount, targetArchiveId, selected,
                            sourceRole));
                        continue;
                    }
                    result.Add(CreateAccount(
                        request.Package.PackageFingerprint,
                        sourceArchive,
                        sourceAccount,
                        targetArchiveId,
                        sourceRole,
                        targetRoles));
                    continue;
                }
                if (sourceRole is null)
                {
                    result.Add(archive.Kind == ArchiveRestoreArchiveMappingKind.CreateNew
                        ? CreateAccount(
                            request.Package.PackageFingerprint,
                            sourceArchive,
                            sourceAccount,
                            targetArchiveId,
                            null,
                            targetRoles)
                        : RequiresAccount(sourceArchive, sourceAccount, targetArchiveId));
                }
                else if (naturalMatches.Length == 1)
                {
                    result.Add(MapExistingAccount(
                        sourceArchive, sourceAccount, targetArchiveId,
                        naturalMatches[0], sourceRole));
                }
                else if (naturalMatches.Length > 1)
                {
                    conflicts.Add(new ArchiveRestoreConflict(
                        ArchiveRestoreConflictCode.AccountMapping,
                        "The target Archive contains multiple accounts with the same natural identity.",
                        sourceArchive.ArchiveReference,
                        sourceAccount.AccountReference));
                    result.Add(CollisionAccount(
                        sourceArchive, sourceAccount, targetArchiveId, sourceRole,
                        "Multiple target accounts share the natural identity."));
                }
                else
                {
                    result.Add(CreateAccount(
                        request.Package.PackageFingerprint,
                        sourceArchive,
                        sourceAccount,
                        targetArchiveId,
                        sourceRole,
                        targetRoles));
                }
            }
        }
        return result;
    }

    private static bool AccountCompatible(
        ArchiveCatalogRoleIdentity? sourceRole,
        ArchiveCatalogAccount targetAccount,
        IReadOnlyDictionary<Guid, ArchiveCatalogRoleIdentity> targetRoles)
    {
        if (sourceRole is null) return targetAccount.RoleIdentityReference is null;
        return targetAccount.RoleIdentityReference is Guid targetRoleId &&
            targetRoles.TryGetValue(targetRoleId, out var targetRole) &&
            targetRole.NaturalIdentity == sourceRole.NaturalIdentity;
    }

    private static ArchiveRestoreAccountMapping MapExistingAccount(
        ArchiveCatalogArchive sourceArchive,
        ArchiveCatalogAccount sourceAccount,
        Guid targetArchiveId,
        ArchiveCatalogAccount targetAccount,
        ArchiveCatalogRoleIdentity? sourceRole)
    {
        Guid? targetRoleId = targetAccount.RoleIdentityReference;
        return new ArchiveRestoreAccountMapping(
            sourceArchive.ArchiveReference,
            sourceAccount.AccountReference,
            ArchiveRestoreAccountMappingKind.MapExisting,
            targetArchiveId,
            targetAccount.AccountReference,
            null,
            sourceRole?.RoleIdentityReference,
            targetRoleId,
            sourceRole?.NaturalIdentity,
            sourceRole is null
                ? ArchiveRestoreIdentityMappingKind.Unresolved
                : sourceRole.RoleIdentityReference == targetRoleId
                    ? ArchiveRestoreIdentityMappingKind.Reuse
                    : ArchiveRestoreIdentityMappingKind.AliasSourceToTarget,
            null);
    }

    private static ArchiveRestoreAccountMapping CreateAccount(
        string packageFingerprint,
        ArchiveCatalogArchive sourceArchive,
        ArchiveCatalogAccount sourceAccount,
        Guid targetArchiveId,
        ArchiveCatalogRoleIdentity? sourceRole,
        IReadOnlyDictionary<Guid, ArchiveCatalogRoleIdentity> targetRoles)
    {
        ArchiveCatalogRoleIdentity[] naturalTargets = sourceRole is null
            ? []
            : targetRoles.Values.Where(item =>
                item.NaturalIdentity == sourceRole.NaturalIdentity).ToArray();
        ArchiveCatalogRoleIdentity? naturalTarget = naturalTargets.Length == 1
            ? naturalTargets[0]
            : null;
        Guid? targetRoleId = sourceRole is null
            ? null
            : naturalTarget?.RoleIdentityReference ?? sourceRole.RoleIdentityReference;
        ArchiveRestoreIdentityMappingKind identityKind = sourceRole is null
            ? ArchiveRestoreIdentityMappingKind.Unresolved
            : naturalTarget is null
                ? ArchiveRestoreIdentityMappingKind.PreserveSource
                : naturalTarget.RoleIdentityReference == sourceRole.RoleIdentityReference
                    ? ArchiveRestoreIdentityMappingKind.Reuse
                    : ArchiveRestoreIdentityMappingKind.AliasSourceToTarget;
        return new ArchiveRestoreAccountMapping(
            sourceArchive.ArchiveReference,
            sourceAccount.AccountReference,
            ArchiveRestoreAccountMappingKind.CreateNew,
            targetArchiveId,
            null,
            DeterministicGuid(
                packageFingerprint,
                $"account:{targetArchiveId:D}",
                sourceAccount.AccountReference),
            sourceRole?.RoleIdentityReference,
            targetRoleId,
            sourceRole?.NaturalIdentity,
            identityKind,
            null);
    }

    private static ArchiveRestoreAccountMapping RequiresAccount(
        ArchiveCatalogArchive sourceArchive,
        ArchiveCatalogAccount sourceAccount,
        Guid? targetArchiveId) => new(
        sourceArchive.ArchiveReference,
        sourceAccount.AccountReference,
        ArchiveRestoreAccountMappingKind.RequiresSelection,
        targetArchiveId,
        null,
        null,
        sourceAccount.RoleIdentityReference,
        null,
        null,
        ArchiveRestoreIdentityMappingKind.Unresolved,
        "The account requires an explicit mapping decision.");

    private static ArchiveRestoreAccountMapping CollisionAccount(
        ArchiveCatalogArchive sourceArchive,
        ArchiveCatalogAccount sourceAccount,
        Guid targetArchiveId,
        ArchiveCatalogRoleIdentity? sourceRole,
        string diagnostic) => new(
        sourceArchive.ArchiveReference,
        sourceAccount.AccountReference,
        ArchiveRestoreAccountMappingKind.Collision,
        targetArchiveId,
        null,
        null,
        sourceRole?.RoleIdentityReference,
        null,
        sourceRole?.NaturalIdentity,
        ArchiveRestoreIdentityMappingKind.Collision,
        diagnostic);
}

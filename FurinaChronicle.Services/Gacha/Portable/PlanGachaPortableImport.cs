// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Gacha.Portable;

public sealed class PlanGachaPortableImport(
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository,
    IGachaRecordRepository recordRepository,
    TimeProvider timeProvider)
{
    private const int RecordLookupBatchSize = 500;

    public async Task<GachaPortableImportPlan> ExecuteAsync(
        GachaPortableImportPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Package);
        if (request.TargetArchiveId is not null && request.CreateNewArchive)
        {
            throw new ArgumentException(
                "An existing target archive and a new archive cannot both be selected.",
                nameof(request));
        }

        IReadOnlyList<PlayerArchive> archives =
            await archiveRepository.GetAllAsync(cancellationToken);
        GachaPortableArchivePlan archivePlan = ResolveArchivePlan(
            request,
            archives);
        var importContext = new GachaPortableImportContext(
            DataOrigin.FurinaImport,
            timeProvider.GetUtcNow(),
            Guid.NewGuid());
        if (archivePlan.Kind == GachaPortableArchivePlanKind.RequiresSelection)
        {
            return new GachaPortableImportPlan(
                archivePlan,
                [],
                importContext,
                new GachaPortableImportPreview(0, 0, 0, 0, 0, 0));
        }

        var accountPlans = new List<GachaPortableAccountImportPlan>(
            request.Package.Accounts.Count);
        foreach (GachaPortableAccount sourceAccount in request.Package.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            accountPlans.Add(await PlanAccountAsync(
                archivePlan,
                sourceAccount,
                cancellationToken));
        }

        return new GachaPortableImportPlan(
            archivePlan,
            accountPlans,
            importContext,
            new GachaPortableImportPreview(
                accountPlans.Count(plan => plan.TargetAccountId is null),
                accountPlans.Count(plan => plan.TargetAccountId is not null),
                accountPlans.Sum(plan => plan.AddCount),
                accountPlans.Sum(plan => plan.SkipCount),
                accountPlans.Sum(plan => plan.ConflictCount),
                accountPlans.Count(plan =>
                    plan.Identity.Kind ==
                        GachaPortableIdentityPlanKind.AliasSourceToTarget)));
    }

    private async Task<GachaPortableAccountImportPlan> PlanAccountAsync(
        GachaPortableArchivePlan archivePlan,
        GachaPortableAccount sourceAccount,
        CancellationToken cancellationToken)
    {
        GameRoleIdentity sourceIdentity = sourceAccount.RoleIdentity;
        GameRoleIdentity? identityBySourceId =
            await accountRepository.GetRoleIdentityByIdAsync(
                sourceIdentity.Id,
                cancellationToken);
        if (identityBySourceId is not null &&
            identityBySourceId.NaturalIdentity != sourceIdentity.NaturalIdentity)
        {
            return CreateIdentityCollisionPlan(
                sourceAccount,
                identityBySourceId);
        }

        GameAccount? targetAccount = null;
        if (archivePlan.TargetArchiveId is Guid targetArchiveId)
        {
            targetAccount =
                await accountRepository.GetByArchiveIdAndNaturalIdentityAsync(
                    targetArchiveId,
                    sourceIdentity.NaturalIdentity,
                    cancellationToken);
        }

        GameRoleIdentity? targetIdentity = targetAccount?.RoleIdentity ??
            await accountRepository.GetRoleIdentityByNaturalIdentityAsync(
                sourceIdentity.NaturalIdentity,
                cancellationToken);
        GachaPortableIdentityPlan identityPlan = ResolveIdentityPlan(
            sourceIdentity,
            targetIdentity);
        if (targetAccount is null)
        {
            return new GachaPortableAccountImportPlan(
                sourceAccount.AccountReference,
                TargetAccountId: null,
                ProposedAccountId: Guid.NewGuid(),
                sourceIdentity.NaturalIdentity,
                identityPlan,
                AddCount: sourceAccount.Records.Count,
                SkipCount: 0,
                ConflictCount: 0,
                Conflicts: [],
                CountOrigins(sourceAccount.Records),
                TargetAccountUpdatedAt: null,
                TargetRecordCountAtPlan: null);
        }

        var conflicts = new List<GachaPortableRecordConflict>();
        int addCount = 0;
        int skipCount = 0;
        for (int offset = 0;
            offset < sourceAccount.Records.Count;
            offset += RecordLookupBatchSize)
        {
            GachaRecord[] sourceBatch = sourceAccount.Records
                .Skip(offset)
                .Take(RecordLookupBatchSize)
                .ToArray();
            IReadOnlyList<GachaRecord> targetRecords =
                await recordRepository.GetByExternalRecordIdsAsync(
                    targetAccount.Id,
                    sourceBatch
                        .Select(record => record.ExternalRecordId)
                        .ToArray(),
                    cancellationToken);
            Dictionary<string, GachaRecord> targetById = targetRecords
                .ToDictionary(
                    record => record.ExternalRecordId,
                    StringComparer.Ordinal);
            foreach (GachaRecord sourceRecord in sourceBatch)
            {
                if (!targetById.TryGetValue(
                        sourceRecord.ExternalRecordId,
                        out GachaRecord? targetRecord))
                {
                    addCount++;
                    continue;
                }

                string[] differences = GetFactDifferences(
                    targetRecord,
                    sourceRecord);
                if (differences.Length == 0)
                {
                    skipCount++;
                }
                else
                {
                    conflicts.Add(new GachaPortableRecordConflict(
                        sourceRecord.ExternalRecordId,
                        differences));
                }
            }
        }

        int targetRecordCount = await recordRepository.CountAsync(
            targetAccount.Id,
            cancellationToken);
        return new GachaPortableAccountImportPlan(
            sourceAccount.AccountReference,
            targetAccount.Id,
            ProposedAccountId: null,
            sourceIdentity.NaturalIdentity,
            identityPlan,
            addCount,
            skipCount,
            conflicts.Count,
            conflicts,
            CountOrigins(sourceAccount.Records),
            targetAccount.UpdatedAt,
            targetRecordCount);
    }

    private static GachaPortableAccountImportPlan CreateIdentityCollisionPlan(
        GachaPortableAccount sourceAccount,
        GameRoleIdentity existingIdentity)
    {
        return new GachaPortableAccountImportPlan(
            sourceAccount.AccountReference,
            TargetAccountId: null,
            ProposedAccountId: null,
            sourceAccount.RoleIdentity.NaturalIdentity,
            new GachaPortableIdentityPlan(
                GachaPortableIdentityPlanKind.Collision,
                sourceAccount.RoleIdentity.Id,
                existingIdentity.Id,
                "The source role identity GUID already refers to a different natural identity."),
            AddCount: 0,
            SkipCount: 0,
            ConflictCount: sourceAccount.Records.Count,
            Conflicts: [],
            CountOrigins(sourceAccount.Records),
            TargetAccountUpdatedAt: null,
            TargetRecordCountAtPlan: null);
    }

    private static GachaPortableIdentityPlan ResolveIdentityPlan(
        GameRoleIdentity source,
        GameRoleIdentity? target)
    {
        if (target is null)
        {
            return new GachaPortableIdentityPlan(
                GachaPortableIdentityPlanKind.PreserveSource,
                source.Id,
                source.Id,
                Diagnostic: null);
        }

        if (target.Id == source.Id)
        {
            return new GachaPortableIdentityPlan(
                GachaPortableIdentityPlanKind.Reuse,
                source.Id,
                target.Id,
                Diagnostic: null);
        }

        return new GachaPortableIdentityPlan(
            GachaPortableIdentityPlanKind.AliasSourceToTarget,
            source.Id,
            target.Id,
            "The target already uses another stable GUID for the same natural identity.");
    }

    private static GachaPortableArchivePlan ResolveArchivePlan(
        GachaPortableImportPlanRequest request,
        IReadOnlyList<PlayerArchive> archives)
    {
        string sourceName = request.Package.SourceArchive.Name;
        GachaPortableArchiveCandidate[] exact = archives
            .Where(archive => string.Equals(
                archive.Name,
                sourceName,
                StringComparison.Ordinal))
            .Select(archive => new GachaPortableArchiveCandidate(
                archive.Id,
                archive.Name,
                IsExactMatch: true))
            .ToArray();
        GachaPortableArchiveCandidate[] similar = archives
            .Where(archive => !string.Equals(
                archive.Name,
                sourceName,
                StringComparison.Ordinal) &&
                IsSimilarName(archive.Name, sourceName))
            .Select(archive => new GachaPortableArchiveCandidate(
                archive.Id,
                archive.Name,
                IsExactMatch: false))
            .ToArray();
        GachaPortableArchiveCandidate[] candidates = [.. exact, .. similar];

        if (request.TargetArchiveId is Guid explicitTarget)
        {
            PlayerArchive target = archives.FirstOrDefault(
                archive => archive.Id == explicitTarget)
                ?? throw new KeyNotFoundException(
                    "The explicitly selected target archive does not exist.");
            return new GachaPortableArchivePlan(
                GachaPortableArchivePlanKind.MapExisting,
                target.Id,
                ProposedArchiveId: null,
                target.Name,
                candidates,
                target.UpdatedAt);
        }

        if (request.CreateNewArchive)
        {
            return CreateArchivePlan(
                sourceName,
                archives,
                candidates,
                request.RequireUniqueArchiveNames);
        }

        if (exact.Length == 1)
        {
            PlayerArchive target = archives.Single(
                archive => archive.Id == exact[0].ArchiveId);
            return new GachaPortableArchivePlan(
                GachaPortableArchivePlanKind.MapExisting,
                target.Id,
                ProposedArchiveId: null,
                target.Name,
                candidates,
                target.UpdatedAt);
        }

        if (exact.Length > 1)
        {
            return new GachaPortableArchivePlan(
                GachaPortableArchivePlanKind.RequiresSelection,
                TargetArchiveId: null,
                ProposedArchiveId: null,
                sourceName,
                candidates,
                TargetArchiveUpdatedAt: null);
        }

        return CreateArchivePlan(
            sourceName,
            archives,
            candidates,
            request.RequireUniqueArchiveNames);
    }

    private static GachaPortableArchivePlan CreateArchivePlan(
        string sourceName,
        IReadOnlyList<PlayerArchive> archives,
        IReadOnlyList<GachaPortableArchiveCandidate> candidates,
        bool requireUniqueNames)
    {
        string proposedName = sourceName;
        if (requireUniqueNames)
        {
            var existingNames = archives
                .Select(archive => archive.Name)
                .ToHashSet(StringComparer.Ordinal);
            int suffix = 2;
            while (existingNames.Contains(proposedName))
            {
                proposedName = $"{sourceName} ({suffix++})";
            }
        }

        return new GachaPortableArchivePlan(
            GachaPortableArchivePlanKind.CreateNew,
            TargetArchiveId: null,
            ProposedArchiveId: Guid.NewGuid(),
            proposedName,
            candidates,
            TargetArchiveUpdatedAt: null);
    }

    private static bool IsSimilarName(string left, string right)
    {
        string normalizedLeft = NormalizeName(left);
        string normalizedRight = NormalizeName(right);
        if (normalizedLeft.Length == 0 || normalizedRight.Length == 0)
        {
            return false;
        }

        return string.Equals(
                normalizedLeft,
                normalizedRight,
                StringComparison.Ordinal) ||
            normalizedLeft.Contains(normalizedRight, StringComparison.Ordinal) ||
            normalizedRight.Contains(normalizedLeft, StringComparison.Ordinal);
    }

    private static string NormalizeName(string value)
    {
        return value
            .Trim()
            .Normalize(NormalizationForm.FormKC)
            .ToUpperInvariant();
    }

    private static IReadOnlyDictionary<DataOrigin, int> CountOrigins(
        IReadOnlyList<GachaRecord> records)
    {
        return records
            .GroupBy(record => record.Provenance.Origin)
            .ToDictionary(group => group.Key, group => group.Count());
    }

    private static string[] GetFactDifferences(
        GachaRecord target,
        GachaRecord source)
    {
        var differences = new List<string>();
        AddDifference(differences, "item_name", target.ItemName, source.ItemName);
        AddDifference(differences, "item_id", target.ItemId, source.ItemId);
        AddDifference(differences, "item_type", target.ItemType, source.ItemType);
        AddDifference(differences, "gacha_type", target.GachaType, source.GachaType);
        AddDifference(
            differences,
            "uigf_gacha_type",
            target.UigfGachaType,
            source.UigfGachaType);
        AddDifference(differences, "rank_type", target.RankType, source.RankType);
        AddDifference(differences, "count", target.Count, source.Count);
        AddDifference(
            differences,
            "occurred_at_instant",
            target.Time.UtcDateTime.Ticks,
            source.Time.UtcDateTime.Ticks);
        AddDifference(
            differences,
            "occurred_at_offset",
            target.Time.Offset,
            source.Time.Offset);
        return differences.ToArray();
    }

    private static void AddDifference<T>(
        ICollection<string> differences,
        string field,
        T target,
        T source)
    {
        if (!EqualityComparer<T>.Default.Equals(target, source))
        {
            differences.Add(field);
        }
    }
}

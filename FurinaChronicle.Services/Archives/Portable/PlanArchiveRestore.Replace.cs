// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static readonly IReadOnlySet<ArchiveReplaceDomain> AllReplaceDomains =
        Enum.GetValues<ArchiveReplaceDomain>().ToHashSet();

    private static void ValidateReplaceSelections(ArchiveRestorePlanRequest request)
    {
        ArchiveReplaceSelection[] selections =
            (request.ReplaceSelections ?? []).ToArray();
        if (request.Mode == ArchiveRestoreMode.Merge)
        {
            if (selections.Length != 0)
            {
                throw new ArgumentException(
                    "Merge does not accept Replace selections.",
                    nameof(request));
            }
            return;
        }
        if (selections.Length == 0)
        {
            throw new ArgumentException(
                "Replace requires at least one explicit scope.",
                nameof(request));
        }
        if (selections.Distinct().Count() != selections.Length)
        {
            throw new ArgumentException(
                "Replace selections must be unique.",
                nameof(request));
        }

        IReadOnlyDictionary<Guid, ArchiveCatalogArchive> archives =
            request.Package.Catalog.Archives.ToDictionary(
                item => item.ArchiveReference);
        var covered = new HashSet<(Guid Archive, Guid Account, ArchiveReplaceDomain Domain)>();
        foreach (ArchiveReplaceSelection selection in selections)
        {
            if (selection.SourceArchiveReference == Guid.Empty ||
                !archives.TryGetValue(
                    selection.SourceArchiveReference,
                    out ArchiveCatalogArchive? archive))
            {
                throw new ArgumentException(
                    "A Replace selection references an unknown Archive.",
                    nameof(request));
            }
            Guid[] accounts;
            IReadOnlySet<ArchiveReplaceDomain> domains;
            switch (selection.Kind)
            {
                case ArchiveReplaceScopeKind.Archive
                    when selection.SourceAccountReference is null &&
                         selection.Domain is null:
                    accounts = archive.Accounts
                        .Select(item => item.AccountReference)
                        .ToArray();
                    domains = AllReplaceDomains;
                    break;
                case ArchiveReplaceScopeKind.Account
                    when selection.SourceAccountReference is Guid accountId &&
                         selection.Domain is null:
                    EnsureSourceAccount(archive, accountId, request);
                    accounts = [accountId];
                    domains = AllReplaceDomains;
                    break;
                case ArchiveReplaceScopeKind.Domain
                    when selection.SourceAccountReference is Guid accountId &&
                         selection.Domain is ArchiveReplaceDomain domain &&
                         Enum.IsDefined(domain):
                    EnsureSourceAccount(archive, accountId, request);
                    accounts = [accountId];
                    domains = new HashSet<ArchiveReplaceDomain> { domain };
                    break;
                default:
                    throw new ArgumentException(
                        "A Replace scope has an invalid Archive/account/domain shape.",
                        nameof(request));
            }
            foreach (Guid accountId in accounts)
            foreach (ArchiveReplaceDomain domain in domains)
            {
                if (!covered.Add((
                        selection.SourceArchiveReference,
                        accountId,
                        domain)))
                {
                    throw new ArgumentException(
                        "Replace scopes must not overlap.",
                        nameof(request));
                }
            }
        }
    }

    private static void EnsureSourceAccount(
        ArchiveCatalogArchive archive,
        Guid accountId,
        ArchiveRestorePlanRequest request)
    {
        if (accountId == Guid.Empty ||
            archive.Accounts.All(item => item.AccountReference != accountId))
        {
            throw new ArgumentException(
                "A Replace selection references an unknown account.",
                nameof(request));
        }
    }

    private static bool IsArchiveIncluded(
        ArchiveRestorePlanRequest request,
        Guid sourceArchiveReference) =>
        request.Mode == ArchiveRestoreMode.Merge ||
        (request.ReplaceSelections ?? []).Any(item =>
            item.SourceArchiveReference == sourceArchiveReference);

    private static bool IsAccountIncluded(
        ArchiveRestorePlanRequest request,
        Guid sourceArchiveReference,
        Guid sourceAccountReference) =>
        request.Mode == ArchiveRestoreMode.Merge ||
        (request.ReplaceSelections ?? []).Any(item =>
            item.SourceArchiveReference == sourceArchiveReference &&
            (item.Kind == ArchiveReplaceScopeKind.Archive ||
             item.SourceAccountReference == sourceAccountReference));

    private static IReadOnlySet<ArchiveReplaceDomain> GetSelectedDomains(
        ArchiveRestorePlanRequest request,
        Guid sourceArchiveReference,
        Guid sourceAccountReference)
    {
        if (request.Mode == ArchiveRestoreMode.Merge)
        {
            return AllReplaceDomains;
        }
        var result = new HashSet<ArchiveReplaceDomain>();
        foreach (ArchiveReplaceSelection selection in
                 request.ReplaceSelections ?? [])
        {
            if (selection.SourceArchiveReference != sourceArchiveReference)
            {
                continue;
            }
            if (selection.Kind == ArchiveReplaceScopeKind.Archive ||
                selection.Kind == ArchiveReplaceScopeKind.Account &&
                selection.SourceAccountReference == sourceAccountReference)
            {
                result.UnionWith(AllReplaceDomains);
            }
            else if (selection.Kind == ArchiveReplaceScopeKind.Domain &&
                     selection.SourceAccountReference ==
                         sourceAccountReference &&
                     selection.Domain is ArchiveReplaceDomain domain)
            {
                result.Add(domain);
            }
        }
        return result;
    }

    private static bool IsReplaceDatasetDeclared(
        ArchiveRestorePlanRequest request,
        Guid sourceArchiveReference,
        ArchiveReplaceDomain domain)
    {
        if (request.Mode == ArchiveRestoreMode.Merge)
        {
            return true;
        }
        string reference = domain switch
        {
            ArchiveReplaceDomain.Gacha => "gacha-history",
            ArchiveReplaceDomain.ItemObservation or
                ArchiveReplaceDomain.ManualAcquisition =>
                $"character-weapon-history-{sourceArchiveReference:D}"
                    .ToLowerInvariant(),
            ArchiveReplaceDomain.ChallengeObservation =>
                $"challenge-history-{sourceArchiveReference:D}"
                    .ToLowerInvariant(),
            _ => throw new ArgumentOutOfRangeException(nameof(domain)),
        };
        return request.Package.Summary.Manifest.Datasets.Any(item =>
            string.Equals(
                item.DatasetReference,
                reference,
                StringComparison.Ordinal));
    }

    private static void ValidateReplaceDatasets(
        ArchiveRestorePlanRequest request,
        ICollection<ArchiveRestoreConflict> conflicts)
    {
        if (request.Mode != ArchiveRestoreMode.Replace)
        {
            return;
        }
        foreach (IGrouping<Guid, ArchiveReplaceSelection> group in
                 request.ReplaceSelections!.GroupBy(item =>
                     item.SourceArchiveReference))
        {
            IReadOnlySet<ArchiveReplaceDomain> domains = group.Any(item =>
                item.Kind is ArchiveReplaceScopeKind.Archive or
                    ArchiveReplaceScopeKind.Account)
                ? AllReplaceDomains
                : group.Select(item => item.Domain!.Value).ToHashSet();
            foreach (ArchiveReplaceDomain domain in domains)
            {
                if (IsReplaceDatasetDeclared(
                        request,
                        group.Key,
                        domain))
                {
                    continue;
                }
                conflicts.Add(new ArchiveRestoreConflict(
                    ArchiveRestoreConflictCode.UnsupportedPartialRestore,
                    "The selected Replace domain is absent rather than explicitly complete and empty.",
                    group.Key));
            }
        }
    }

    private static async Task<(
        IReadOnlyList<ArchiveRestoreTargetFactRemoval> Removals,
        IReadOnlyList<ArchiveRestoreAccountRemoval> AccountRemovals)>
        PlanReplaceRemovalsAsync(
            ArchiveRestorePlanRequest request,
            IArchiveRestoreTargetSnapshot target,
            IReadOnlyList<ArchiveRestoreArchiveMapping> archiveMappings,
            IReadOnlyList<ArchiveRestoreAccountMapping> accountMappings,
            IReadOnlyList<ArchiveRestoreFactPlan> sourceFacts,
            CancellationToken cancellationToken)
    {
        if (request.Mode != ArchiveRestoreMode.Replace)
        {
            return ([], []);
        }

        var removals = new List<ArchiveRestoreTargetFactRemoval>();
        var accountRemovals = new List<ArchiveRestoreAccountRemoval>();
        HashSet<(Guid Account, ArchiveRestoreFactKind Kind, string Stable)> source =
            sourceFacts.Select(item => (
                item.TargetAccountId,
                item.Kind,
                item.StableReference)).ToHashSet();

        foreach (ArchiveRestoreAccountMapping mapping in accountMappings.Where(
                     item => item.Kind ==
                         ArchiveRestoreAccountMappingKind.MapExisting &&
                         item.TargetArchiveId is not null &&
                         item.TargetAccountId is not null))
        {
            IReadOnlySet<ArchiveReplaceDomain> domains = GetSelectedDomains(
                request,
                mapping.SourceArchiveReference,
                mapping.SourceAccountReference);
            foreach (ArchiveReplaceDomain domain in domains.Where(item =>
                         IsReplaceDatasetDeclared(
                             request,
                             mapping.SourceArchiveReference,
                             item)))
            {
                await AddTargetDomainRemovalsAsync(
                    target,
                    mapping.TargetArchiveId!.Value,
                    mapping.TargetAccountId!.Value,
                    domain,
                    source,
                    removals,
                    cancellationToken);
            }
        }

        foreach (ArchiveRestoreArchiveMapping archiveMapping in
                 archiveMappings.Where(mapping =>
                     mapping.Kind ==
                         ArchiveRestoreArchiveMappingKind.MapExisting &&
                     (request.ReplaceSelections ?? []).Any(selection =>
                         selection.SourceArchiveReference ==
                             mapping.SourceArchiveReference &&
                         selection.Kind == ArchiveReplaceScopeKind.Archive)))
        {
            if (!AllReplaceDomains.All(domain => IsReplaceDatasetDeclared(
                    request,
                    archiveMapping.SourceArchiveReference,
                    domain)))
            {
                continue;
            }
            Guid targetArchiveId = archiveMapping.TargetArchiveId!.Value;
            HashSet<Guid> mappedAccounts = accountMappings
                .Where(item => item.SourceArchiveReference ==
                    archiveMapping.SourceArchiveReference)
                .Select(item => item.TargetAccountId ??
                    item.ProposedAccountId ??
                    Guid.Empty)
                .Where(item => item != Guid.Empty)
                .ToHashSet();
            ArchiveCatalogArchive targetArchive = target.Catalog.Archives
                .Single(item => item.ArchiveReference == targetArchiveId);
            foreach (ArchiveCatalogAccount targetAccount in
                     targetArchive.Accounts.Where(item =>
                         !mappedAccounts.Contains(item.AccountReference)))
            {
                accountRemovals.Add(new ArchiveRestoreAccountRemoval(
                    targetArchiveId,
                    targetAccount.AccountReference));
                foreach (ArchiveReplaceDomain domain in AllReplaceDomains)
                {
                    await AddTargetDomainRemovalsAsync(
                        target,
                        targetArchiveId,
                        targetAccount.AccountReference,
                        domain,
                        source,
                        removals,
                        cancellationToken);
                }
            }
        }

        return (
            removals.Distinct()
                .OrderBy(item => item.TargetArchiveId)
                .ThenBy(item => item.TargetAccountId)
                .ThenBy(item => item.Kind)
                .ThenBy(item => item.StableReference, StringComparer.Ordinal)
                .ToArray(),
            accountRemovals.Distinct()
                .OrderBy(item => item.TargetArchiveId)
                .ThenBy(item => item.TargetAccountId)
                .ToArray());
    }

    private static async Task AddTargetDomainRemovalsAsync(
        IArchiveRestoreTargetSnapshot target,
        Guid targetArchiveId,
        Guid targetAccountId,
        ArchiveReplaceDomain domain,
        IReadOnlySet<(Guid Account, ArchiveRestoreFactKind Kind, string Stable)> source,
        ICollection<ArchiveRestoreTargetFactRemoval> removals,
        CancellationToken cancellationToken)
    {
        switch (domain)
        {
            case ArchiveReplaceDomain.Gacha:
                await foreach (GachaRecord value in target.ReadGachaRecordsAsync(
                    targetAccountId,
                    cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.GachaCurrent,
                        value.ExternalRecordId,
                        ArchiveRestoreSemanticDigest.ForGacha(value));
                }
                await foreach (GachaRevisionItem value in target
                    .ReadGachaRevisionsAsync(targetAccountId, cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.GachaRevision,
                        value.RevisionId.ToString("D"),
                        ArchiveRestoreSemanticDigest.ForGachaRevision(value));
                }
                break;
            case ArchiveReplaceDomain.ItemObservation:
                await foreach (ItemStateObservation value in target
                    .ReadItemObservationsAsync(targetAccountId, cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.ItemObservation,
                        value.ObservationId.ToString(),
                        ArchiveRestoreSemanticDigest.ForItemObservation(value));
                }
                break;
            case ArchiveReplaceDomain.ManualAcquisition:
                await foreach (ManualAcquisitionState value in target
                    .ReadManualAcquisitionsAsync(targetAccountId, cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.ManualAcquisition,
                        value.Acquisition.AcquisitionId.ToString(),
                        ArchiveRestoreSemanticDigest.ForManualAcquisition(value));
                }
                await foreach (ManualAcquisitionRevisionItem value in target
                    .ReadManualAcquisitionRevisionsAsync(
                        targetAccountId,
                        cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.ManualAcquisitionRevision,
                        value.RevisionId.ToString("D"),
                        ArchiveRestoreSemanticDigest.ForManualRevision(value));
                }
                break;
            case ArchiveReplaceDomain.ChallengeObservation:
                await foreach (ChallengeObservation value in target
                    .ReadChallengeObservationsAsync(
                        targetAccountId,
                        cancellationToken))
                {
                    Add(
                        ArchiveRestoreFactKind.ChallengeObservation,
                        value.ObservationId.ToString(),
                        ArchiveRestoreSemanticDigest.ForChallenge(value));
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(domain));
        }
        return;

        void Add(
            ArchiveRestoreFactKind kind,
            string stableReference,
            string digest)
        {
            if (!source.Contains((targetAccountId, kind, stableReference)))
            {
                removals.Add(new ArchiveRestoreTargetFactRemoval(
                    targetArchiveId,
                    targetAccountId,
                    kind,
                    stableReference,
                    digest));
            }
        }
    }
}

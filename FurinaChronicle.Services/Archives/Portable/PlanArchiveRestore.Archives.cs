// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed partial class PlanArchiveRestore
{
    private static IReadOnlyList<ArchiveRestoreArchiveMapping> PlanArchives(
        ArchiveRestorePlanRequest request,
        IArchiveRestoreTargetSnapshot target,
        ICollection<ArchiveRestoreConflict> conflicts)
    {
        var decisions = (request.ArchiveDecisions ?? [])
            .ToDictionary(item => item.SourceArchiveReference);
        var result = new List<ArchiveRestoreArchiveMapping>();
        var usedTargets = new HashSet<Guid>();
        var plannedNames = new HashSet<string>(
            target.Catalog.Archives.Select(item => item.Name),
            StringComparer.Ordinal);
        foreach (ArchiveCatalogArchive source in request.Package.Catalog.Archives
            .OrderBy(item => item.ArchiveReference))
        {
            ArchiveCatalogArchive[] exact = target.Catalog.Archives
                .Where(item => string.Equals(
                    item.Name, source.Name, StringComparison.Ordinal))
                .ToArray();
            ArchiveCatalogArchive[] similar = target.Catalog.Archives
                .Where(item => !exact.Contains(item) &&
                    AreSimilarNames(item.Name, source.Name))
                .ToArray();
            ArchiveRestoreArchiveCandidate[] candidates = exact
                .Select(item => new ArchiveRestoreArchiveCandidate(
                    item.ArchiveReference, item.Name, true))
                .Concat(similar.Select(item => new ArchiveRestoreArchiveCandidate(
                    item.ArchiveReference, item.Name, false)))
                .OrderByDescending(item => item.IsExactMatch)
                .ThenBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.ArchiveId)
                .ToArray();
            if (decisions.TryGetValue(source.ArchiveReference, out var decision))
            {
                if (decision.Kind == ArchiveRestoreArchiveDecisionKind.MapExisting)
                {
                    ArchiveCatalogArchive? selected = target.Catalog.Archives
                        .SingleOrDefault(item => item.ArchiveReference ==
                            decision.TargetArchiveId);
                    if (selected is null ||
                        !usedTargets.Add(selected.ArchiveReference))
                    {
                        conflicts.Add(new ArchiveRestoreConflict(
                            ArchiveRestoreConflictCode.ArchiveMapping,
                            "The selected target Archive does not exist or is already used by another source Archive.",
                            source.ArchiveReference));
                        result.Add(UnresolvedArchive(source, candidates));
                        continue;
                    }
                    result.Add(new ArchiveRestoreArchiveMapping(
                        source.ArchiveReference,
                        source.Name,
                        ArchiveRestoreArchiveMappingKind.MapExisting,
                        selected.ArchiveReference,
                        null,
                        selected.Name,
                        candidates,
                        selected.UpdatedAt));
                    continue;
                }
                string requestedName = string.IsNullOrEmpty(decision.NewArchiveName)
                    ? source.Name
                    : decision.NewArchiveName;
                if (target.Settings.RequireUniqueArchiveNames &&
                    plannedNames.Contains(requestedName) &&
                    decision.NewArchiveName is not null)
                {
                    conflicts.Add(new ArchiveRestoreConflict(
                        ArchiveRestoreConflictCode.ArchiveMapping,
                        "The explicitly requested new Archive name violates the target unique-name policy.",
                        source.ArchiveReference));
                    result.Add(UnresolvedArchive(source, candidates));
                    continue;
                }
                string newName = AllocateName(
                    requestedName,
                    plannedNames,
                    target.Settings.RequireUniqueArchiveNames);
                plannedNames.Add(newName);
                result.Add(NewArchive(
                    request.Package.PackageFingerprint,
                    source,
                    newName,
                    candidates));
                continue;
            }
            if (exact.Length == 1 && usedTargets.Add(exact[0].ArchiveReference))
            {
                result.Add(new ArchiveRestoreArchiveMapping(
                    source.ArchiveReference,
                    source.Name,
                    ArchiveRestoreArchiveMappingKind.MapExisting,
                    exact[0].ArchiveReference,
                    null,
                    exact[0].Name,
                    candidates,
                    exact[0].UpdatedAt));
            }
            else if (exact.Length > 0 || similar.Length > 0)
            {
                result.Add(UnresolvedArchive(source, candidates));
            }
            else
            {
                string newName = AllocateName(
                    source.Name,
                    plannedNames,
                    target.Settings.RequireUniqueArchiveNames);
                plannedNames.Add(newName);
                result.Add(NewArchive(
                    request.Package.PackageFingerprint,
                    source,
                    newName,
                    candidates));
            }
        }
        return result;
    }

    private static ArchiveRestoreArchiveMapping NewArchive(
        string packageFingerprint,
        ArchiveCatalogArchive source,
        string name,
        IReadOnlyList<ArchiveRestoreArchiveCandidate> candidates) => new(
        source.ArchiveReference,
        source.Name,
        ArchiveRestoreArchiveMappingKind.CreateNew,
        null,
        DeterministicGuid(packageFingerprint, "archive", source.ArchiveReference),
        name,
        candidates,
        null);

    private static ArchiveRestoreArchiveMapping UnresolvedArchive(
        ArchiveCatalogArchive source,
        IReadOnlyList<ArchiveRestoreArchiveCandidate> candidates) => new(
        source.ArchiveReference,
        source.Name,
        ArchiveRestoreArchiveMappingKind.RequiresSelection,
        null,
        null,
        source.Name,
        candidates,
        null);

    private static bool AreSimilarNames(string left, string right) =>
        !string.Equals(left, right, StringComparison.Ordinal) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string AllocateName(
        string requested,
        IReadOnlySet<string> used,
        bool requireUnique)
    {
        if (!requireUnique || !used.Contains(requested)) return requested;
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = $"{requested} ({suffix})";
            if (!used.Contains(candidate)) return candidate;
        }
        throw new InvalidOperationException("Unable to allocate a unique Archive name.");
    }

    private static Guid DeterministicGuid(
        string packageFingerprint,
        string scope,
        Guid sourceReference)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{packageFingerprint}|{scope}|{sourceReference:D}"));
        Span<byte> bytes = hash.AsSpan(0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }
}

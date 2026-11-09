// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Services.Archives.Portable;

public sealed record ArchiveSemanticDifference(
    string Path,
    int ExpectedCount,
    int ActualCount,
    string ValueDigest);

public sealed record ArchiveSemanticComparison(
    string ExpectedDigest,
    string ActualDigest,
    IReadOnlyList<ArchiveSemanticDifference> Differences)
{
    public bool IsEquivalent => Differences.Count == 0;
}

/// <summary>
/// Compares logical Archive content while ignoring target-local parent IDs,
/// export metadata and receiving-side operation state.
/// </summary>
public sealed class ArchiveSemanticComparer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public async Task<ArchiveSemanticComparison> CompareAsync(
        IArchiveRestoreDataSource expected,
        IArchiveRestoreDataSource actual,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        SemanticEntrySet expectedEntries =
            await BuildAsync(expected, cancellationToken);
        SemanticEntrySet actualEntries =
            await BuildAsync(actual, cancellationToken);
        string[] keys = expectedEntries.Counts.Keys
            .Concat(actualEntries.Counts.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var differences = new List<ArchiveSemanticDifference>();
        foreach (string key in keys)
        {
            expectedEntries.Counts.TryGetValue(key, out int expectedCount);
            actualEntries.Counts.TryGetValue(key, out int actualCount);
            if (expectedCount == actualCount)
            {
                continue;
            }

            SemanticEntry entry =
                expectedEntries.Entries.GetValueOrDefault(key) ??
                actualEntries.Entries[key];
            if (expectedCount == 0 && actualCount > 0 &&
                IsReceivingOperationRevision(entry.Path))
            {
                continue;
            }
            differences.Add(new ArchiveSemanticDifference(
                entry.Path,
                expectedCount,
                actualCount,
                Digest(entry.Value)));
        }

        return new ArchiveSemanticComparison(
            expectedEntries.Digest,
            actualEntries.Digest,
            differences);
    }

    private static bool IsReceivingOperationRevision(string path) =>
        path.Contains("/gacha_revision/", StringComparison.Ordinal) ||
        path.Contains("/manual_revision/", StringComparison.Ordinal);

    private static async Task<SemanticEntrySet> BuildAsync(
        IArchiveRestoreDataSource source,
        CancellationToken cancellationToken)
    {
        var entries = new List<SemanticEntry>();
        Add(
            entries,
            "settings/require_unique_archive_names",
            source.Settings.RequireUniqueArchiveNames);

        Dictionary<Guid, ArchiveCatalogRoleIdentity> identities =
            source.Catalog.RoleIdentities.ToDictionary(
                value => value.RoleIdentityReference);
        foreach (ArchiveCatalogRoleIdentity identity in
            source.Catalog.RoleIdentities)
        {
            Add(
                entries,
                $"identity/{identity.IdentityId:D}",
                new
                {
                    identity.IdentityId,
                    identity.NaturalIdentity.GameBiz,
                    identity.NaturalIdentity.Server,
                    identity.NaturalIdentity.Uid,
                });
        }

        Dictionary<Guid, int> undoLimits = source.Settings.Archives
            .ToDictionary(value => value.ArchiveReference, value => value.UndoLimit);
        foreach (ArchiveCatalogArchive archive in source.Catalog.Archives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string archivePath = $"archive/{Anchor(new
            {
                archive.Name,
                archive.CreatedAt,
                archive.UpdatedAt,
            })}";
            Add(entries, $"{archivePath}/parent", new
            {
                archive.Name,
                archive.CreatedAt,
                archive.UpdatedAt,
            });
            if (!undoLimits.TryGetValue(
                    archive.ArchiveReference,
                    out int undoLimit))
            {
                throw new InvalidDataException(
                    "An Archive has no matching portable UndoLimit setting.");
            }
            Add(entries, $"{archivePath}/settings/undo_limit", undoLimit);

            foreach (ArchiveCatalogAccount account in archive.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? identity = null;
                if (account.RoleIdentityReference is Guid reference)
                {
                    if (!identities.TryGetValue(
                            reference,
                            out ArchiveCatalogRoleIdentity? resolved))
                    {
                        throw new InvalidDataException(
                            "An account references a missing role identity.");
                    }
                    identity = new
                    {
                        resolved.IdentityId,
                        resolved.NaturalIdentity.GameBiz,
                        resolved.NaturalIdentity.Server,
                        resolved.NaturalIdentity.Uid,
                    };
                }

                string accountPath = $"{archivePath}/account/{Anchor(new
                {
                    Identity = identity,
                    account.DisplayName,
                    account.LegacyUid,
                    account.LegacyServer,
                    account.IsPlaceholder,
                    account.UnresolvedReason,
                    account.CreatedAt,
                    account.UpdatedAt,
                })}";
                Add(entries, $"{accountPath}/parent", new
                {
                    Identity = identity,
                    account.DisplayName,
                    account.LegacyUid,
                    account.LegacyServer,
                    account.IsPlaceholder,
                    account.UnresolvedReason,
                    account.CreatedAt,
                    account.UpdatedAt,
                });

                await AddFactsAsync(
                    entries,
                    accountPath,
                    "gacha_current",
                    source.ReadGachaRecordsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.ExternalRecordId,
                    ArchiveRestoreSemanticDigest.ForGacha,
                    cancellationToken);
                await AddFactsAsync(
                    entries,
                    accountPath,
                    "gacha_revision",
                    source.ReadGachaRevisionsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.RevisionId.ToString("D"),
                    ArchiveRestoreSemanticDigest.ForGachaRevision,
                    cancellationToken);
                await AddFactsAsync(
                    entries,
                    accountPath,
                    "item_observation",
                    source.ReadItemObservationsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.ObservationId.ToString(),
                    ArchiveRestoreSemanticDigest.ForItemObservation,
                    cancellationToken);
                await AddFactsAsync(
                    entries,
                    accountPath,
                    "manual_acquisition",
                    source.ReadManualAcquisitionsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.Acquisition.AcquisitionId.ToString(),
                    ArchiveRestoreSemanticDigest.ForManualAcquisition,
                    cancellationToken);
                await AddFactsAsync(
                    entries,
                    accountPath,
                    "manual_revision",
                    source.ReadManualAcquisitionRevisionsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.RevisionId.ToString("D"),
                    ArchiveRestoreSemanticDigest.ForManualRevision,
                    cancellationToken);
                await AddFactsAsync(
                    entries,
                    accountPath,
                    "challenge_observation",
                    source.ReadChallengeObservationsAsync(
                        account.AccountReference,
                        cancellationToken),
                    value => value.ObservationId.ToString(),
                    ArchiveRestoreSemanticDigest.ForChallenge,
                    cancellationToken);
            }
        }

        return SemanticEntrySet.Create(entries);
    }

    private static async Task AddFactsAsync<T>(
        ICollection<SemanticEntry> entries,
        string accountPath,
        string kind,
        IAsyncEnumerable<T> values,
        Func<T, string> stableReference,
        Func<T, string> semanticDigest,
        CancellationToken cancellationToken)
    {
        await foreach (T value in values.WithCancellation(cancellationToken))
        {
            Add(
                entries,
                $"{accountPath}/{kind}/{stableReference(value)}",
                semanticDigest(value));
        }
    }

    private static void Add<T>(
        ICollection<SemanticEntry> entries,
        string path,
        T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        entries.Add(new SemanticEntry(path, json));
    }

    private static string Anchor<T>(T value)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);
        return Digest(json)[..16];
    }

    private static string Digest(string value) =>
        Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record SemanticEntry(string Path, string Value)
    {
        public string Key => $"{Path}\u001f{Value}";
    }

    private sealed record SemanticEntrySet(
        IReadOnlyDictionary<string, int> Counts,
        IReadOnlyDictionary<string, SemanticEntry> Entries,
        string Digest)
    {
        public static SemanticEntrySet Create(
            IReadOnlyCollection<SemanticEntry> entries)
        {
            Dictionary<string, int> counts = entries
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count(),
                    StringComparer.Ordinal);
            Dictionary<string, SemanticEntry> byKey = entries
                .GroupBy(value => value.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First(),
                    StringComparer.Ordinal);
            string canonical = string.Join(
                "\n",
                counts.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Value}:{pair.Key}"));
            return new SemanticEntrySet(
                counts,
                byKey,
                ArchiveSemanticComparer.Digest(canonical));
        }
    }
}

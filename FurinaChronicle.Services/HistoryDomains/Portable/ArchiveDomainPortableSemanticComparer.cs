// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;

namespace FurinaChronicle.Services.HistoryDomains.Portable;

public sealed record ArchiveDomainPortableSemanticComparison(
    bool Equivalent,
    string ExpectedDigest,
    string ActualDigest,
    IReadOnlyList<string> Differences);

/// <summary>
/// Compares the portable semantics that must survive a database-to-database
/// transfer. Local archive/account identifiers, export time, package
/// fingerprint and receiver-side operation records are deliberately excluded.
/// </summary>
public static class ArchiveDomainPortableSemanticComparer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static ArchiveDomainPortableSemanticComparison Compare(
        ArchiveDomainPortablePackage expected,
        ArchiveDomainPortablePackage actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        PackageSemantics expectedSemantics = Normalize(expected);
        PackageSemantics actualSemantics = Normalize(actual);
        string expectedJson = JsonSerializer.Serialize(
            expectedSemantics,
            JsonOptions);
        string actualJson = JsonSerializer.Serialize(
            actualSemantics,
            JsonOptions);
        var differences = new List<string>();

        if (expected.DatasetKind != actual.DatasetKind)
        {
            differences.Add(
                $"Dataset kind differs: expected {expected.DatasetKind}, actual {actual.DatasetKind}.");
        }
        if (expected.FormatMajor != actual.FormatMajor ||
            expected.FormatMinor != actual.FormatMinor)
        {
            differences.Add(
                $"Format version differs: expected {expected.FormatMajor}.{expected.FormatMinor}, actual {actual.FormatMajor}.{actual.FormatMinor}.");
        }

        IReadOnlyDictionary<string, AccountSemantics> expectedAccounts =
            expectedSemantics.Accounts.ToDictionary(
                account => account.NaturalIdentityKey,
                StringComparer.Ordinal);
        IReadOnlyDictionary<string, AccountSemantics> actualAccounts =
            actualSemantics.Accounts.ToDictionary(
                account => account.NaturalIdentityKey,
                StringComparer.Ordinal);
        foreach (string missing in expectedAccounts.Keys.Except(
                     actualAccounts.Keys,
                     StringComparer.Ordinal))
        {
            differences.Add($"Account is missing: {missing}.");
        }
        foreach (string unexpected in actualAccounts.Keys.Except(
                     expectedAccounts.Keys,
                     StringComparer.Ordinal))
        {
            differences.Add($"Unexpected account: {unexpected}.");
        }
        foreach (string identity in expectedAccounts.Keys.Intersect(
                     actualAccounts.Keys,
                     StringComparer.Ordinal))
        {
            string expectedAccount = JsonSerializer.Serialize(
                expectedAccounts[identity],
                JsonOptions);
            string actualAccount = JsonSerializer.Serialize(
                actualAccounts[identity],
                JsonOptions);
            if (!string.Equals(
                    expectedAccount,
                    actualAccount,
                    StringComparison.Ordinal))
            {
                differences.Add(
                    $"Portable semantics differ for account {identity}.");
            }
        }

        return new ArchiveDomainPortableSemanticComparison(
            differences.Count == 0 && string.Equals(
                expectedJson,
                actualJson,
                StringComparison.Ordinal),
            ComputeDigest(expectedJson),
            ComputeDigest(actualJson),
            differences);
    }

    public static string ComputeSemanticDigest(
        ArchiveDomainPortablePackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        string json = JsonSerializer.Serialize(Normalize(package), JsonOptions);
        return ComputeDigest(json);
    }

    private static PackageSemantics Normalize(
        ArchiveDomainPortablePackage package) => new(
        package.DatasetKind,
        package.FormatMajor,
        package.FormatMinor,
        package.Accounts
            .Select(NormalizeAccount)
            .OrderBy(account => account.NaturalIdentityKey, StringComparer.Ordinal)
            .ToArray());

    private static AccountSemantics NormalizeAccount(
        ArchiveDomainPortableAccount account) => new(
        account.RoleIdentity.NaturalIdentity.ToDeterministicName(),
        account.RoleIdentity.Id,
        account.RoleIdentity.NaturalIdentity,
        account.ItemObservations
            .Select(observation => new ItemObservationSemantics(
                observation.ObservationId,
                observation.Kind,
                observation.CollectionCompleteness,
                observation.Entries
                    .OrderBy(entry => entry.StableItemKey, StringComparer.Ordinal)
                    .ToArray(),
                observation.Provenance))
            .OrderBy(observation => observation.ObservationId.ToString(), StringComparer.Ordinal)
            .ToArray(),
        account.ManualAcquisitions
            .Select(NormalizeAcquisition)
            .OrderBy(acquisition => acquisition.AcquisitionId.ToString(), StringComparer.Ordinal)
            .ToArray(),
        account.ManualRevisions
            .Select(revision => new ManualRevisionSemantics(
                revision.RevisionId,
                revision.ChangeKind,
                revision.BeforeVersion,
                revision.AfterVersion,
                NormalizeOptionalAcquisition(revision.Before),
                NormalizeOptionalAcquisition(revision.After),
                revision.CreatedAt))
            .OrderBy(revision => revision.CreatedAt)
            .ThenBy(revision => revision.RevisionId)
            .ToArray(),
        account.ChallengeObservations
            .Select(challenge => new ChallengeObservationSemantics(
                challenge.ObservationId,
                challenge.Mode,
                challenge.SeasonId,
                challenge.PeriodStart,
                challenge.PeriodEnd,
                challenge.Completeness,
                challenge.SpiralAbyssFloors,
                challenge.Provenance))
            .OrderBy(challenge => challenge.ObservationId.ToString(), StringComparer.Ordinal)
            .ToArray());

    private static ManualAcquisitionSemantics NormalizeAcquisition(
        ManualAcquisition acquisition) => new(
            acquisition.AcquisitionId,
            acquisition.Kind,
            acquisition.TypeId,
            acquisition.OccurredAt,
            acquisition.Reason,
            acquisition.ReasonDetail,
            acquisition.Provenance);

    private static ManualAcquisitionSemantics? NormalizeOptionalAcquisition(
        ManualAcquisition? acquisition) => acquisition is null
        ? null
        : NormalizeAcquisition(acquisition);

    private static string ComputeDigest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();

    private sealed record PackageSemantics(
        ArchiveDomainDatasetKind DatasetKind,
        int FormatMajor,
        int FormatMinor,
        IReadOnlyList<AccountSemantics> Accounts);

    private sealed record AccountSemantics(
        string NaturalIdentityKey,
        GameRoleIdentityId RoleIdentityId,
        GameRoleNaturalIdentity NaturalIdentity,
        IReadOnlyList<ItemObservationSemantics> ItemObservations,
        IReadOnlyList<ManualAcquisitionSemantics?> ManualAcquisitions,
        IReadOnlyList<ManualRevisionSemantics> ManualRevisions,
        IReadOnlyList<ChallengeObservationSemantics> ChallengeObservations);

    private sealed record ItemObservationSemantics(
        ItemObservationId ObservationId,
        InventoryItemKind Kind,
        DataCompleteness CollectionCompleteness,
        IReadOnlyList<InventoryStateEntry> Entries,
        RecordProvenance Provenance);

    private sealed record ManualAcquisitionSemantics(
        ManualAcquisitionId AcquisitionId,
        InventoryItemKind Kind,
        string TypeId,
        OccurrenceTime OccurredAt,
        ManualAcquisitionReason Reason,
        string? ReasonDetail,
        RecordProvenance Provenance);

    private sealed record ManualRevisionSemantics(
        Guid RevisionId,
        EntityChangeKind ChangeKind,
        FactVersion? BeforeVersion,
        FactVersion? AfterVersion,
        ManualAcquisitionSemantics? Before,
        ManualAcquisitionSemantics? After,
        DateTimeOffset CreatedAt);

    private sealed record ChallengeObservationSemantics(
        ChallengeObservationId ObservationId,
        ChallengeMode Mode,
        string SeasonId,
        DateTimeOffset? PeriodStart,
        DateTimeOffset? PeriodEnd,
        DataCompleteness Completeness,
        IReadOnlyList<SpiralAbyssFloorResult> SpiralAbyssFloors,
        RecordProvenance Provenance);
}

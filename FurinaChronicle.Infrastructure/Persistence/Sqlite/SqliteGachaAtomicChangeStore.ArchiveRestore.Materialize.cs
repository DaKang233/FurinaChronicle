// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed partial class SqliteGachaAtomicChangeStore
{
    private async Task<IReadOnlyList<MaterializedArchiveFact>>
        MaterializeArchiveFactsAsync(
            ArchiveRestoreApplyRequest request,
            CancellationToken cancellationToken)
    {
        var result = new List<MaterializedArchiveFact>();
        foreach (ArchiveRestoreFactPlan fact in request.Plan.Facts.Where(item =>
                     item.Action is ArchiveRestoreFactAction.Add or
                         ArchiveRestoreFactAction.Reintroduce))
        {
            cancellationToken.ThrowIfCancellationRequested();
            object? value = fact.Kind switch
            {
                ArchiveRestoreFactKind.GachaCurrent =>
                    (await request.Package.FindGachaRecordsAsync(
                        fact.SourceAccountReference,
                        [fact.StableReference],
                        cancellationToken)).GetValueOrDefault(
                            fact.StableReference)?.Value,
                ArchiveRestoreFactKind.GachaRevision =>
                    (await request.Package.FindGachaRevisionAsync(
                        fact.SourceAccountReference,
                        ParseArchiveStableGuid(fact),
                        cancellationToken))?.Value,
                ArchiveRestoreFactKind.ItemObservation =>
                    (await request.Package.FindItemObservationAsync(
                        fact.SourceAccountReference,
                        new ItemObservationId(ParseArchiveStableGuid(fact)),
                        cancellationToken))?.Value,
                ArchiveRestoreFactKind.ManualAcquisition =>
                    (await request.Package.FindManualAcquisitionAsync(
                        fact.SourceAccountReference,
                        new ManualAcquisitionId(ParseArchiveStableGuid(fact)),
                        cancellationToken))?.Value,
                ArchiveRestoreFactKind.ManualAcquisitionRevision =>
                    (await request.Package.FindManualAcquisitionRevisionAsync(
                        fact.SourceAccountReference,
                        ParseArchiveStableGuid(fact),
                        cancellationToken))?.Value,
                ArchiveRestoreFactKind.ChallengeObservation =>
                    (await request.Package.FindChallengeObservationAsync(
                        fact.SourceAccountReference,
                        new ChallengeObservationId(ParseArchiveStableGuid(fact)),
                        cancellationToken))?.Value,
                _ => throw new ArgumentOutOfRangeException(nameof(fact)),
            };
            if (value is null ||
                !string.Equals(
                    fact.SourceSemanticDigest,
                    ComputeArchiveMaterializedDigest(fact.Kind, value),
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The staged Archive fact changed after RestorePlan creation.");
            }
            result.Add(new MaterializedArchiveFact(fact, value));
        }
        return result;
    }

    private static string ComputeArchiveMaterializedDigest(
        ArchiveRestoreFactKind kind,
        object value) => kind switch
        {
            ArchiveRestoreFactKind.GachaCurrent =>
                ArchiveRestoreSemanticDigest.ForGacha((GachaRecord)value),
            ArchiveRestoreFactKind.GachaRevision =>
                ArchiveRestoreSemanticDigest.ForGachaRevision(
                    (GachaRevisionItem)value),
            ArchiveRestoreFactKind.ItemObservation =>
                ArchiveRestoreSemanticDigest.ForItemObservation(
                    (ItemStateObservation)value),
            ArchiveRestoreFactKind.ManualAcquisition =>
                ArchiveRestoreSemanticDigest.ForManualAcquisition(
                    (ManualAcquisitionState)value),
            ArchiveRestoreFactKind.ManualAcquisitionRevision =>
                ArchiveRestoreSemanticDigest.ForManualRevision(
                    (ManualAcquisitionRevisionItem)value),
            ArchiveRestoreFactKind.ChallengeObservation =>
                ArchiveRestoreSemanticDigest.ForChallenge(
                    (ChallengeObservation)value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

    private static Guid ParseArchiveStableGuid(ArchiveRestoreFactPlan fact) =>
        Guid.TryParseExact(fact.StableReference, "D", out Guid value) &&
        value != Guid.Empty
            ? value
            : throw new InvalidDataException(
                "An Archive fact has an invalid stable GUID.");
}

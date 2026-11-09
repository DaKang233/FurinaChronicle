// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.TestDoubles;

internal sealed class MemoryArchiveRestoreDataSource(
    ArchiveCatalog catalog,
    ArchivePortableSettings settings,
    DateTimeOffset? generatedAt = null)
    : IArchiveRestoreDataSource
{
    public DateTimeOffset GeneratedAt { get; } =
        generatedAt ?? new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    public ArchiveExportScope Scope { get; } = ArchiveExportScope.All();

    public ArchiveCatalog Catalog { get; } = catalog;

    public ArchivePortableSettings Settings { get; } = settings;

    public Dictionary<Guid, List<GachaRecord>> GachaRecords { get; } = [];

    public Dictionary<Guid, List<GachaRevisionItem>> GachaRevisions { get; } = [];

    public Dictionary<Guid, List<ItemStateObservation>> ItemObservations { get; } = [];

    public Dictionary<Guid, List<ManualAcquisitionState>> ManualAcquisitions { get; } = [];

    public Dictionary<Guid, List<ManualAcquisitionRevisionItem>>
        ManualRevisions { get; } = [];

    public Dictionary<Guid, List<ChallengeObservation>>
        ChallengeObservations { get; } = [];

    public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(GachaRecords, accountReference, cancellationToken);

    public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(GachaRevisions, accountReference, cancellationToken);

    public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ItemObservations, accountReference, cancellationToken);

    public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ManualAcquisitions, accountReference, cancellationToken);

    public IAsyncEnumerable<ManualAcquisitionRevisionItem>
        ReadManualAcquisitionRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
        ReadAsync(ManualRevisions, accountReference, cancellationToken);

    public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(ChallengeObservations, accountReference, cancellationToken);

    public Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
        FindGachaRecordsAsync(
            Guid accountReference,
            IReadOnlyCollection<string> externalRecordIds,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HashSet<string> ids = externalRecordIds.ToHashSet(StringComparer.Ordinal);
        IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>> result =
            Values(GachaRecords, accountReference)
                .Where(value => ids.Contains(value.ExternalRecordId))
                .ToDictionary(
                    value => value.ExternalRecordId,
                    value => new ArchiveIndexedFact<GachaRecord>(
                        value,
                        ArchiveRestoreSemanticDigest.ForGacha(value)),
                    StringComparer.Ordinal);
        return Task.FromResult(result);
    }

    public Task<ArchiveIndexedFact<GachaRevisionItem>?> FindGachaRevisionAsync(
        Guid accountReference,
        Guid revisionId,
        CancellationToken cancellationToken = default) =>
        FindOneAsync(
            GachaRevisions,
            accountReference,
            value => value.RevisionId == revisionId,
            ArchiveRestoreSemanticDigest.ForGachaRevision,
            cancellationToken);

    public Task<ArchiveIndexedFact<ItemStateObservation>?> FindItemObservationAsync(
        Guid accountReference,
        ItemObservationId observationId,
        CancellationToken cancellationToken = default) =>
        FindOneAsync(
            ItemObservations,
            accountReference,
            value => value.ObservationId == observationId,
            ArchiveRestoreSemanticDigest.ForItemObservation,
            cancellationToken);

    public Task<ArchiveIndexedFact<ManualAcquisitionState>?>
        FindManualAcquisitionAsync(
            Guid accountReference,
            ManualAcquisitionId acquisitionId,
            CancellationToken cancellationToken = default) =>
        FindOneAsync(
            ManualAcquisitions,
            accountReference,
            value => value.Acquisition.AcquisitionId == acquisitionId,
            ArchiveRestoreSemanticDigest.ForManualAcquisition,
            cancellationToken);

    public Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
        FindManualAcquisitionRevisionAsync(
            Guid accountReference,
            Guid revisionId,
            CancellationToken cancellationToken = default) =>
        FindOneAsync(
            ManualRevisions,
            accountReference,
            value => value.RevisionId == revisionId,
            ArchiveRestoreSemanticDigest.ForManualRevision,
            cancellationToken);

    public Task<ArchiveIndexedFact<ChallengeObservation>?>
        FindChallengeObservationAsync(
            Guid accountReference,
            ChallengeObservationId observationId,
            CancellationToken cancellationToken = default) =>
        FindOneAsync(
            ChallengeObservations,
            accountReference,
            value => value.ObservationId == observationId,
            ArchiveRestoreSemanticDigest.ForChallenge,
            cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static IReadOnlyList<T> Values<T>(
        IReadOnlyDictionary<Guid, List<T>> source,
        Guid accountReference) =>
        source.TryGetValue(accountReference, out List<T>? values)
            ? values
            : [];

    private static async IAsyncEnumerable<T> ReadAsync<T>(
        IReadOnlyDictionary<Guid, List<T>> source,
        Guid accountReference,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (T value in Values(source, accountReference))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
    }

    private static Task<ArchiveIndexedFact<T>?> FindOneAsync<T>(
        IReadOnlyDictionary<Guid, List<T>> source,
        Guid accountReference,
        Func<T, bool> predicate,
        Func<T, string> semanticDigest,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        T? value = Values(source, accountReference).SingleOrDefault(predicate);
        ArchiveIndexedFact<T>? result = value is null
            ? null
            : new ArchiveIndexedFact<T>(value, semanticDigest(value));
        return Task.FromResult(result);
    }
}

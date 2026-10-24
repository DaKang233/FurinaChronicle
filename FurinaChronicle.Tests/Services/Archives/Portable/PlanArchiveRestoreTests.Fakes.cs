// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    private sealed class QueueTargetFactory(params MemoryArchiveData[] targets)
        : IArchiveRestoreTargetSnapshotFactory
    {
        private readonly Queue<MemoryArchiveData> queue = new(targets);
        private MemoryArchiveData? last;

        public Task<IArchiveRestoreTargetSnapshot> OpenAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (queue.Count > 0) last = queue.Dequeue();
            return Task.FromResult<IArchiveRestoreTargetSnapshot>(
                last ?? throw new InvalidOperationException("No target fixture."));
        }
    }

    private sealed class MemoryArchiveData :
        IArchiveRestoreStagedPackage,
        IArchiveRestoreTargetSnapshot
    {
        private MemoryArchiveData(
            ArchiveCatalog catalog,
            ArchivePortableSettings settings,
            string packageFingerprint,
            string stateToken)
        {
            Catalog = catalog;
            Settings = settings;
            PackageFingerprint = packageFingerprint;
            StateToken = stateToken;
            var manifest = new ArchiveManifest(
                "furina-archive",
                "1.0",
                GeneratedAt,
                new ArchiveGeneratorInfo(
                    "FurinaChronicle", "0.8.11", "8C.2", "fixture", "test"),
                new ArchiveDeclaredScope(
                    ArchiveExportScopeKind.AllArchives,
                    catalog.Archives.Select(item => item.ArchiveReference).ToArray(),
                    "supported_logical_scope"),
                [],
                "settings/portable.v1.json",
                "checksums.sha256");
            Summary = new ArchivePackageSummary(
                manifest,
                catalog,
                settings,
                0,
                0,
                0,
                0,
                100,
                []);
        }

        public static MemoryArchiveData Source(
            ArchiveCatalog catalog,
            ArchivePortableSettings settings) => new(
            catalog,
            settings,
            new string('b', 64),
            "source-state");

        public static MemoryArchiveData Target(
            ArchiveCatalog catalog,
            ArchivePortableSettings settings,
            string stateToken) => new(
            catalog,
            settings,
            "target-package",
            stateToken);

        public DateTimeOffset GeneratedAt { get; } =
            DateTimeOffset.Parse("2026-10-23T00:00:00Z");
        public ArchiveExportScope Scope { get; } = ArchiveExportScope.All();
        public ArchiveCatalog Catalog { get; }
        public ArchivePortableSettings Settings { get; }
        public string PackageFingerprint { get; }
        public ArchivePackageSummary Summary { get; }
        public long ContainerLength { get; } = 100;
        public long StagingLength { get; } = 200;
        public int SchemaVersion { get; } = 7;
        public string StateToken { get; set; }
        public long SnapshotLength { get; } = 300;
        public Dictionary<Guid, IReadOnlyList<GachaRecord>> Gacha { get; } = [];
        public Dictionary<Guid, IReadOnlyList<GachaRevisionItem>> GachaRevisions
            { get; } = [];
        public Dictionary<Guid, IReadOnlyList<ItemStateObservation>> Items { get; } = [];
        public Dictionary<Guid, IReadOnlyList<ManualAcquisitionState>> Manuals { get; } = [];
        public Dictionary<Guid, IReadOnlyList<ManualAcquisitionRevisionItem>>
            ManualRevisions { get; } = [];
        public Dictionary<Guid, IReadOnlyList<ChallengeObservation>> Challenges
            { get; } = [];
        public List<ArchiveRestoreActiveTombstone> Tombstones { get; } = [];

        public void AddDomainFixture(Guid archiveId, Guid accountId)
        {
            DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+08:00");
            var official = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(FetchedAt: now));
            GachaRecord current = Gacha[accountId].Single();
            GachaRevisions[accountId] =
            [new GachaRevisionItem(
                Guid.Parse("21000000-0000-0000-0000-000000000001"),
                Guid.NewGuid(),
                new GachaFactReference(accountId, current.ExternalRecordId),
                EntityChangeKind.Update,
                new FactVersion(1),
                new FactVersion(2),
                current with { ItemName = "Old" },
                current,
                now)];
            Items[accountId] =
            [new ItemStateObservation(
                new ItemObservationId(
                    Guid.Parse("21000000-0000-0000-0000-000000000002")),
                accountId,
                InventoryItemKind.Character,
                DataCompleteness.Complete,
                [new InventoryStateEntry(
                    InventoryItemKind.Character,
                    "10000089",
                    null,
                    1,
                    90,
                    0,
                    InventoryFieldCoverage.Quantity |
                        InventoryFieldCoverage.Level |
                        InventoryFieldCoverage.Progression,
                    DataCompleteness.Complete)],
                official)];
            var user = new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: now));
            var acquisition = new ManualAcquisition(
                new ManualAcquisitionId(
                    Guid.Parse("21000000-0000-0000-0000-000000000003")),
                accountId,
                InventoryItemKind.Character,
                "10000089",
                OccurrenceTime.At(now, TimePrecision.Second),
                ManualAcquisitionReason.Other,
                "Fixture",
                user);
            Manuals[accountId] =
            [new ManualAcquisitionState(acquisition, new FactVersion(2), archiveId)];
            ManualRevisions[accountId] =
            [new ManualAcquisitionRevisionItem(
                Guid.Parse("21000000-0000-0000-0000-000000000004"),
                Guid.NewGuid(),
                EntityChangeKind.Insert,
                null,
                new FactVersion(1),
                null,
                acquisition,
                now)];
            Challenges[accountId] =
            [new ChallengeObservation(
                new ChallengeObservationId(
                    Guid.Parse("21000000-0000-0000-0000-000000000005")),
                accountId,
                ChallengeMode.SpiralAbyss,
                "2026-10-A",
                now.AddDays(-15),
                now,
                DataCompleteness.Complete,
                [],
                official)];
        }

        public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Read(Gacha, accountReference, cancellationToken);
        public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Read(GachaRevisions, accountReference, cancellationToken);
        public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Read(Items, accountReference, cancellationToken);
        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Read(Manuals, accountReference, cancellationToken);
        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            Read(ManualRevisions, accountReference, cancellationToken);
        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Read(Challenges, accountReference, cancellationToken);

        public Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
            FindGachaRecordsAsync(
                Guid accountReference,
                IReadOnlyCollection<string> externalRecordIds,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>> result =
                Values(Gacha, accountReference)
                    .Where(item => externalRecordIds.Contains(
                        item.ExternalRecordId,
                        StringComparer.Ordinal))
                    .ToDictionary(
                        item => item.ExternalRecordId,
                        item => new ArchiveIndexedFact<GachaRecord>(
                            item,
                            ArchiveRestoreSemanticDigest.ForGacha(item)),
                        StringComparer.Ordinal);
            return Task.FromResult(result);
        }

        public Task<ArchiveIndexedFact<GachaRevisionItem>?> FindGachaRevisionAsync(
            Guid accountReference,
            Guid revisionId,
            CancellationToken cancellationToken = default) =>
            Find(
                GachaRevisions,
                accountReference,
                item => item.RevisionId == revisionId,
                ArchiveRestoreSemanticDigest.ForGachaRevision,
                cancellationToken);

        public Task<ArchiveIndexedFact<ItemStateObservation>?> FindItemObservationAsync(
            Guid accountReference,
            ItemObservationId observationId,
            CancellationToken cancellationToken = default) =>
            Find(
                Items,
                accountReference,
                item => item.ObservationId == observationId,
                ArchiveRestoreSemanticDigest.ForItemObservation,
                cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionState>?>
            FindManualAcquisitionAsync(
                Guid accountReference,
                ManualAcquisitionId acquisitionId,
                CancellationToken cancellationToken = default) =>
            Find(
                Manuals,
                accountReference,
                item => item.Acquisition.AcquisitionId == acquisitionId,
                ArchiveRestoreSemanticDigest.ForManualAcquisition,
                cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
            FindManualAcquisitionRevisionAsync(
                Guid accountReference,
                Guid revisionId,
                CancellationToken cancellationToken = default) =>
            Find(
                ManualRevisions,
                accountReference,
                item => item.RevisionId == revisionId,
                ArchiveRestoreSemanticDigest.ForManualRevision,
                cancellationToken);

        public Task<ArchiveIndexedFact<ChallengeObservation>?>
            FindChallengeObservationAsync(
                Guid accountReference,
                ChallengeObservationId observationId,
                CancellationToken cancellationToken = default) =>
            Find(
                Challenges,
                accountReference,
                item => item.ObservationId == observationId,
                ArchiveRestoreSemanticDigest.ForChallenge,
                cancellationToken);

        public Task<IReadOnlyList<ArchiveRestoreActiveTombstone>>
            FindActiveTombstonesAsync(
                IReadOnlyCollection<ArchiveRestoreTombstoneProbe> probes,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<ArchiveRestoreActiveTombstone> result = Tombstones
                .Where(item => probes.Contains(item.Probe))
                .ToArray();
            return Task.FromResult(result);
        }

        private static async IAsyncEnumerable<T> Read<T>(
            IReadOnlyDictionary<Guid, IReadOnlyList<T>> values,
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (T item in Values(values, accountReference))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return item;
            }
            await Task.CompletedTask;
        }

        private static IReadOnlyList<T> Values<T>(
            IReadOnlyDictionary<Guid, IReadOnlyList<T>> values,
            Guid accountReference) =>
            values.TryGetValue(accountReference, out var result) ? result : [];

        private static Task<ArchiveIndexedFact<T>?> Find<T>(
            IReadOnlyDictionary<Guid, IReadOnlyList<T>> values,
            Guid accountReference,
            Func<T, bool> predicate,
            Func<T, string> digest,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            T? value = Values(values, accountReference).SingleOrDefault(predicate);
            ArchiveIndexedFact<T>? result = value is null
                ? null
                : new ArchiveIndexedFact<T>(value, digest(value));
            return Task.FromResult(result);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

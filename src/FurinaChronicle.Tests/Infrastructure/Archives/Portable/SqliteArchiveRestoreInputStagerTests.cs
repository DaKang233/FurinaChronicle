// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Infrastructure.Archives.Portable;

public sealed class SqliteArchiveRestoreInputStagerTests
{
    [Fact]
    public async Task StageAsync_UsesValidatedPrivateSqliteAndDeletesSessionOnDispose()
    {
        string root = CreateRoot();
        try
        {
            await using TestSnapshot snapshot = TestSnapshot.Create();
            using var archive = new MemoryStream();
            await new ArchivePackageWriter().WriteAsync(
                archive,
                snapshot,
                new ArchiveGeneratorInfo(
                    "FurinaChronicle", "0.8.11", "8C.2", "fixture", "test"));
            archive.Position = 0;
            var stager = new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(),
                new ArchiveRestoreStorageOptions(root));

            IArchiveRestoreStagedPackage staged = await stager.StageAsync(
                new NonSeekableReadStream(archive));

            Assert.Equal(64, staged.PackageFingerprint.Length);
            Assert.True(staged.ContainerLength > 0);
            Assert.True(staged.StagingLength > 0);
            Assert.Equal(1, staged.Summary.GachaCurrentFactCount);
            Assert.Equal(1, staged.Summary.GachaRevisionCount);
            Assert.Equal(3, staged.Summary.DomainFactCount);
            Assert.Equal(1, staged.Summary.DomainRevisionCount);
            Guid accountId = staged.Catalog.Archives.Single().Accounts.Single()
                .AccountReference;
            GachaRecord record = Assert.Single(
                await CollectAsync(staged.ReadGachaRecordsAsync(accountId)));
            Assert.Equal("1001", record.ExternalRecordId);
            Assert.Equal(snapshot.Gacha.Time, record.Time);
            Assert.Equal(snapshot.Gacha.Provenance, record.Provenance);
            GachaRevisionItem gachaRevision = Assert.Single(
                await CollectAsync(staged.ReadGachaRevisionsAsync(accountId)));
            Assert.Equal(Guid.Empty, gachaRevision.ChangeSetId);
            ManualAcquisitionRevisionItem manualRevision = Assert.Single(
                await CollectAsync(
                    staged.ReadManualAcquisitionRevisionsAsync(accountId)));
            Assert.Equal(Guid.Empty, manualRevision.ChangeSetId);
            IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>> indexed =
                await staged.FindGachaRecordsAsync(accountId, ["1001", "missing"]);
            Assert.Single(indexed);
            Assert.Equal(64, indexed["1001"].SemanticDigest.Length);

            await staged.DisposeAsync();
            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task StageAsync_InvalidInputCleansPrivateSession()
    {
        string root = CreateRoot();
        try
        {
            var stager = new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(),
                new ArchiveRestoreStorageOptions(root));
            using var invalid = new MemoryStream("not-an-archive"u8.ToArray());

            await Assert.ThrowsAnyAsync<Exception>(() => stager.StageAsync(invalid));

            Assert.Empty(Directory.EnumerateFileSystemEntries(root));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(
        IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (T value in values) result.Add(value);
        return result;
    }

    private sealed class NonSeekableReadStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) =>
            inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    private sealed class TestSnapshot : IArchiveExportSnapshot
    {
        private TestSnapshot(
            DateTimeOffset generatedAt,
            ArchiveCatalog catalog,
            ArchivePortableSettings settings,
            GachaRecord gacha,
            GachaRevisionItem gachaRevision,
            ItemStateObservation item,
            ManualAcquisitionState manual,
            ManualAcquisitionRevisionItem manualRevision,
            ChallengeObservation challenge)
        {
            GeneratedAt = generatedAt;
            Catalog = catalog;
            Settings = settings;
            Gacha = gacha;
            GachaRevision = gachaRevision;
            Item = item;
            Manual = manual;
            ManualRevision = manualRevision;
            Challenge = challenge;
        }

        public DateTimeOffset GeneratedAt { get; }
        public ArchiveExportScope Scope { get; } = ArchiveExportScope.All();
        public ArchiveCatalog Catalog { get; }
        public ArchivePortableSettings Settings { get; }
        public GachaRecord Gacha { get; }
        public GachaRevisionItem GachaRevision { get; }
        public ItemStateObservation Item { get; }
        public ManualAcquisitionState Manual { get; }
        public ManualAcquisitionRevisionItem ManualRevision { get; }
        public ChallengeObservation Challenge { get; }

        public static TestSnapshot Create()
        {
            DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T18:30:00+08:00");
            Guid archiveId = Guid.Parse("10000000-0000-0000-0000-000000000001");
            Guid accountId = Guid.Parse("10000000-0000-0000-0000-000000000002");
            GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
                "100000001", GameServerRegion.ChinaOfficial);
            var catalog = new ArchiveCatalog(
                [new ArchiveCatalogRoleIdentity(
                    identity.Id.Value,
                    identity.Id.Value,
                    identity.NaturalIdentity)],
                [new ArchiveCatalogArchive(
                    archiveId,
                    "Main",
                    now,
                    now,
                    [new ArchiveCatalogAccount(
                        accountId,
                        identity.Id.Value,
                        "Traveler",
                        "100000001",
                        GameServerRegion.ChinaOfficial,
                        false,
                        null,
                        now,
                        now)])]);
            var official = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(FetchedAt: now));
            var gacha = new GachaRecord(accountId, "1001", "Furina", 5, now)
            {
                ItemId = "10000089",
                ItemType = "Character",
                GachaType = "301",
                UigfGachaType = "301",
                Count = 1,
                Provenance = official,
            };
            var gachaRevision = new GachaRevisionItem(
                Guid.Parse("10000000-0000-0000-0000-000000000003"),
                Guid.Parse("10000000-0000-0000-0000-000000000004"),
                new GachaFactReference(accountId, gacha.ExternalRecordId),
                EntityChangeKind.Update,
                new FactVersion(1),
                new FactVersion(2),
                gacha with { ItemName = "Old" },
                gacha,
                now);
            var item = new ItemStateObservation(
                new ItemObservationId(
                    Guid.Parse("10000000-0000-0000-0000-000000000005")),
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
                official);
            var user = new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: now));
            var acquisition = new ManualAcquisition(
                new ManualAcquisitionId(
                    Guid.Parse("10000000-0000-0000-0000-000000000006")),
                accountId,
                InventoryItemKind.Character,
                "10000089",
                OccurrenceTime.At(now, TimePrecision.Second),
                ManualAcquisitionReason.Other,
                "Fixture",
                user);
            var manual = new ManualAcquisitionState(
                acquisition,
                new FactVersion(2),
                archiveId);
            var manualRevision = new ManualAcquisitionRevisionItem(
                Guid.Parse("10000000-0000-0000-0000-000000000007"),
                Guid.Parse("10000000-0000-0000-0000-000000000008"),
                EntityChangeKind.Insert,
                null,
                new FactVersion(1),
                null,
                acquisition,
                now);
            var challenge = new ChallengeObservation(
                new ChallengeObservationId(
                    Guid.Parse("10000000-0000-0000-0000-000000000009")),
                accountId,
                ChallengeMode.SpiralAbyss,
                "2026-10-A",
                now.AddDays(-15),
                now,
                DataCompleteness.Complete,
                [],
                official);
            return new TestSnapshot(
                now,
                catalog,
                new ArchivePortableSettings(
                    true,
                    [new ArchivePortableArchiveSetting(archiveId, 3)]),
                gacha,
                gachaRevision,
                item,
                manual,
                manualRevision,
                challenge);
        }

        public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            One(Gacha, accountReference, cancellationToken);
        public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            One(GachaRevision, accountReference, cancellationToken);
        public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            One(Item, accountReference, cancellationToken);
        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            One(Manual, accountReference, cancellationToken);
        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            One(ManualRevision, accountReference, cancellationToken);
        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            One(Challenge, accountReference, cancellationToken);

        private async IAsyncEnumerable<T> One<T>(
            T value,
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (accountReference == Catalog.Archives.Single().Accounts.Single()
                    .AccountReference)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return value;
            }
            await Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

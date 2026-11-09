// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.IO.Compression;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Infrastructure.Archives.Portable;

namespace FurinaChronicle.Tests.Infrastructure.Archives.Portable;

public sealed class ArchivePackageCodecTests
{
    [Fact]
    public async Task EmptyArchiveAndUnresolvedAccountRoundTrip()
    {
        Guid archiveId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        Guid accountId = Guid.Parse("00000000-0000-0000-0000-000000000102");
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+00:00");
        var catalog = new ArchiveCatalog(
            [],
            [new ArchiveCatalogArchive(
                archiveId,
                " Archive ",
                now,
                now,
                [new ArchiveCatalogAccount(
                    accountId,
                    null,
                    "待修复账号",
                    "placeholder",
                    GameServerRegion.Unknown,
                    true,
                    "placeholder_account",
                    now,
                    now)])]);
        await using var snapshot = new FakeSnapshot(
            now,
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(archiveId, 3)]));
        using var stream = new MemoryStream();

        ArchiveWriteResult written = await new ArchivePackageWriter().WriteAsync(
            stream,
            snapshot,
            Generator());
        stream.Position = 0;
        ArchivePackageSummary read = await new ArchivePackageReader()
            .ReadAndValidateAsync(stream);

        Assert.Equal(" Archive ", read.Catalog.Archives.Single().Name);
        Assert.Equal("placeholder_account", read.Catalog.Archives.Single()
            .Accounts.Single().UnresolvedReason);
        Assert.Equal(0, read.GachaCurrentFactCount);
        Assert.Equal(0, read.GachaRevisionCount);
        Assert.True(read.IsComplete);
        Assert.Equal(64, written.PackageSha256.Length);
    }

    [Fact]
    public async Task StandardIncompleteAndRevisionAreCountedWithoutMerging()
    {
        Guid archiveId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        Guid accountId = Guid.Parse("00000000-0000-0000-0000-000000000202");
        GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
            "100000001",
            GameServerRegion.ChinaOfficial);
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+00:00");
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
        GachaRecord standard = Record(accountId, "1001", "10000001", now);
        GachaRecord incomplete = Record(accountId, "1002", null, now.AddMinutes(1));
        var revision = new GachaRevisionItem(
            Guid.Parse("00000000-0000-0000-0000-000000000203"),
            Guid.Parse("00000000-0000-0000-0000-000000000204"),
            new GachaFactReference(accountId, standard.ExternalRecordId),
            EntityChangeKind.Update,
            new FactVersion(1),
            new FactVersion(2),
            standard with { ItemName = "Old" },
            standard,
            now);
        var itemObservation = new ItemStateObservation(
            ItemObservationId.New(),
            accountId,
            InventoryItemKind.Character,
            DataCompleteness.Complete,
            [new InventoryStateEntry(
                InventoryItemKind.Character,
                "CHAR_10000089",
                null,
                1,
                90,
                2,
                InventoryFieldCoverage.Quantity |
                    InventoryFieldCoverage.Level |
                    InventoryFieldCoverage.Progression,
                DataCompleteness.Complete)],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: now)));
        var acquisition = new ManualAcquisition(
            ManualAcquisitionId.New(),
            accountId,
            InventoryItemKind.Character,
            "CHAR_10000089",
            OccurrenceTime.At(now, TimePrecision.Second),
            ManualAcquisitionReason.Other,
            "Fixture",
            new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: now)));
        var acquisitionState = new ManualAcquisitionState(
            acquisition,
            new FactVersion(2),
            archiveId);
        var previousAcquisition = new ManualAcquisition(
            acquisition.AcquisitionId,
            accountId,
            InventoryItemKind.Character,
            "CHAR_10000089",
            OccurrenceTime.At(now, TimePrecision.Second),
            ManualAcquisitionReason.Other,
            "Before",
            acquisition.Provenance);
        var acquisitionRevision = new ManualAcquisitionRevisionItem(
            Guid.Parse("00000000-0000-0000-0000-000000000205"),
            Guid.Parse("00000000-0000-0000-0000-000000000206"),
            EntityChangeKind.Update,
            new FactVersion(1),
            new FactVersion(2),
            previousAcquisition,
            acquisition,
            now);
        var challenge = new ChallengeObservation(
            ChallengeObservationId.New(),
            accountId,
            ChallengeMode.SpiralAbyss,
            "2026-10-A",
            now.AddDays(-15),
            now,
            DataCompleteness.Complete,
            [new SpiralAbyssFloorResult(
                12,
                DataCompleteness.Complete,
                [new SpiralAbyssChamberResult(
                    3,
                    3,
                    OccurrenceTime.At(now, TimePrecision.Second))])],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: now)));
        await using var snapshot = new FakeSnapshot(
            now,
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(archiveId, 3)]),
            new Dictionary<Guid, IReadOnlyList<GachaRecord>>
            {
                [accountId] = [standard, incomplete],
            },
            new Dictionary<Guid, IReadOnlyList<GachaRevisionItem>>
            {
                [accountId] = [revision],
            },
            new Dictionary<Guid, IReadOnlyList<ItemStateObservation>>
            {
                [accountId] = [itemObservation],
            },
            new Dictionary<Guid, IReadOnlyList<ManualAcquisitionState>>
            {
                [accountId] = [acquisitionState],
            },
            new Dictionary<Guid, IReadOnlyList<ManualAcquisitionRevisionItem>>
            {
                [accountId] = [acquisitionRevision],
            },
            new Dictionary<Guid, IReadOnlyList<ChallengeObservation>>
            {
                [accountId] = [challenge],
            });
        using var stream = new MemoryStream();

        await new ArchivePackageWriter().WriteAsync(stream, snapshot, Generator());
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read, true))
        {
            ZipArchiveEntry domainEntry = zip.Entries.Single(entry =>
                entry.FullName.EndsWith(
                    "/character-weapon-history.v1.json",
                    StringComparison.Ordinal));
            await using Stream payload = domainEntry.Open();
            using var reader = new StreamReader(payload, Encoding.UTF8);
            string json = await reader.ReadToEndAsync();
            Assert.DoesNotContain("change_set_id", json, StringComparison.Ordinal);
            Assert.Contains(
                acquisitionRevision.RevisionId.ToString("D"),
                json,
                StringComparison.OrdinalIgnoreCase);
        }
        stream.Position = 0;
        ArchivePackageSummary read = await new ArchivePackageReader()
            .ReadAndValidateAsync(stream);

        Assert.Equal(2, read.GachaCurrentFactCount);
        Assert.Equal(1, read.GachaRevisionCount);
        Assert.Equal(3, read.DomainFactCount);
        Assert.Equal(1, read.DomainRevisionCount);
        ArchiveDatasetDescriptor uigf = read.Manifest.Datasets.Single(item =>
            item.Format == "uigf");
        Assert.Equal(1, uigf.RecordCounts["current_facts"]);
    }

    [Fact]
    public async Task ChangedPayloadIsRejectedByChecksum()
    {
        await using FakeSnapshot snapshot = CreateEmptySnapshot();
        using var stream = new MemoryStream();
        await new ArchivePackageWriter().WriteAsync(stream, snapshot, Generator());
        stream.Position = 0;
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            ZipArchiveEntry entry = zip.GetEntry(ArchivePackageWriter.CatalogPath)!;
            await using Stream payload = entry.Open();
            int original = payload.ReadByte();
            payload.Position = 0;
            payload.WriteByte((byte)(original == (byte)'{' ? '[' : '{'));
        }
        stream.Position = 0;

        ArchivePackageException exception = await Assert.ThrowsAsync<ArchivePackageException>(
            () => new ArchivePackageReader().ReadAndValidateAsync(stream));

        Assert.Equal(ArchivePackageErrorCode.HashMismatch, exception.Code);
    }

    [Fact]
    public async Task TraversalPathIsRejectedBeforePayloadParsing()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            ZipArchiveEntry entry = zip.CreateEntry("../manifest.json");
            await using Stream payload = entry.Open();
            await payload.WriteAsync("{}"u8.ToArray());
        }
        stream.Position = 0;

        ArchivePackageException exception = await Assert.ThrowsAsync<ArchivePackageException>(
            () => new ArchivePackageReader().ReadAndValidateAsync(stream));

        Assert.Equal(ArchivePackageErrorCode.InvalidPath, exception.Code);
    }

    [Fact]
    public async Task WriterEnforcesCatalogResourceLimit()
    {
        await using FakeSnapshot snapshot = CreateEmptySnapshot();
        using var stream = new MemoryStream();
        var writer = new ArchivePackageWriter(new ArchivePortableLimits
        {
            MaximumArchiveCount = 0,
        });

        ArchivePackageException exception = await Assert.ThrowsAsync<ArchivePackageException>(
            () => writer.WriteAsync(stream, snapshot, Generator()));

        Assert.Equal(ArchivePackageErrorCode.ResourceLimitExceeded, exception.Code);
    }

    [Fact]
    public async Task StreamingUigfValidationCrossesBufferBoundaries()
    {
        Guid archiveId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        Guid accountId = Guid.Parse("00000000-0000-0000-0000-000000000402");
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+00:00");
        GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
            "100000001",
            GameServerRegion.ChinaOfficial);
        var catalog = new ArchiveCatalog(
            [new ArchiveCatalogRoleIdentity(
                identity.Id.Value,
                identity.Id.Value,
                identity.NaturalIdentity)],
            [new ArchiveCatalogArchive(
                archiveId,
                "Large",
                now,
                now,
                [new ArchiveCatalogAccount(
                    accountId,
                    identity.Id.Value,
                    null,
                    "100000001",
                    GameServerRegion.ChinaOfficial,
                    false,
                    null,
                    now,
                    now)])]);
        GachaRecord[] records = Enumerable.Range(1, 2_000)
            .Select(index => Record(
                accountId,
                index.ToString("D19"),
                "10000001",
                now.AddSeconds(index)))
            .ToArray();
        await using var snapshot = new FakeSnapshot(
            now,
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(archiveId, 3)]),
            new Dictionary<Guid, IReadOnlyList<GachaRecord>>
            {
                [accountId] = records,
            });
        using var stream = new MemoryStream();

        await new ArchivePackageWriter().WriteAsync(stream, snapshot, Generator());
        stream.Position = 0;
        ArchivePackageSummary read = await new ArchivePackageReader()
            .ReadAndValidateAsync(stream);

        Assert.Equal(2_000, read.GachaCurrentFactCount);
    }

    [Fact]
    public async Task NonstandardFactWithConflictingAccountReferenceIsRejected()
    {
        Guid archiveId = Guid.Parse("00000000-0000-0000-0000-000000000501");
        Guid accountId = Guid.Parse("00000000-0000-0000-0000-000000000502");
        Guid conflictingAccountId =
            Guid.Parse("00000000-0000-0000-0000-000000000503");
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+00:00");
        GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
            "100000001",
            GameServerRegion.ChinaOfficial);
        var catalog = new ArchiveCatalog(
            [new ArchiveCatalogRoleIdentity(
                identity.Id.Value,
                identity.Id.Value,
                identity.NaturalIdentity)],
            [new ArchiveCatalogArchive(
                archiveId,
                "Reference fixture",
                now,
                now,
                [new ArchiveCatalogAccount(
                    accountId,
                    identity.Id.Value,
                    null,
                    "100000001",
                    GameServerRegion.ChinaOfficial,
                    false,
                    null,
                    now,
                    now)])]);
        GachaRecord conflicting = Record(
            conflictingAccountId,
            "5001",
            itemId: null,
            now);
        await using var snapshot = new FakeSnapshot(
            now,
            catalog,
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(archiveId, 3)]),
            new Dictionary<Guid, IReadOnlyList<GachaRecord>>
            {
                [accountId] = [conflicting],
            });
        using var stream = new MemoryStream();

        await new ArchivePackageWriter().WriteAsync(stream, snapshot, Generator());
        stream.Position = 0;
        ArchivePackageException exception = await Assert.ThrowsAsync<ArchivePackageException>(
            () => new ArchivePackageReader().ReadAndValidateAsync(stream));

        Assert.Equal(ArchivePackageErrorCode.InvalidReference, exception.Code);
    }

    private static ArchiveGeneratorInfo Generator() =>
        new("FurinaChronicle", "0.8.10.174", "8C.1", "0000000", "test");

    private static GachaRecord Record(
        Guid accountId,
        string id,
        string? itemId,
        DateTimeOffset time) =>
        new(accountId, id, "Furina", 5, time)
        {
            ItemId = itemId,
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
        };

    private static FakeSnapshot CreateEmptySnapshot()
    {
        Guid archiveId = Guid.Parse("00000000-0000-0000-0000-000000000301");
        DateTimeOffset now = DateTimeOffset.Parse("2026-10-23T12:00:00+00:00");
        return new FakeSnapshot(
            now,
            new ArchiveCatalog(
                [],
                [new ArchiveCatalogArchive(archiveId, "Empty", now, now, [])]),
            new ArchivePortableSettings(
                true,
                [new ArchivePortableArchiveSetting(archiveId, 3)]));
    }

    private sealed class FakeSnapshot(
        DateTimeOffset generatedAt,
        ArchiveCatalog catalog,
        ArchivePortableSettings settings,
        IReadOnlyDictionary<Guid, IReadOnlyList<GachaRecord>>? records = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<GachaRevisionItem>>? revisions = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<ItemStateObservation>>? items = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<ManualAcquisitionState>>?
            acquisitions = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<ManualAcquisitionRevisionItem>>?
            acquisitionRevisions = null,
        IReadOnlyDictionary<Guid, IReadOnlyList<ChallengeObservation>>? challenges = null)
        : IArchiveExportSnapshot
    {
        public DateTimeOffset GeneratedAt { get; } = generatedAt;
        public ArchiveExportScope Scope { get; } = ArchiveExportScope.All();
        public ArchiveCatalog Catalog { get; } = catalog;
        public ArchivePortableSettings Settings { get; } = settings;

        public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate(records, accountReference, cancellationToken);

        public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate(revisions, accountReference, cancellationToken);

        public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate(items, accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate(acquisitions, accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            Enumerate(
                acquisitionRevisions,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate(challenges, accountReference, cancellationToken);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async IAsyncEnumerable<T> Enumerate<T>(
            IReadOnlyDictionary<Guid, IReadOnlyList<T>>? values,
            Guid accountReference,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (values is not null && values.TryGetValue(accountReference, out var items))
            {
                foreach (T item in items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return item;
                }
            }
            await Task.CompletedTask;
        }
    }
}

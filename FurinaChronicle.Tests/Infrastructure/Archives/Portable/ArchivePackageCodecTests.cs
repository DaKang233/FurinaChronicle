// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.IO.Compression;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
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
            });
        using var stream = new MemoryStream();

        await new ArchivePackageWriter().WriteAsync(stream, snapshot, Generator());
        stream.Position = 0;
        ArchivePackageSummary read = await new ArchivePackageReader()
            .ReadAndValidateAsync(stream);

        Assert.Equal(2, read.GachaCurrentFactCount);
        Assert.Equal(1, read.GachaRevisionCount);
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
        IReadOnlyDictionary<Guid, IReadOnlyList<GachaRevisionItem>>? revisions = null)
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
            Enumerate<ItemStateObservation>(null, accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate<ManualAcquisitionState>(null, accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            Enumerate<ManualAcquisitionRevisionItem>(
                null,
                accountReference,
                cancellationToken);

        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            Enumerate<ChallengeObservation>(null, accountReference, cancellationToken);

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

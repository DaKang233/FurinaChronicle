// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

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
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class ArchiveSemanticComparerTests
{
    [Fact]
    public async Task EquivalentLogicalArchivesIgnoreOnlyLocalReferencesAndExportTime()
    {
        await using MemoryArchiveRestoreDataSource expected = Create(remapped: false);
        await using MemoryArchiveRestoreDataSource actual = Create(remapped: true);

        ArchiveSemanticComparison comparison =
            await new ArchiveSemanticComparer().CompareAsync(expected, actual);

        Assert.True(comparison.IsEquivalent);
        Assert.Empty(comparison.Differences);
        Assert.Equal(comparison.ExpectedDigest, comparison.ActualDigest);
    }

    [Fact]
    public async Task DifferentContainerHashesCanStillBeSemanticallyEquivalent()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        try
        {
            await using MemoryArchiveRestoreDataSource first = Create(remapped: false);
            await using MemoryArchiveRestoreDataSource second = Create(remapped: true);
            using var firstPackage = new MemoryStream();
            using var secondPackage = new MemoryStream();
            var writer = new ArchivePackageWriter();
            ArchiveWriteResult firstWrite = await writer.WriteAsync(
                firstPackage,
                first,
                new ArchiveGeneratorInfo(
                    "FurinaChronicle",
                    "0.8.15.200",
                    "8C.6",
                    "aaaaaaa",
                    "windows"));
            ArchiveWriteResult secondWrite = await writer.WriteAsync(
                secondPackage,
                second,
                new ArchiveGeneratorInfo(
                    "FurinaChronicle",
                    "0.8.15.201",
                    "8C.6",
                    "bbbbbbb",
                    "android"));
            Assert.NotEqual(
                firstWrite.PackageSha256,
                secondWrite.PackageSha256);

            var stager = new SqliteArchiveRestoreInputStager(
                new ArchivePackageReader(),
                new ArchiveRestoreStorageOptions(root));
            firstPackage.Position = 0;
            secondPackage.Position = 0;
            await using IArchiveRestoreStagedPackage stagedFirst =
                await stager.StageAsync(firstPackage);
            await using IArchiveRestoreStagedPackage stagedSecond =
                await stager.StageAsync(secondPackage);

            ArchiveSemanticComparison comparison =
                await new ArchiveSemanticComparer().CompareAsync(
                    stagedFirst,
                    stagedSecond);

            Assert.True(comparison.IsEquivalent);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReceivingSideOnlyRevisionsDoNotChangePortableSemantics()
    {
        await using MemoryArchiveRestoreDataSource expected =
            Create(remapped: false);
        await using MemoryArchiveRestoreDataSource actual =
            Create(remapped: true);
        (Guid accountId, List<GachaRecord> records) = actual.GachaRecords
            .Single(value => value.Value.Any(record =>
                record.ExternalRecordId == "8000000000000000001"));
        GachaRecord current = Assert.Single(records);
        actual.GachaRevisions[accountId].Add(new GachaRevisionItem(
            Id(0x0701),
            Id(0x0702),
            new GachaFactReference(accountId, current.ExternalRecordId),
            EntityChangeKind.Insert,
            null,
            new FactVersion(3),
            null,
            current,
            new DateTimeOffset(
                2026,
                10,
                25,
                12,
                0,
                0,
                TimeSpan.FromHours(8))));

        ArchiveSemanticComparison comparison =
            await new ArchiveSemanticComparer().CompareAsync(expected, actual);

        Assert.True(comparison.IsEquivalent);
        Assert.Empty(comparison.Differences);
    }

    [Theory]
    [InlineData(Mutation.MissingGachaRevision, "gacha_revision")]
    [InlineData(Mutation.ChangedTimePrecision, "manual_acquisition")]
    [InlineData(Mutation.MergedIndependentUidCopy, "/account/")]
    [InlineData(Mutation.DeletedEmptyArchive, "/parent")]
    public async Task RequiredSemanticMutationsFail(
        Mutation mutation,
        string expectedPathFragment)
    {
        await using MemoryArchiveRestoreDataSource expected = Create(remapped: false);
        await using MemoryArchiveRestoreDataSource actual =
            Create(remapped: true, mutation);

        ArchiveSemanticComparison comparison =
            await new ArchiveSemanticComparer().CompareAsync(expected, actual);

        Assert.False(comparison.IsEquivalent);
        Assert.NotEqual(comparison.ExpectedDigest, comparison.ActualDigest);
        Assert.Contains(
            comparison.Differences,
            difference => difference.Path.Contains(
                expectedPathFragment,
                StringComparison.Ordinal));
    }

    private static MemoryArchiveRestoreDataSource Create(
        bool remapped,
        Mutation mutation = Mutation.None)
    {
        DateTimeOffset created =
            new(2026, 10, 1, 8, 0, 0, TimeSpan.FromHours(8));
        DateTimeOffset observed =
            new(2026, 10, 20, 20, 30, 0, TimeSpan.FromHours(8));
        Guid mainArchive = Id(remapped ? 0x8101 : 0x0101);
        Guid copyArchive = Id(remapped ? 0x8102 : 0x0102);
        Guid emptyArchive = Id(remapped ? 0x8103 : 0x0103);
        Guid unresolvedArchive = Id(remapped ? 0x8104 : 0x0104);
        Guid mainAccount = Id(remapped ? 0x8201 : 0x0201);
        Guid copyAccount = Id(remapped ? 0x8202 : 0x0202);
        Guid emptyAccount = Id(remapped ? 0x8203 : 0x0203);
        Guid unresolvedAccount = Id(remapped ? 0x8204 : 0x0204);

        GameRoleIdentity shared = GenshinGameRoleIdentity.CreateIdentity(
            "800000001",
            GameServerRegion.Asia);
        GameRoleIdentity other = GenshinGameRoleIdentity.CreateIdentity(
            "800000002",
            GameServerRegion.Asia);
        ArchiveCatalogRoleIdentity sharedCatalog = Identity(shared);
        ArchiveCatalogRoleIdentity otherCatalog = Identity(other);

        var archives = new List<ArchiveCatalogArchive>
        {
            new(
                mainArchive,
                "主档案",
                created,
                created.AddDays(20),
                [
                    Account(mainAccount, shared, "旅行者", created),
                    Account(emptyAccount, other, "空账号", created),
                ]),
            new(
                copyArchive,
                "独立副本",
                created.AddMinutes(1),
                created.AddDays(20),
                mutation == Mutation.MergedIndependentUidCopy
                    ? []
                    : [Account(copyAccount, shared, "旅行者副本", created)]),
            new(
                emptyArchive,
                "空档案",
                created.AddMinutes(2),
                created.AddMinutes(2),
                []),
            new(
                unresolvedArchive,
                "待修复",
                created.AddMinutes(3),
                created.AddDays(1),
                [new ArchiveCatalogAccount(
                    unresolvedAccount,
                    null,
                    "未解析账号",
                    "placeholder",
                    GameServerRegion.Unknown,
                    true,
                    "placeholder_account",
                    created,
                    created.AddDays(1))]),
        };
        if (mutation == Mutation.DeletedEmptyArchive)
        {
            archives.RemoveAll(value => value.ArchiveReference == emptyArchive);
        }

        var settings = new List<ArchivePortableArchiveSetting>
        {
            new(mainArchive, 3),
            new(copyArchive, 5),
            new(emptyArchive, 0),
            new(unresolvedArchive, 9),
        };
        if (mutation == Mutation.DeletedEmptyArchive)
        {
            settings.RemoveAll(value => value.ArchiveReference == emptyArchive);
        }

        var source = new MemoryArchiveRestoreDataSource(
            new ArchiveCatalog(
                [sharedCatalog, otherCatalog],
                archives),
            new ArchivePortableSettings(true, settings),
            generatedAt: remapped
                ? created.AddYears(1)
                : created);

        GachaRecord current = Gacha(
            mainAccount,
            "8000000000000000001",
            "芙宁娜",
            observed,
            "10000089");
        source.GachaRecords[mainAccount] = [current];
        source.GachaRevisions[mainAccount] =
        [
            new GachaRevisionItem(
                Id(0x0301),
                Id(remapped ? 0x8301 : 0x0302),
                new GachaFactReference(
                    mainAccount,
                    current.ExternalRecordId),
                EntityChangeKind.Update,
                new FactVersion(1),
                new FactVersion(2),
                current with { ItemName = "旧名称" },
                current,
                observed.AddMinutes(1)),
        ];
        if (mutation == Mutation.MissingGachaRevision)
        {
            source.GachaRevisions[mainAccount].Clear();
        }

        source.GachaRecords[copyAccount] =
        [
            Gacha(
                copyAccount,
                "8000000000000000002",
                "那维莱特",
                observed.AddMinutes(2),
                "10000087"),
        ];
        source.GachaRecords[unresolvedAccount] =
        [
            new GachaRecord(
                unresolvedAccount,
                "incomplete-0001",
                "历史未知物品",
                5,
                observed.AddMinutes(3))
            {
                GachaType = "301",
                Provenance = new RecordProvenance(
                    DataOrigin.FurinaImport,
                    new RecordTimestamps(ImportedAt: observed.AddMinutes(4))),
            },
        ];

        source.ItemObservations[mainAccount] =
        [
            Observation(mainAccount, 0x0401, 1, observed),
            Observation(mainAccount, 0x0402, 2, observed.AddHours(1)),
            Observation(mainAccount, 0x0403, 1, observed.AddHours(2)),
        ];

        OccurrenceTime acquisitionTime = OccurrenceTime.Within(
            new DateTimeOffset(
                2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(8)),
            new DateTimeOffset(
                2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(8)),
            mutation == Mutation.ChangedTimePrecision
                ? TimePrecision.Day
                : TimePrecision.Week);
        var acquisition = new ManualAcquisition(
            new ManualAcquisitionId(Id(0x0501)),
            mainAccount,
            InventoryItemKind.Character,
            "CHAR_10000089",
            acquisitionTime,
            ManualAcquisitionReason.EventSelection,
            "合成活动",
            new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: observed)));
        source.ManualAcquisitions[mainAccount] =
        [
            new ManualAcquisitionState(
                acquisition,
                new FactVersion(remapped ? 99 : 2),
                mainArchive),
        ];
        source.ManualRevisions[mainAccount] =
        [
            new ManualAcquisitionRevisionItem(
                Id(0x0502),
                Id(remapped ? 0x8501 : 0x0503),
                EntityChangeKind.Update,
                new FactVersion(1),
                new FactVersion(2),
                new ManualAcquisition(
                    acquisition.AcquisitionId,
                    acquisition.GameAccountId,
                    acquisition.Kind,
                    acquisition.TypeId,
                    acquisition.OccurredAt,
                    acquisition.Reason,
                    "修订前",
                    acquisition.Provenance),
                acquisition,
                observed.AddMinutes(5)),
        ];
        source.ChallengeObservations[mainAccount] =
        [
            new ChallengeObservation(
                new ChallengeObservationId(Id(0x0601)),
                mainAccount,
                ChallengeMode.SpiralAbyss,
                "2026-10-B",
                observed.AddDays(-15),
                observed.AddDays(1),
                DataCompleteness.Partial,
                [new SpiralAbyssFloorResult(
                    12,
                    DataCompleteness.Partial,
                    [new SpiralAbyssChamberResult(
                        1,
                        3,
                        OccurrenceTime.Unknown)])],
                new RecordProvenance(
                    DataOrigin.OfficialApi,
                    new RecordTimestamps(
                        ObservedAt: observed,
                        FetchedAt: observed.AddSeconds(2)),
                    new DataSourceReference(
                        "miyoushe",
                        sourceSnapshotId: "synthetic-challenge"))),
        ];
        return source;
    }

    private static ArchiveCatalogRoleIdentity Identity(
        GameRoleIdentity identity) => new(
        identity.Id.Value,
        identity.Id.Value,
        identity.NaturalIdentity);

    private static ArchiveCatalogAccount Account(
        Guid reference,
        GameRoleIdentity identity,
        string displayName,
        DateTimeOffset created) => new(
        reference,
        identity.Id.Value,
        displayName,
        identity.NaturalIdentity.Uid,
        GameServerRegion.Asia,
        false,
        null,
        created,
        created.AddDays(20));

    private static GachaRecord Gacha(
        Guid account,
        string externalId,
        string name,
        DateTimeOffset time,
        string itemId) => new(
        account,
        externalId,
        name,
        5,
        time)
    {
        ItemId = itemId,
        ItemType = "Character",
        GachaType = "301",
        UigfGachaType = "301",
        Provenance = new RecordProvenance(
            DataOrigin.StandardImport,
            new RecordTimestamps(ImportedAt: time.AddMinutes(1)),
            new DataSourceReference(
                "uigf",
                sourceRecordId: externalId)),
    };

    private static ItemStateObservation Observation(
        Guid account,
        int id,
        int progression,
        DateTimeOffset observedAt) => new(
        new ItemObservationId(Id(id)),
        account,
        InventoryItemKind.Character,
        DataCompleteness.Complete,
        [new InventoryStateEntry(
            InventoryItemKind.Character,
            "CHAR_10000089",
            null,
            1,
            90,
            progression,
            InventoryFieldCoverage.Quantity |
                InventoryFieldCoverage.Level |
                InventoryFieldCoverage.Progression,
            DataCompleteness.Complete)],
        new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(
                ObservedAt: observedAt,
                FetchedAt: observedAt.AddSeconds(1)),
            new DataSourceReference(
                "miyoushe",
                sourceSnapshotId: $"synthetic-item-{id}")));

    private static Guid Id(int suffix) =>
        Guid.Parse($"00000000-0000-0000-0000-{suffix:X12}");

    public enum Mutation
    {
        None = 0,
        MissingGachaRevision = 1,
        ChangedTimePrecision = 2,
        MergedIndependentUidCopy = 3,
        DeletedEmptyArchive = 4,
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.HistoryDomains;

internal sealed class SyntheticArchiveSnapshot(
    ArchiveCatalog catalog,
    ArchivePortableSettings settings,
    DateTimeOffset generatedAt) : IArchiveExportSnapshot
{
    private readonly Dictionary<Guid, List<GachaRecord>> gacha = [];
    private readonly Dictionary<Guid, List<GachaRevisionItem>> gachaRevisions = [];
    private readonly Dictionary<Guid, List<ItemStateObservation>> observations = [];
    private readonly Dictionary<Guid, List<ManualAcquisitionState>> acquisitions = [];
    private readonly Dictionary<Guid, List<ManualAcquisitionRevisionItem>>
        acquisitionRevisions = [];
    private readonly Dictionary<Guid, List<ChallengeObservation>> challenges = [];

    public DateTimeOffset GeneratedAt { get; } = generatedAt;

    public ArchiveExportScope Scope { get; } = ArchiveExportScope.All();

    public ArchiveCatalog Catalog { get; } = catalog;

    public ArchivePortableSettings Settings { get; } = settings;

    public static SyntheticArchiveSnapshot Create(string origin)
    {
        bool android = origin == "android";
        int baseId = android ? 0x100000 : 0;
        DateTimeOffset created = new(
            2026,
            10,
            android ? 2 : 1,
            8,
            0,
            0,
            TimeSpan.FromHours(8));
        DateTimeOffset observed = new(
            2026,
            10,
            android ? 22 : 20,
            20,
            30,
            0,
            TimeSpan.FromHours(8));
        Guid mainArchive = Id(baseId + 0x0101);
        Guid copyArchive = Id(baseId + 0x0102);
        Guid emptyArchive = Id(baseId + 0x0103);
        Guid unresolvedArchive = Id(baseId + 0x0104);
        Guid mainAccount = Id(baseId + 0x0201);
        Guid copyAccount = Id(baseId + 0x0202);
        Guid emptyAccount = Id(baseId + 0x0203);
        Guid unresolvedAccount = Id(baseId + 0x0204);
        string uid = android ? "800000101" : "800000001";
        string otherUid = android ? "800000102" : "800000002";
        GameRoleIdentity shared = GenshinGameRoleIdentity.CreateIdentity(
            uid,
            GameServerRegion.Asia);
        GameRoleIdentity other = GenshinGameRoleIdentity.CreateIdentity(
            otherUid,
            GameServerRegion.Asia);

        var snapshot = new SyntheticArchiveSnapshot(
            new ArchiveCatalog(
                [Identity(shared), Identity(other)],
                [
                    new ArchiveCatalogArchive(
                        mainArchive,
                        android ? "Android 主档案" : "Windows 主档案",
                        created,
                        created.AddDays(20),
                        [
                            Account(
                                mainAccount,
                                shared,
                                android ? "Android 旅行者" : "Windows 旅行者",
                                created),
                            Account(emptyAccount, other, "空账号", created),
                        ]),
                    new ArchiveCatalogArchive(
                        copyArchive,
                        "同 UID 独立副本",
                        created.AddMinutes(1),
                        created.AddDays(20),
                        [Account(copyAccount, shared, "旅行者副本", created)]),
                    new ArchiveCatalogArchive(
                        emptyArchive,
                        "空档案",
                        created.AddMinutes(2),
                        created.AddMinutes(2),
                        []),
                    new ArchiveCatalogArchive(
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
                            "phase8c6_synthetic_unresolved",
                            created,
                            created.AddDays(1))]),
                ]),
            new ArchivePortableSettings(
                true,
                [
                    new ArchivePortableArchiveSetting(mainArchive, 3),
                    new ArchivePortableArchiveSetting(copyArchive, 5),
                    new ArchivePortableArchiveSetting(emptyArchive, 0),
                    new ArchivePortableArchiveSetting(unresolvedArchive, 9),
                ]),
            created);

        string firstExternalId = android
            ? "8100000000000000001"
            : "8000000000000000001";
        string secondExternalId = android
            ? "8100000000000000002"
            : "8000000000000000002";
        GachaRecord current = Gacha(
            mainAccount,
            firstExternalId,
            android ? "纳西妲" : "芙宁娜",
            observed,
            android ? "10000073" : "10000089");
        snapshot.gacha[mainAccount] = [current];
        snapshot.gachaRevisions[mainAccount] =
        [
            new GachaRevisionItem(
                Id(baseId + 0x0301),
                Id(baseId + 0x0302),
                new GachaFactReference(mainAccount, firstExternalId),
                EntityChangeKind.Update,
                new FactVersion(1),
                new FactVersion(2),
                current with { ItemName = "修订前名称" },
                current,
                observed.AddMinutes(1)),
        ];
        snapshot.gacha[copyAccount] =
        [
            Gacha(
                copyAccount,
                secondExternalId,
                android ? "钟离" : "那维莱特",
                observed.AddMinutes(2),
                android ? "10000030" : "10000087"),
        ];
        snapshot.gacha[unresolvedAccount] =
        [
            new GachaRecord(
                unresolvedAccount,
                $"incomplete-{origin}-0001",
                "历史未知物品",
                5,
                observed.AddMinutes(3))
            {
                GachaType = "301",
                Provenance = new RecordProvenance(
                    DataOrigin.FurinaImport,
                    new RecordTimestamps(
                        ImportedAt: observed.AddMinutes(4))),
            },
        ];

        snapshot.observations[mainAccount] =
        [
            Observation(mainAccount, baseId + 0x0401, 1, observed),
            Observation(
                mainAccount,
                baseId + 0x0402,
                2,
                observed.AddHours(1)),
            Observation(
                mainAccount,
                baseId + 0x0403,
                1,
                observed.AddHours(2)),
        ];

        OccurrenceTime acquisitionTime = OccurrenceTime.Within(
            new DateTimeOffset(
                2026,
                10,
                19,
                0,
                0,
                0,
                TimeSpan.FromHours(8)),
            new DateTimeOffset(
                2026,
                10,
                26,
                0,
                0,
                0,
                TimeSpan.FromHours(8)),
            TimePrecision.Week);
        var acquisition = new ManualAcquisition(
            new ManualAcquisitionId(Id(baseId + 0x0501)),
            mainAccount,
            InventoryItemKind.Character,
            android ? "CHAR_ANDROID_SYNTHETIC" : "CHAR_10000089",
            acquisitionTime,
            ManualAcquisitionReason.EventSelection,
            "Phase 8C.6 合成活动",
            new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: observed)));
        snapshot.acquisitions[mainAccount] =
        [
            new ManualAcquisitionState(
                acquisition,
                new FactVersion(2),
                mainArchive),
        ];
        snapshot.acquisitionRevisions[mainAccount] =
        [
            new ManualAcquisitionRevisionItem(
                Id(baseId + 0x0502),
                Id(baseId + 0x0503),
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
        snapshot.challenges[mainAccount] =
        [
            new ChallengeObservation(
                new ChallengeObservationId(Id(baseId + 0x0601)),
                mainAccount,
                ChallengeMode.SpiralAbyss,
                android ? "2026-10-Android" : "2026-10-Windows",
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
                        "phase8c6-synthetic",
                        sourceSnapshotId: $"challenge-{origin}"))),
        ];
        return snapshot;
    }

    public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(gacha, accountReference, cancellationToken);

    public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(gachaRevisions, accountReference, cancellationToken);

    public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(observations, accountReference, cancellationToken);

    public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(acquisitions, accountReference, cancellationToken);

    public IAsyncEnumerable<ManualAcquisitionRevisionItem>
        ReadManualAcquisitionRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
        ReadAsync(acquisitionRevisions, accountReference, cancellationToken);

    public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
        Guid accountReference,
        CancellationToken cancellationToken = default) =>
        ReadAsync(challenges, accountReference, cancellationToken);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static async IAsyncEnumerable<T> ReadAsync<T>(
        IReadOnlyDictionary<Guid, List<T>> source,
        Guid accountReference,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (T value in source.GetValueOrDefault(accountReference) ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return value;
            await Task.Yield();
        }
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
            "CHAR_PHASE8C6",
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
                "phase8c6-synthetic",
                sourceSnapshotId: $"item-{id}")));

    private static Guid Id(int suffix) =>
        Guid.Parse($"00000000-0000-0000-0000-{suffix:X12}");
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteArchiveDomainHistoryStoreTests
{
    [Fact]
    public async Task ItemObservations_RoundTripAcrossDatabaseReopen()
    {
        string directory = CreateTemporaryDirectory();
        string path = Path.Combine(directory, "history.db3");
        Guid accountId;
        ItemStateObservation character;
        ItemStateObservation weapon;
        try
        {
            await using (var database = new FurinaDatabase(
                new SqliteDatabaseOptions(path)))
            {
                (Guid _, accountId) = await AddArchiveAndAccountAsync(database);
                var store = new SqliteGachaAtomicChangeStore(database);
                DateTimeOffset observed = new(
                    2026, 10, 18, 8, 30, 0, TimeSpan.FromHours(8));
                RecordProvenance provenance = new(
                    DataOrigin.OfficialApi,
                    new RecordTimestamps(
                        ObservedAt: observed,
                        FetchedAt: observed.AddSeconds(2)),
                    new DataSourceReference(
                        "miyoushe",
                        sourceSnapshotId: "snapshot-1"),
                    AcquisitionBatchId.New());
                character = new ItemStateObservation(
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
                    provenance);
                weapon = new ItemStateObservation(
                    ItemObservationId.New(),
                    accountId,
                    InventoryItemKind.Weapon,
                    DataCompleteness.Partial,
                    [new InventoryStateEntry(
                        InventoryItemKind.Weapon,
                        "WEAPON_15514",
                        instanceId: null,
                        quantity: 2,
                        level: null,
                        progression: null,
                        InventoryFieldCoverage.Quantity,
                        DataCompleteness.Partial)],
                    new RecordProvenance(
                        provenance.Origin,
                        new RecordTimestamps(
                            ObservedAt: observed.AddDays(1)),
                        provenance.Source,
                        provenance.AcquisitionBatchId));
                ArchiveOperationResult result =
                    await store.CommitItemObservationsAsync(
                        ItemRequest([character, weapon]));
                Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
            }

            await using var reopened = new FurinaDatabase(
                new SqliteDatabaseOptions(path));
            var reopenedStore = new SqliteGachaAtomicChangeStore(reopened);
            IReadOnlyList<ItemStateObservation> observations =
                await reopenedStore.GetItemObservationsAsync(accountId);

            Assert.Equal(2, observations.Count);
            Assert.Equal(
                character.Provenance,
                observations.Single(item =>
                    item.ObservationId == character.ObservationId).Provenance);
            InventoryStateEntry weaponEntry = Assert.Single(
                observations.Single(item =>
                    item.ObservationId == weapon.ObservationId).Entries);
            Assert.Null(weaponEntry.InstanceId);
            Assert.Equal(2, weaponEntry.Quantity);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ImmutableObservation_SameContentNoOp_DifferentContentConflicts()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        ItemObservationId id = ItemObservationId.New();
        ItemStateObservation original = CharacterObservation(
            id,
            accountId,
            level: 80);

        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitItemObservationsAsync(
                ItemRequest([original]))).Status);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await context.AtomicGacha.CommitItemObservationsAsync(
                ItemRequest([original]))).Status);
        ArchiveOperationResult conflict =
            await context.AtomicGacha.CommitItemObservationsAsync(
                ItemRequest([
                    CharacterObservation(id, accountId, level: 90)
                ]));

        Assert.Equal(ChangeExecutionStatus.Conflict, conflict.Status);
        ItemStateObservation saved = Assert.Single(
            await context.AtomicGacha.GetItemObservationsAsync(accountId));
        Assert.Equal(80, Assert.Single(saved.Entries).Level);
    }

    [Fact]
    public async Task ManualCorrectionAndDelete_AreRevisionedAndUndoable()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        ManualAcquisition original = Acquisition(accountId, "original");
        ArchiveOperationResult inserted =
            await context.AtomicGacha.CommitManualAcquisitionsAsync(
                ManualRequest([
                    new ManualAcquisitionMutation(
                        original.AcquisitionId,
                        accountId,
                        original)
                ], DataChangeOperationKind.Import));
        ManualAcquisitionState version1 = Assert.Single(
            await context.AtomicGacha.GetManualAcquisitionsAsync(accountId));
        var corrected = new ManualAcquisition(
            original.AcquisitionId,
            original.GameAccountId,
            original.Kind,
            original.TypeId,
            original.OccurredAt,
            original.Reason,
            "corrected",
            original.Provenance);
        ArchiveOperationResult updated =
            await context.AtomicGacha.CommitManualAcquisitionsAsync(
                ManualRequest([
                    new ManualAcquisitionMutation(
                        original.AcquisitionId,
                        accountId,
                        corrected,
                        version1.Version)
                ], DataChangeOperationKind.Correction));
        IArchiveOperationHistoryStore history = context.AtomicGacha;
        DateTimeOffset now = TestTime().AddMinutes(2);

        ArchiveOperationResult undone = await history.UndoLatestAsync(
            new ArchiveUndoRequest(
                OperationId.New(),
                version1.ArchiveId,
                now,
                now.AddSeconds(1)));

        Assert.Equal(ChangeExecutionStatus.Applied, inserted.Status);
        Assert.Equal(ChangeExecutionStatus.Applied, updated.Status);
        Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
        ManualAcquisitionState restored = Assert.Single(
            await context.AtomicGacha.GetManualAcquisitionsAsync(accountId));
        Assert.Equal("original", restored.Acquisition.ReasonDetail);
        Assert.Equal(3, restored.Version.Value);
        Assert.Single(
            await context.AtomicGacha.GetDomainRevisionsAsync(
                updated.ChangeSetId!.Value));

        ArchiveOperationResult deleted =
            await context.AtomicGacha.CommitManualAcquisitionsAsync(
                ManualRequest([
                    new ManualAcquisitionMutation(
                        original.AcquisitionId,
                        accountId,
                        Proposed: null,
                        restored.Version)
                ], DataChangeOperationKind.Delete));
        Assert.Equal(ChangeExecutionStatus.Applied, deleted.Status);
        Assert.Empty(
            await context.AtomicGacha.GetManualAcquisitionsAsync(accountId));
    }

    [Fact]
    public async Task Challenge_AtoBtoAWithDifferentIdsPreservesEveryObservation()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        ChallengeObservation a1 = Challenge(accountId, stars: 3, sequence: 1);
        ChallengeObservation b = Challenge(accountId, stars: 6, sequence: 2);
        ChallengeObservation a2 = Challenge(accountId, stars: 3, sequence: 3);

        foreach (ChallengeObservation observation in new[] { a1, b, a2 })
        {
            Assert.Equal(
                ChangeExecutionStatus.Applied,
                (await context.AtomicGacha.CommitChallengeObservationsAsync(
                    ChallengeRequest([observation]))).Status);
        }

        IReadOnlyList<ChallengeObservation> history =
            await context.AtomicGacha.GetChallengeObservationsAsync(accountId);
        Assert.Equal(3, history.Count);
        Assert.Equal([3, 6, 3], history.Select(item =>
            item.SpiralAbyssFloors.Sum(floor => floor.Stars)));
    }

    [Fact]
    public async Task SharedUndo_UsesLatestOperationAcrossGachaManualAndChallenge()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GameAccount account = Assert.IsType<GameAccount>(
            await context.Accounts.GetByIdAsync(accountId));
        var gacha = new GachaRecord(
            accountId,
            "cross-gacha",
            "Furina",
            5,
            new DateTimeOffset(
                2026, 10, 18, 11, 0, 0, TimeSpan.FromHours(8)))
        {
            ItemId = "10000089",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = RecordProvenance.Unknown,
        };
        DateTimeOffset now = TestTime();
        await context.AtomicGacha.CommitAsync(
            new FurinaChronicle.Services.Gacha.History.GachaAtomicChangeRequest(
                OperationId.New(),
                DataChangeOperationKind.Import,
                DataOrigin.StandardImport,
                "gacha first",
                now,
                now.AddSeconds(1),
                [new FurinaChronicle.Services.Gacha.History.GachaFactMutation(
                    new GachaFactReference(accountId, "cross-gacha"),
                    gacha)]));
        ManualAcquisition manual = Acquisition(accountId, "manual second");
        await context.AtomicGacha.CommitManualAcquisitionsAsync(
            ManualRequest([
                new ManualAcquisitionMutation(
                    manual.AcquisitionId,
                    accountId,
                    manual)
            ], DataChangeOperationKind.Import));
        ChallengeObservation challenge = Challenge(
            accountId,
            stars: 6,
            sequence: 3);
        await context.AtomicGacha.CommitChallengeObservationsAsync(
            ChallengeRequest([challenge]));
        IArchiveOperationHistoryStore historyStore = context.AtomicGacha;

        ArchiveOperationResult firstUndo = await historyStore.UndoLatestAsync(
            new ArchiveUndoRequest(
                OperationId.New(),
                account.PlayerArchiveId,
                now.AddMinutes(4),
                now.AddMinutes(4).AddSeconds(1)));
        ArchiveOperationResult secondUndo = await historyStore.UndoLatestAsync(
            new ArchiveUndoRequest(
                OperationId.New(),
                account.PlayerArchiveId,
                now.AddMinutes(5),
                now.AddMinutes(5).AddSeconds(1)));

        Assert.Equal(ChangeExecutionStatus.Applied, firstUndo.Status);
        Assert.Empty(
            await context.AtomicGacha.GetChallengeObservationsAsync(accountId));
        Assert.Equal(ChangeExecutionStatus.Applied, secondUndo.Status);
        Assert.Empty(
            await context.AtomicGacha.GetManualAcquisitionsAsync(accountId));
        Assert.NotNull(await context.AtomicGacha.GetCurrentAsync(
            new GachaFactReference(accountId, "cross-gacha")));
    }

    [Fact]
    public async Task AccountPurge_RemovesDomainRecoveryMaterialAndScopesSuppression()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GameAccount originalAccount = Assert.IsType<GameAccount>(
            await context.Accounts.GetByIdAsync(accountId));
        ItemStateObservation observation = CharacterObservation(
            ItemObservationId.New(),
            accountId,
            level: 90);
        ManualAcquisition manual = Acquisition(accountId, "purge fixture");
        await context.AtomicGacha.CommitItemObservationsAsync(
            ItemRequest([observation]));
        await context.AtomicGacha.CommitManualAcquisitionsAsync(
            ManualRequest([
                new ManualAcquisitionMutation(
                    manual.AcquisitionId,
                    accountId,
                    manual)
            ], DataChangeOperationKind.Import));
        ManualAcquisitionState manualState = Assert.Single(
            await context.AtomicGacha.GetManualAcquisitionsAsync(accountId));
        await context.AtomicGacha.CommitManualAcquisitionsAsync(
            ManualRequest([
                new ManualAcquisitionMutation(
                    manual.AcquisitionId,
                    accountId,
                    Proposed: null,
                    manualState.Version)
            ], DataChangeOperationKind.Delete));
        IArchiveOperationHistoryStore operationStore = context.AtomicGacha;
        DateTimeOffset now = TestTime().AddMinutes(6);

        ArchiveOperationResult purged = await operationStore.PurgeAccountAsync(
            new ArchiveScopePurgeRequest(
                OperationId.New(),
                accountId,
                now,
                now.AddSeconds(1),
                "purge account fixture"));

        Assert.Equal(ChangeExecutionStatus.Applied, purged.Status);
        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            Assert.Equal(0, raw.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ArchiveDomainFacts;"));
            Assert.Equal(0, raw.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM ArchiveDomainRevisions;"));
            Assert.Equal(0, raw.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM UndoMaterials WHERE EntityKind IN (?, ?, ?);",
                SqliteGachaAtomicChangeStore.ItemObservationEntityKind,
                SqliteGachaAtomicChangeStore.ManualAcquisitionEntityKind,
                SqliteGachaAtomicChangeStore.ChallengeObservationEntityKind));
        }

        GameAccount replacement =
            SqliteRepositoryTestContext.CreateAccount(
                originalAccount.PlayerArchiveId,
                uid: originalAccount.Uid,
                region: originalAccount.ServerRegion,
                id: Guid.NewGuid(),
                roleIdentity: originalAccount.RoleIdentity);
        await context.Accounts.AddAsync(replacement);
        ItemStateObservation sameArchive = new(
            observation.ObservationId,
            replacement.Id,
            observation.Kind,
            observation.CollectionCompleteness,
            observation.Entries,
            observation.Provenance);
        ArchiveOperationResult suppressed =
            await context.AtomicGacha.CommitItemObservationsAsync(
                ItemRequest([sameArchive]));
        Assert.Equal(ChangeExecutionStatus.Suppressed, suppressed.Status);

        PlayerArchive otherArchive =
            SqliteRepositoryTestContext.CreateArchive("独立档案");
        await context.Archives.AddAsync(otherArchive);
        GameAccount otherAccount =
            SqliteRepositoryTestContext.CreateAccount(
                otherArchive.Id,
                uid: originalAccount.Uid,
                region: originalAccount.ServerRegion,
                id: Guid.NewGuid(),
                roleIdentity: originalAccount.RoleIdentity);
        await context.Accounts.AddAsync(otherAccount);
        ItemStateObservation otherCopy = new(
            observation.ObservationId,
            otherAccount.Id,
            observation.Kind,
            observation.CollectionCompleteness,
            observation.Entries,
            observation.Provenance);
        ArchiveOperationResult independent =
            await context.AtomicGacha.CommitItemObservationsAsync(
                ItemRequest([otherCopy]));
        Assert.Equal(ChangeExecutionStatus.Applied, independent.Status);
    }

    [Fact]
    public async Task PagedQueries_FilterRangeAndReturnNewestFirst()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        ItemStateObservation[] items = Enumerable.Range(0, 3)
            .Select(index =>
            {
                ItemStateObservation seed = CharacterObservation(
                    ItemObservationId.New(),
                    accountId,
                    80 + index);
                return new ItemStateObservation(
                    seed.ObservationId,
                    accountId,
                    seed.Kind,
                    seed.CollectionCompleteness,
                    seed.Entries,
                    new RecordProvenance(
                        DataOrigin.OfficialApi,
                        new RecordTimestamps(
                            ObservedAt: TestTime().AddDays(index))));
            })
            .ToArray();
        await context.AtomicGacha.CommitItemObservationsAsync(
            ItemRequest(items));

        ManualAcquisition character = Acquisition(accountId, "character");
        var weapon = new ManualAcquisition(
            ManualAcquisitionId.New(),
            accountId,
            InventoryItemKind.Weapon,
            "WEAPON_15514",
            OccurrenceTime.At(
                TestTime().AddDays(2),
                TimePrecision.Second),
            ManualAcquisitionReason.Other,
            "event weapon",
            new RecordProvenance(DataOrigin.UserEntered));
        await context.AtomicGacha.CommitManualAcquisitionsAsync(
            ManualRequest([
                new ManualAcquisitionMutation(
                    character.AcquisitionId,
                    accountId,
                    character),
                new ManualAcquisitionMutation(
                    weapon.AcquisitionId,
                    accountId,
                    weapon),
            ], DataChangeOperationKind.Import));

        ChallengeObservation first = Challenge(accountId, 3, sequence: 1);
        ChallengeObservation second = Challenge(accountId, 6, sequence: 2);
        await context.AtomicGacha.CommitChallengeObservationsAsync(
            ChallengeRequest([first, second]));

        ArchiveDomainPage<ItemStateObservation> itemPage =
            await context.AtomicGacha.GetItemObservationPageAsync(
                new ItemObservationPageQuery(
                    accountId,
                    InventoryItemKind.Character,
                    ObservedFrom: TestTime().AddDays(1),
                    ObservedTo: TestTime().AddDays(3),
                    Limit: 1));
        Assert.Equal(2, itemPage.TotalCount);
        Assert.Single(itemPage.Items);
        Assert.Equal(
            TestTime().AddDays(2),
            itemPage.Items[0].Provenance.Timestamps.ObservedAt);

        ArchiveDomainPage<ManualAcquisitionState> manualPage =
            await context.AtomicGacha.GetManualAcquisitionPageAsync(
                new ManualAcquisitionPageQuery(
                    accountId,
                    InventoryItemKind.Weapon));
        Assert.Equal(1, manualPage.TotalCount);
        Assert.Equal(InventoryItemKind.Weapon,
            Assert.Single(manualPage.Items).Acquisition.Kind);

        ArchiveDomainPage<ChallengeObservation> challengePage =
            await context.AtomicGacha.GetChallengeObservationPageAsync(
                new ChallengeObservationPageQuery(
                    accountId,
                    ChallengeMode.SpiralAbyss,
                    first.SeasonId,
                    Limit: 1));
        Assert.Equal(2, challengePage.TotalCount);
        Assert.Equal(
            second.ObservationId,
            Assert.Single(challengePage.Items).ObservationId);
    }

    [Fact]
    public async Task Challenge_AdjacentSameContentAndStableSourceRetryAreNoOp()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        ChallengeObservation first = Challenge(accountId, 6, sequence: 1);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                ChallengeRequest([first]))).Status);

        ChallengeObservation adjacent = CloneChallenge(
            first,
            ChallengeObservationId.New(),
            TestTime().AddMinutes(2),
            sourceRecordId: "different-source-record");
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                ChallengeRequest([adjacent]))).Status);

        string stableSource = first.Provenance.Source!.SourceRecordId!;
        ChallengeObservation stableRetry = CloneChallenge(
            first,
            ChallengeObservationId.New(),
            TestTime().AddDays(1),
            stableSource);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                ChallengeRequest([stableRetry]))).Status);
        Assert.Single(
            await context.AtomicGacha.GetChallengeObservationsAsync(accountId));

        ChallengeObservation conflictingSource = CloneChallenge(
            Challenge(accountId, 3, sequence: 3),
            ChallengeObservationId.New(),
            TestTime().AddDays(2),
            stableSource);
        Assert.Equal(
            ChangeExecutionStatus.Conflict,
            (await context.AtomicGacha.CommitChallengeObservationsAsync(
                ChallengeRequest([conflictingSource]))).Status);
    }

    private static ChallengeObservation CloneChallenge(
        ChallengeObservation source,
        ChallengeObservationId id,
        DateTimeOffset observedAt,
        string sourceRecordId) =>
        new(
            id,
            source.GameAccountId,
            source.Mode,
            source.SeasonId,
            source.PeriodStart,
            source.PeriodEnd,
            source.Completeness,
            source.SpiralAbyssFloors,
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(
                    ObservedAt: observedAt,
                    FetchedAt: observedAt.AddSeconds(1)),
                new DataSourceReference(
                    "miyoushe",
                    sourceRecordId: sourceRecordId)));

    private static async Task<SqliteRepositoryTestContext> CreateContextAsync()
    {
        SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        return context;
    }

    private static async Task<Guid> AddAccountAsync(
        SqliteRepositoryTestContext context)
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account =
            SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return account.Id;
    }

    private static async Task<(Guid ArchiveId, Guid AccountId)>
        AddArchiveAndAccountAsync(FurinaDatabase database)
    {
        await database.InitializeAsync();
        var archives = new SqlitePlayerArchiveRepository(database);
        var accounts = new SqliteGameAccountRepository(database);
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        GameAccount account =
            SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await archives.AddAsync(archive);
        await accounts.AddAsync(account);
        return (archive.Id, account.Id);
    }

    private static ItemObservationCommitRequest ItemRequest(
        IEnumerable<ItemStateObservation> observations) =>
        new(
            OperationId.New(),
            DataChangeOperationKind.Refresh,
            DataOrigin.OfficialApi,
            "save inventory observations",
            TestTime(),
            TestTime().AddSeconds(1),
            observations);

    private static ManualAcquisitionCommitRequest ManualRequest(
        IEnumerable<ManualAcquisitionMutation> mutations,
        DataChangeOperationKind kind) =>
        new(
            OperationId.New(),
            kind,
            "save manual acquisition",
            TestTime(),
            TestTime().AddSeconds(1),
            mutations);

    private static ChallengeObservationCommitRequest ChallengeRequest(
        IEnumerable<ChallengeObservation> observations) =>
        new(
            OperationId.New(),
            DataChangeOperationKind.Refresh,
            DataOrigin.OfficialApi,
            "save Spiral Abyss observation",
            TestTime(),
            TestTime().AddSeconds(1),
            observations);

    private static ItemStateObservation CharacterObservation(
        ItemObservationId id,
        Guid accountId,
        int level) =>
        new(
            id,
            accountId,
            InventoryItemKind.Character,
            DataCompleteness.Partial,
            [new InventoryStateEntry(
                InventoryItemKind.Character,
                "CHAR_1001",
                null,
                1,
                level,
                null,
                InventoryFieldCoverage.Quantity |
                    InventoryFieldCoverage.Level,
                DataCompleteness.Partial)],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: TestTime())));

    private static ManualAcquisition Acquisition(
        Guid accountId,
        string detail) =>
        new(
            ManualAcquisitionId.New(),
            accountId,
            InventoryItemKind.Character,
            "CHAR_1001",
            OccurrenceTime.At(TestTime(), TimePrecision.Second),
            ManualAcquisitionReason.Other,
            detail,
            new RecordProvenance(DataOrigin.UserEntered));

    private static ChallengeObservation Challenge(
        Guid accountId,
        int stars,
        int sequence = 0)
    {
        DateTimeOffset observed = TestTime().AddMinutes(sequence);
        return new ChallengeObservation(
            ChallengeObservationId.New(),
            accountId,
            ChallengeMode.SpiralAbyss,
            "2026-10-upper",
            observed.AddDays(-1),
            observed.AddDays(14),
            DataCompleteness.Partial,
            [new SpiralAbyssFloorResult(
                12,
                DataCompleteness.Partial,
                [new SpiralAbyssChamberResult(
                    1,
                    Math.Min(3, stars),
                    OccurrenceTime.Unknown),
                 new SpiralAbyssChamberResult(
                    2,
                    Math.Max(0, stars - 3),
                    OccurrenceTime.Unknown)])],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: observed),
                new DataSourceReference(
                    "miyoushe",
                    sourceRecordId: Guid.NewGuid().ToString("N"))));
    }

    private static DateTimeOffset TestTime() =>
        new(2026, 10, 18, 12, 0, 0, TimeSpan.FromHours(8));

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

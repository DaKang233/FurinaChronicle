// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class Phase8B7MigrationTests
{
    private const int ApplicationId = 0x46554348;

    [Fact]
    public async Task InitializeAsync_PopulatedVersionSix_PreservesAllDomains()
    {
        await using VersionSixFixture fixture =
            await VersionSixFixture.CreateAsync();
        PreservedState before = fixture.ReadPreservedState();

        await using FurinaDatabase database = fixture.OpenDatabase();
        await database.InitializeAsync();

        PreservedState after = fixture.ReadPreservedState();
        Assert.Equal(before, after);

        using (SQLiteConnection raw = fixture.OpenRawConnection())
        {
            Assert.Equal(7, raw.ExecuteScalar<int>("PRAGMA user_version;"));
            Assert.Equal(
                ApplicationId,
                raw.ExecuteScalar<int>("PRAGMA application_id;"));
            Assert.Empty(raw.Query<ForeignKeyFailure>(
                "PRAGMA foreign_key_check;"));
            Assert.Equal(3, CountTables(raw, "Chronicle%"));
            Assert.Equal(9, CountTriggers(raw, "TR_Chronicle_%"));
            Assert.Equal(
                0,
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ChronicleGenerations;"));
            Assert.Equal(
                0,
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ChronicleEntries;"));
        }

        var archives = new SqlitePlayerArchiveRepository(database);
        var accounts = new SqliteGameAccountRepository(database);
        var store = new SqliteGachaAtomicChangeStore(database);
        var files = new ArchiveDomainPortableFileService(
            archives,
            accounts,
            store);

        Assert.Single(await store.GetItemObservationsAsync(
            fixture.AccountId));
        Assert.Single(await store.GetManualAcquisitionsAsync(
            fixture.AccountId));
        Assert.Equal(
            2,
            (await store.GetManualAcquisitionRevisionsAsync(
                fixture.AccountId)).Count);
        Assert.Single(await store.GetChallengeObservationsAsync(
            fixture.AccountId));

        using var characterWeapon = new MemoryStream();
        await files.ExportAsync(
            fixture.ArchiveId,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            characterWeapon,
            TestTime().AddHours(1));
        characterWeapon.Position = 0;
        ArchiveDomainPortablePackage characterPackage =
            await files.ReadAsync(characterWeapon);
        Assert.Equal(
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            characterPackage.DatasetKind);
        Assert.Single(characterPackage.Accounts);

        using var challenge = new MemoryStream();
        await files.ExportAsync(
            fixture.ArchiveId,
            ArchiveDomainDatasetKind.ChallengeHistory,
            challenge,
            TestTime().AddHours(1));
        challenge.Position = 0;
        ArchiveDomainPortablePackage challengePackage =
            await files.ReadAsync(challenge);
        Assert.Equal(
            ArchiveDomainDatasetKind.ChallengeHistory,
            challengePackage.DatasetKind);
        Assert.Single(challengePackage.Accounts);
    }

    [Fact]
    public async Task InitializeAsync_VersionSixLateFailure_RollsBackMigration()
    {
        await using VersionSixFixture fixture =
            await VersionSixFixture.CreateAsync();
        PreservedState before = fixture.ReadPreservedState();
        fixture.AddConflictingChronicleGenerationTable();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await Assert.ThrowsAsync<SQLiteException>(
                () => database.InitializeAsync());
        }

        Assert.Equal(before, fixture.ReadPreservedState());
        using SQLiteConnection raw = fixture.OpenRawConnection();
        Assert.Equal(6, raw.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(1, CountTables(raw, "ChronicleGenerations"));
        Assert.Equal(0, CountTables(raw, "ChronicleScopeStates"));
        Assert.Equal(0, CountTables(raw, "ChronicleEntries"));
        Assert.Equal(0, CountTriggers(raw, "TR_Chronicle_%"));
    }

    [Fact]
    public async Task InitializeAsync_CanceledVersionSix_DoesNotModifyDatabase()
    {
        await using VersionSixFixture fixture =
            await VersionSixFixture.CreateAsync();
        PreservedState before = fixture.ReadPreservedState();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await using (FurinaDatabase database = fixture.OpenDatabase())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => database.InitializeAsync(cancellation.Token));
        }

        Assert.Equal(before, fixture.ReadPreservedState());
        using SQLiteConnection raw = fixture.OpenRawConnection();
        Assert.Equal(6, raw.ExecuteScalar<int>("PRAGMA user_version;"));
        Assert.Equal(0, CountTables(raw, "Chronicle%"));
        Assert.Equal(0, CountTriggers(raw, "TR_Chronicle_%"));
    }

    private static int CountTables(
        SQLiteConnection connection,
        string pattern) => connection.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE ?;",
        pattern);

    private static int CountTriggers(
        SQLiteConnection connection,
        string pattern) => connection.ExecuteScalar<int>(
        "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE ?;",
        pattern);

    private static DateTimeOffset TestTime() =>
        new(2026, 10, 20, 10, 0, 0, TimeSpan.FromHours(8));

    private sealed record PreservedState(
        int ArchiveCount,
        int AccountCount,
        int GachaCount,
        string GachaExternalId,
        int DomainFactCount,
        int DomainRevisionCount,
        string DomainContentHashes,
        int ChangeSetCount,
        int UndoMaterialCount,
        int TombstoneCount,
        int GachaReceiptCount,
        int DomainReceiptCount);

    private sealed class ForeignKeyFailure
    {
        public string Table { get; set; } = string.Empty;
        public long RowId { get; set; }
        public string Parent { get; set; } = string.Empty;
        public int Fkid { get; set; }
    }

    private sealed class VersionSixFixture : IAsyncDisposable
    {
        private readonly string directory;

        private VersionSixFixture(
            string directory,
            Guid archiveId,
            Guid accountId)
        {
            this.directory = directory;
            ArchiveId = archiveId;
            AccountId = accountId;
        }

        public string DatabasePath => Path.Combine(directory, "phase8b7.db3");

        public Guid ArchiveId { get; }

        public Guid AccountId { get; }

        public static async Task<VersionSixFixture> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Guid archiveId = Guid.NewGuid();
            Guid accountId = Guid.NewGuid();
            var fixture = new VersionSixFixture(
                directory,
                archiveId,
                accountId);

            await using (FurinaDatabase database = fixture.OpenDatabase())
            {
                await database.InitializeAsync();
                var archives = new SqlitePlayerArchiveRepository(database);
                var accounts = new SqliteGameAccountRepository(database);
                var store = new SqliteGachaAtomicChangeStore(database);
                DateTimeOffset now = TestTime();
                var archive = new PlayerArchive(
                    archiveId,
                    "Phase 8B.7 migration fixture",
                    now,
                    now);
                await archives.AddAsync(archive);
                GameRoleNaturalIdentity natural =
                    GenshinGameRoleIdentity.Create(
                        "800000001",
                        GameServerRegion.Asia);
                var identity = new GameRoleIdentity(
                    GameRoleIdentityId.FromNaturalIdentity(natural),
                    natural);
                var account = new GameAccount(
                    accountId,
                    archiveId,
                    natural.Uid,
                    GameServerRegion.Asia,
                    "Synthetic migration account",
                    false,
                    now,
                    now,
                    identity);
                await accounts.AddAsync(account);

                await SeedGachaAsync(store, accountId);
                await SeedArchiveDomainsAsync(store, accountId);
            }

            fixture.ConvertCurrentDatabaseToPopulatedVersionSix();
            return fixture;
        }

        public FurinaDatabase OpenDatabase() =>
            new(new SqliteDatabaseOptions(DatabasePath));

        public SQLiteConnection OpenRawConnection() =>
            new(
                DatabasePath,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create |
                SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);

        public PreservedState ReadPreservedState()
        {
            using SQLiteConnection raw = OpenRawConnection();
            return new PreservedState(
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GameAccounts;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GachaRecords;"),
                raw.ExecuteScalar<string>(
                    "SELECT ExternalRecordId FROM GachaRecords ORDER BY ExternalRecordId LIMIT 1;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveDomainFacts;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveDomainRevisions;"),
                raw.ExecuteScalar<string>(
                    "SELECT group_concat(ContentHash, '|') FROM (SELECT ContentHash FROM ArchiveDomainFacts ORDER BY DomainKind, EntityId);"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM UndoMaterials;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM LocalOnlyTombstones;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PortableImportReceipts;"),
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DomainPortableReceipts;"));
        }

        public void AddConflictingChronicleGenerationTable()
        {
            using SQLiteConnection raw = OpenRawConnection();
            raw.Execute(
                "CREATE TABLE ChronicleGenerations (Sentinel TEXT NOT NULL);");
            raw.Execute(
                "INSERT INTO ChronicleGenerations (Sentinel) VALUES ('preserve');");
        }

        private static async Task SeedGachaAsync(
            SqliteGachaAtomicChangeStore store,
            Guid accountId)
        {
            DateTimeOffset now = TestTime();
            var commit = new CommitGachaRecords(store);
            GachaRecord Keep(string id, string itemId) => new(
                accountId,
                id,
                "Synthetic item",
                5,
                now)
            {
                ItemId = itemId,
                ItemType = "Character",
                GachaType = "301",
                UigfGachaType = "301",
                Provenance = new RecordProvenance(
                    DataOrigin.StandardImport,
                    new RecordTimestamps(
                        ImportedAt: now.AddMinutes(1)),
                    source: null,
                    AcquisitionBatchId.New())
            };
            CommitGachaRecordsResult result = await commit.ExecuteAsync(
                new CommitGachaRecordsRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Import,
                    DataOrigin.StandardImport,
                    "seed version six gacha",
                    now,
                    now.AddSeconds(1),
                    [
                        Keep("phase8b7-keep", "10000089"),
                        Keep("phase8b7-delete", "10000090")
                    ],
                    GachaRecordConflictPolicy.PreserveExisting,
                    SuppressTombstonesAndContinue: false));
            Assert.Equal(ChangeExecutionStatus.Applied, result.Status);

            var history = new ManageGachaHistory(store);
            GachaAtomicChangeResult deleted =
                await history.IrreversiblyDeleteAsync(
                    OperationId.New(),
                    new GachaFactReference(
                        accountId,
                        "phase8b7-delete"));
            Assert.Equal(ChangeExecutionStatus.Applied, deleted.Status);
        }

        private static async Task SeedArchiveDomainsAsync(
            SqliteGachaAtomicChangeStore store,
            Guid accountId)
        {
            DateTimeOffset now = TestTime();
            var observation = new ItemStateObservation(
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
                    new RecordTimestamps(
                        ObservedAt: now,
                        FetchedAt: now.AddSeconds(1)),
                    new DataSourceReference(
                        "miyoushe",
                        sourceSnapshotId: "phase8b7-items"),
                    AcquisitionBatchId.New()));
            ArchiveOperationResult itemResult =
                await store.CommitItemObservationsAsync(
                    new ItemObservationCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Refresh,
                        DataOrigin.OfficialApi,
                        "seed item observation",
                        now,
                        now.AddSeconds(1),
                        [observation]));
            Assert.Equal(ChangeExecutionStatus.Applied, itemResult.Status);

            var acquisition = new ManualAcquisition(
                ManualAcquisitionId.New(),
                accountId,
                InventoryItemKind.Character,
                "CHAR_10000089",
                OccurrenceTime.Within(
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
                    TimePrecision.Week),
                ManualAcquisitionReason.EventSelection,
                "Synthetic event",
                new RecordProvenance(DataOrigin.UserEntered));
            ArchiveOperationResult acquisitionResult =
                await store.CommitManualAcquisitionsAsync(
                    new ManualAcquisitionCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Import,
                        "seed manual acquisition",
                        now.AddMinutes(1),
                        now.AddMinutes(1).AddSeconds(1),
                        [new ManualAcquisitionMutation(
                            acquisition.AcquisitionId,
                            accountId,
                            acquisition)]));
            Assert.Equal(
                ChangeExecutionStatus.Applied,
                acquisitionResult.Status);
            ManualAcquisitionState current = Assert.Single(
                await store.GetManualAcquisitionsAsync(accountId));
            var correctedAcquisition = new ManualAcquisition(
                acquisition.AcquisitionId,
                acquisition.GameAccountId,
                acquisition.Kind,
                acquisition.TypeId,
                acquisition.OccurredAt,
                acquisition.Reason,
                "Corrected synthetic event",
                acquisition.Provenance);
            ArchiveOperationResult correctionResult =
                await store.CommitManualAcquisitionsAsync(
                    new ManualAcquisitionCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Correction,
                        "correct manual acquisition",
                        now.AddMinutes(2),
                        now.AddMinutes(2).AddSeconds(1),
                        [new ManualAcquisitionMutation(
                            acquisition.AcquisitionId,
                            accountId,
                            correctedAcquisition,
                            current.Version)]));
            Assert.Equal(
                ChangeExecutionStatus.Applied,
                correctionResult.Status);

            var challenge = new ChallengeObservation(
                ChallengeObservationId.New(),
                accountId,
                ChallengeMode.SpiralAbyss,
                "phase8b7-2026-10",
                now.AddDays(-2),
                now.AddDays(14),
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
                    new RecordTimestamps(ObservedAt: now),
                    new DataSourceReference(
                        "miyoushe",
                        sourceRecordId: "phase8b7-abyss")));
            ArchiveOperationResult challengeResult =
                await store.CommitChallengeObservationsAsync(
                    new ChallengeObservationCommitRequest(
                        OperationId.New(),
                        DataChangeOperationKind.Refresh,
                        DataOrigin.OfficialApi,
                        "seed challenge observation",
                        now.AddMinutes(3),
                        now.AddMinutes(3).AddSeconds(1),
                        [challenge]));
            Assert.Equal(
                ChangeExecutionStatus.Applied,
                challengeResult.Status);
        }

        private void ConvertCurrentDatabaseToPopulatedVersionSix()
        {
            using SQLiteConnection raw = OpenRawConnection();
            string receiptChangeSetId = raw.ExecuteScalar<string>(
                "SELECT ChangeSetId FROM DataChangeSets ORDER BY rowid LIMIT 1;");
            long ticks = TestTime().UtcDateTime.Ticks;
            raw.Execute(
                """
                INSERT INTO PortableImportReceipts
                    (ReceiptId, ChangeSetId, PackageFingerprint,
                     ReceivedAtUtcTicks, ReceivedAtOffsetMinutes,
                     ReceiptBatchId, SourceArchiveId, TargetArchiveId,
                     AccountCount, RecordCount)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?);
                """,
                Guid.NewGuid().ToString("D"),
                receiptChangeSetId,
                new string('a', 64),
                ticks,
                480,
                Guid.NewGuid().ToString("D"),
                Guid.NewGuid().ToString("D"),
                ArchiveId.ToString("D"),
                1,
                1);
            raw.Execute(
                """
                INSERT INTO DomainPortableReceipts
                    (ReceiptId, ChangeSetId, DatasetKind,
                     PackageFingerprint, ReceivedAtUtcTicks,
                     ReceivedAtOffsetMinutes, TargetArchiveId, RecordCount)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?);
                """,
                Guid.NewGuid().ToString("D"),
                receiptChangeSetId,
                "character-weapon-history",
                new string('b', 64),
                ticks,
                480,
                ArchiveId.ToString("D"),
                1);

            foreach (string trigger in raw.Query<NameRow>(
                         "SELECT name FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'TR_Chronicle_%';")
                     .Select(row => row.Name))
            {
                raw.Execute($"DROP TRIGGER {trigger};");
            }
            raw.Execute("DROP TABLE ChronicleEntries;");
            raw.Execute("DROP TABLE ChronicleGenerations;");
            raw.Execute("DROP TABLE ChronicleScopeStates;");
            raw.Execute("PRAGMA user_version = 6;");
        }

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private sealed class NameRow
        {
            public string Name { get; set; } = string.Empty;
        }
    }
}

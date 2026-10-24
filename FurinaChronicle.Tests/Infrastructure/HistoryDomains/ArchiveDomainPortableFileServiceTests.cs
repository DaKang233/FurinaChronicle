// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class ArchiveDomainPortableFileServiceTests
{
    [Fact]
    public async Task CharacterWeaponDataset_RoundTripsIntoNewDatabase()
    {
        await using PortableContext source = await PortableContext.CreateAsync();
        PlayerArchive archive = await source.AddArchiveAndAccountAsync();
        GameAccount account = Assert.Single(
            await source.Accounts.GetByArchiveIdAsync(archive.Id));
        ItemStateObservation observation = CharacterObservation(account.Id);
        ManualAcquisition acquisition = Manual(account.Id);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await source.Store.CommitItemObservationsAsync(
                ItemRequest([observation]))).Status);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await source.Store.CommitManualAcquisitionsAsync(
                ManualRequest(acquisition))).Status);
        ManualAcquisitionState first = Assert.Single(
            await source.Store.GetManualAcquisitionsAsync(account.Id));
        var corrected = new ManualAcquisition(
            acquisition.AcquisitionId,
            account.Id,
            acquisition.Kind,
            acquisition.TypeId,
            acquisition.OccurredAt,
            acquisition.Reason,
            "周年庆自选",
            acquisition.Provenance);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await source.Store.CommitManualAcquisitionsAsync(
                new ManualAcquisitionCommitRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Correction,
                    "correct manual acquisition",
                    TestTime().AddMinutes(2),
                    TestTime().AddMinutes(3),
                    [new ManualAcquisitionMutation(
                        corrected.AcquisitionId,
                        account.Id,
                        corrected,
                        first.Version)]))).Status);

        using var exported = new MemoryStream();
        await source.Files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            exported,
            TestTime());
        Assert.DoesNotContain(
            "\"change_set_id\"",
            Encoding.UTF8.GetString(exported.ToArray()));
        exported.Position = 0;
        ArchiveDomainPortablePackage package =
            await source.Files.ReadAsync(exported);

        await using PortableContext target = await PortableContext.CreateAsync();
        ArchiveDomainPortablePlan plan = await target.Files.PlanAsync(package);
        Assert.Equal(2, plan.AddCount);
        Assert.Equal(0, plan.ConflictCount);
        ArchiveOperationResult applied =
            await target.Store.ApplyArchiveDomainPortableAsync(
                ApplyRequest(package, plan));

        Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
        GameAccount importedAccount = Assert.Single(
            await target.Accounts.GetByArchiveIdAsync(plan.TargetArchiveId));
        Assert.Single(await target.Store.GetItemObservationsAsync(
            importedAccount.Id));
        Assert.Single(await target.Store.GetManualAcquisitionsAsync(
            importedAccount.Id));
        Assert.Equal(
            2,
            (await target.Store.GetManualAcquisitionRevisionsAsync(
                importedAccount.Id)).Count);

        using var reexported = new MemoryStream();
        await target.Files.ExportAsync(
            plan.TargetArchiveId,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            reexported,
            TestTime().AddMinutes(4));
        reexported.Position = 0;
        ArchiveDomainPortablePackage roundTripped =
            await target.Files.ReadAsync(reexported);
        ArchiveDomainPortableSemanticComparison semanticComparison =
            ArchiveDomainPortableSemanticComparer.Compare(
                package,
                roundTripped);
        Assert.True(
            semanticComparison.Equivalent,
            string.Join(Environment.NewLine, semanticComparison.Differences));
        Assert.Equal(
            package.Accounts.Single().ManualRevisions
                .Select(item => item.RevisionId),
            roundTripped.Accounts.Single().ManualRevisions
                .Select(item => item.RevisionId));

        ArchiveDomainPortablePlan repeat = await target.Files.PlanAsync(
            package,
            plan.TargetArchiveId);
        Assert.Equal(0, repeat.AddCount);
        Assert.Equal(2, repeat.SkipCount);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await target.Store.ApplyArchiveDomainPortableAsync(
                ApplyRequest(package, repeat))).Status);
    }

    [Fact]
    public async Task ChallengeDataset_RoundTripsWithoutInventingCompletionTime()
    {
        await using PortableContext source = await PortableContext.CreateAsync();
        PlayerArchive archive = await source.AddArchiveAndAccountAsync();
        GameAccount account = Assert.Single(
            await source.Accounts.GetByArchiveIdAsync(archive.Id));
        ChallengeObservation challenge = Challenge(account.Id);
        Assert.Equal(
            ChangeExecutionStatus.Applied,
            (await source.Store.CommitChallengeObservationsAsync(
                ChallengeRequest([challenge]))).Status);
        using var exported = new MemoryStream();
        await source.Files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.ChallengeHistory,
            exported,
            TestTime());
        exported.Position = 0;
        ArchiveDomainPortablePackage package =
            await source.Files.ReadAsync(exported);

        await using PortableContext target = await PortableContext.CreateAsync();
        ArchiveDomainPortablePlan plan = await target.Files.PlanAsync(package);
        ArchiveOperationResult result =
            await target.Store.ApplyArchiveDomainPortableAsync(
                ApplyRequest(package, plan));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        GameAccount imported = Assert.Single(
            await target.Accounts.GetByArchiveIdAsync(plan.TargetArchiveId));
        ChallengeObservation saved = Assert.Single(
            await target.Store.GetChallengeObservationsAsync(imported.Id));
        Assert.Equal(OccurrenceTimeKind.Unknown,
            Assert.Single(Assert.Single(saved.SpiralAbyssFloors).Chambers)
                .CompletedAt.Kind);
        Assert.Equal(challenge.Provenance, saved.Provenance);

        using var reexported = new MemoryStream();
        await target.Files.ExportAsync(
            plan.TargetArchiveId,
            ArchiveDomainDatasetKind.ChallengeHistory,
            reexported,
            TestTime().AddMinutes(2));
        reexported.Position = 0;
        ArchiveDomainPortablePackage roundTripped =
            await target.Files.ReadAsync(reexported);
        ArchiveDomainPortableSemanticComparison semanticComparison =
            ArchiveDomainPortableSemanticComparer.Compare(
                package,
                roundTripped);
        Assert.True(
            semanticComparison.Equivalent,
            string.Join(Environment.NewLine, semanticComparison.Differences));

        ArchiveDomainPortablePlan repeat = await target.Files.PlanAsync(
            package,
            plan.TargetArchiveId);
        Assert.Equal(0, repeat.AddCount);
        Assert.Equal(1, repeat.SkipCount);
        Assert.Equal(
            ChangeExecutionStatus.NoOp,
            (await target.Store.ApplyArchiveDomainPortableAsync(
                ApplyRequest(package, repeat))).Status);
    }

    [Fact]
    public async Task Apply_WhenTargetChangesAfterPlan_RollsBackNewParents()
    {
        await using PortableContext source = await PortableContext.CreateAsync();
        PlayerArchive archive = await source.AddArchiveAndAccountAsync();
        GameAccount account = Assert.Single(
            await source.Accounts.GetByArchiveIdAsync(archive.Id));
        ItemStateObservation observation = CharacterObservation(account.Id);
        await source.Store.CommitItemObservationsAsync(ItemRequest([observation]));
        using var stream = new MemoryStream();
        await source.Files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            stream,
            TestTime());
        stream.Position = 0;
        ArchiveDomainPortablePackage package = await source.Files.ReadAsync(stream);

        await using PortableContext target = await PortableContext.CreateAsync();
        ArchiveDomainPortablePlan plan = await target.Files.PlanAsync(package);
        ArchiveDomainPortablePlan stale = plan with { AddCount = 0, SkipCount = 1 };
        ArchiveOperationResult result =
            await target.Store.ApplyArchiveDomainPortableAsync(
                ApplyRequest(package, stale));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        Assert.Null(await target.Archives.GetByIdAsync(plan.TargetArchiveId));
        Assert.Empty(await target.Accounts.GetByArchiveIdAsync(
            plan.TargetArchiveId));
    }

    [Fact]
    public async Task Read_RejectsFutureMajorAndTamperedContent()
    {
        await using PortableContext context = await PortableContext.CreateAsync();
        PlayerArchive archive = await context.AddArchiveAndAccountAsync();
        using var stream = new MemoryStream();
        await context.Files.ExportAsync(
            archive.Id,
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            stream,
            TestTime());
        string json = Encoding.UTF8.GetString(stream.ToArray());

        string future = json.Replace(
            "\"format_major\": 1",
            "\"format_major\": 2",
            StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            context.Files.ReadAsync(new MemoryStream(
                Encoding.UTF8.GetBytes(future))));
        string tampered = json.Replace(
            "800000001",
            "800000002",
            StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            context.Files.ReadAsync(new MemoryStream(
                Encoding.UTF8.GetBytes(tampered))));
    }

    private static ArchiveDomainPortableApplyRequest ApplyRequest(
        ArchiveDomainPortablePackage package,
        ArchiveDomainPortablePlan plan) =>
        new(
            OperationId.New(),
            package,
            plan,
            TestTime().AddMinutes(1),
            "import Portable domain history");

    private static ItemObservationCommitRequest ItemRequest(
        IEnumerable<ItemStateObservation> observations) =>
        new(
            OperationId.New(),
            DataChangeOperationKind.Refresh,
            DataOrigin.OfficialApi,
            "save item state",
            TestTime(),
            TestTime().AddSeconds(1),
            observations);

    private static ManualAcquisitionCommitRequest ManualRequest(
        ManualAcquisition acquisition) =>
        new(
            OperationId.New(),
            DataChangeOperationKind.Import,
            "save manual acquisition",
            TestTime(),
            TestTime().AddSeconds(1),
            [new ManualAcquisitionMutation(
                acquisition.AcquisitionId,
                acquisition.GameAccountId,
                acquisition)]);

    private static ChallengeObservationCommitRequest ChallengeRequest(
        IEnumerable<ChallengeObservation> observations) =>
        new(
            OperationId.New(),
            DataChangeOperationKind.Refresh,
            DataOrigin.OfficialApi,
            "save challenge observation",
            TestTime(),
            TestTime().AddSeconds(1),
            observations);

    private static ItemStateObservation CharacterObservation(Guid accountId) =>
        new(
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
                    ObservedAt: TestTime(),
                    FetchedAt: TestTime().AddSeconds(1)),
                new DataSourceReference(
                    "miyoushe",
                    sourceSnapshotId: "snapshot-1"),
                AcquisitionBatchId.New()));

    private static ManualAcquisition Manual(Guid accountId) =>
        new(
            ManualAcquisitionId.New(),
            accountId,
            InventoryItemKind.Character,
            "CHAR_10000089",
            OccurrenceTime.Within(
                new DateTimeOffset(2026, 10, 12, 0, 0, 0,
                    TimeSpan.FromHours(8)),
                new DateTimeOffset(2026, 10, 19, 0, 0, 0,
                    TimeSpan.FromHours(8)),
                TimePrecision.Week),
            ManualAcquisitionReason.EventSelection,
            "砺行修远",
            new RecordProvenance(DataOrigin.UserEntered));

    private static ChallengeObservation Challenge(Guid accountId) =>
        new(
            ChallengeObservationId.New(),
            accountId,
            ChallengeMode.SpiralAbyss,
            "2026-10-upper",
            TestTime().AddDays(-2),
            TestTime().AddDays(14),
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
                new RecordTimestamps(ObservedAt: TestTime()),
                new DataSourceReference(
                    "miyoushe",
                    sourceRecordId: "abyss-2026-10-upper")));

    private static DateTimeOffset TestTime() =>
        new(2026, 10, 18, 10, 0, 0, TimeSpan.FromHours(8));

    private sealed class PortableContext : IAsyncDisposable
    {
        private readonly string directory;

        private PortableContext(string directory, FurinaDatabase database)
        {
            this.directory = directory;
            Database = database;
            Archives = new SqlitePlayerArchiveRepository(database);
            Accounts = new SqliteGameAccountRepository(database);
            Store = new SqliteGachaAtomicChangeStore(database);
            Files = new ArchiveDomainPortableFileService(
                Archives,
                Accounts,
                Store);
        }

        public FurinaDatabase Database { get; }
        public SqlitePlayerArchiveRepository Archives { get; }
        public SqliteGameAccountRepository Accounts { get; }
        public SqliteGachaAtomicChangeStore Store { get; }
        public ArchiveDomainPortableFileService Files { get; }

        public static async Task<PortableContext> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var database = new FurinaDatabase(new SqliteDatabaseOptions(
                Path.Combine(directory, "portable.db3")));
            await database.InitializeAsync();
            return new PortableContext(directory, database);
        }

        public async Task<PlayerArchive> AddArchiveAndAccountAsync()
        {
            DateTimeOffset now = TestTime();
            var archive = new PlayerArchive(
                Guid.NewGuid(),
                "测试档案",
                now,
                now);
            await Archives.AddAsync(archive);
            GameRoleNaturalIdentity natural =
                GenshinGameRoleIdentity.Create(
                    "800000001",
                    GameServerRegion.Asia);
            var identity = new GameRoleIdentity(
                GameRoleIdentityId.FromNaturalIdentity(natural),
                natural);
            var account = new GameAccount(
                Guid.NewGuid(),
                archive.Id,
                "800000001",
                GameServerRegion.Asia,
                "测试账号",
                false,
                now,
                now,
                identity);
            await Accounts.AddAsync(account);
            return archive;
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            Directory.Delete(directory, recursive: true);
        }
    }
}

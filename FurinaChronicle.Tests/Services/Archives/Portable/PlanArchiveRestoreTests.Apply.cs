// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Services.Archives.Portable;

public sealed partial class PlanArchiveRestoreTests
{
    [Fact]
    public async Task AtomicMergePersistsReceiptAndRetryIsIdempotent()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            var store = new SqliteGachaAtomicChangeStore(context.Database);
            var request = Request(source, plan);

            ArchiveRestoreApplyResult applied =
                await store.ApplyAsync(request);
            var restartedStore =
                new SqliteGachaAtomicChangeStore(context.Database);
            ArchiveRestoreApplyResult retry =
                await restartedStore.ApplyAsync(request);

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Assert.NotNull(applied.Receipt);
            Assert.NotNull(applied.Receipt!.ChangeSetId);
            Assert.Equal(1, applied.Receipt.FactCount);
            Assert.Equal(
                ChangeExecutionStatus.AlreadyCommitted,
                retry.Status);
            Assert.Equal(
                applied.Receipt.ReceiptId,
                retry.Receipt!.ReceiptId);
            Assert.Equal(
                applied.Receipt.ReceiptId,
                (await restartedStore.FindReceiptAsync(request.OperationId))!
                    .ReceiptId);
            Guid targetAccountId =
                applied.Receipt.Accounts.Single().TargetAccountId;
            Assert.Single(await context.Gacha.GetByExternalRecordIdsAsync(
                targetAccountId,
                ["1"]));
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task ReplannedMergeOfSamePackageProducesPersistentNoOp()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            var store = new SqliteGachaAtomicChangeStore(context.Database);
            ArchiveRestorePlan firstPlan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            ArchiveRestoreApplyResult first = await store.ApplyAsync(
                Request(source, firstPlan));
            ArchiveRestorePlan secondPlan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: false);

            ArchiveRestoreApplyResult second = await store.ApplyAsync(
                Request(source, secondPlan));

            Assert.Equal(ChangeExecutionStatus.NoOp, second.Status);
            Assert.Null(second.Receipt!.ChangeSetId);
            Assert.Equal(0, second.Receipt.FactCount);
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                2,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
            Assert.Equal(
                first.Receipt!.ChangeSetId,
                connection.Query<ArchiveRestoreChangeSetIdRow>(
                        "SELECT ChangeSetId FROM DataChangeSets;")
                    .Single()
                    .Parse());
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task TargetMutationAfterPlanReturnsConflictWithoutReceipt()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            await context.Archives.AddAsync(
                SqliteRepositoryTestContext.CreateArchive("Later"));
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult result = await store.ApplyAsync(
                Request(source, plan));

            Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
            Assert.Contains(
                "changed after preview",
                result.ConflictReason,
                StringComparison.OrdinalIgnoreCase);
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task StagingMutationAfterPlanIsRejectedBeforeTargetWrite()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            source.Gacha[SourceAccountId] =
                [Record(SourceAccountId, "1", "Diluc")];
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.ApplyAsync(Request(source, plan)));

            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task CancellationBeforeFirstWriteLeavesTargetUnchanged()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            var store = new SqliteGachaAtomicChangeStore(context.Database);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                store.ApplyAsync(
                    Request(source, plan),
                    cancellation.Token));

            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task ExistingParentsPureFactMergeUsesOneUndoableChangeSet()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            var archive = SqliteRepositoryTestContext.CreateArchive(
                "Main",
                TargetArchiveId);
            var account = SqliteRepositoryTestContext.CreateAccount(
                archive.Id,
                Role.NaturalIdentity.Uid,
                GameServerRegion.ChinaOfficial,
                "Traveler",
                TargetAccountId,
                roleIdentity: Role);
            await context.Archives.AddAsync(archive);
            await context.Accounts.AddAsync(account);
            Guid generationId = Guid.NewGuid();
            using (SQLiteConnection connection = context.OpenRawConnection())
            {
                connection.Execute(
                    """
                    INSERT INTO ChronicleScopeStates
                        (ScopeKind, ScopeId, SourceEpoch, DestructiveEpoch)
                    VALUES (1, ?, 0, 0);
                    """,
                    account.Id.ToString("D"));
                connection.Execute(
                    """
                    INSERT INTO ChronicleGenerations
                        (GenerationId, ScopeKind, ScopeId, ScopeSelectionHash,
                         SourceEpoch, DestructiveEpoch, ProjectionVersion,
                         ProfileVersion, CatalogVersion, CreatedAtUtcTicks,
                         ExpiresAtUtcTicks, IsInvalidated)
                    VALUES (?, 1, ?, 'selection', 0, 0, 1, 1, 'catalog',
                            ?, ?, 0);
                    """,
                    generationId.ToString("D"),
                    account.Id.ToString("D"),
                    DateTimeOffset.UtcNow.UtcTicks,
                    DateTimeOffset.UtcNow.AddHours(1).UtcTicks);
                connection.Execute(
                    """
                    INSERT INTO ChronicleEntries
                        (GenerationId, EntryId, SortAtUtcTicks, IsUndated,
                         OccurredStartUtcTicks, OccurredEndUtcTicks, Domain,
                         Kind, Timelines, MinimumGranularity, BaseOrigin,
                         RankType, ItemId, PayloadJson)
                    VALUES (?, 'old', NULL, 1, NULL, NULL,
                            1, 1, 1, 1, 1, NULL, NULL, '{}');
                    """,
                    generationId.ToString("D"));
            }
            MemoryArchiveData source = SourceData(
                "Main",
                [Record(SourceAccountId, "undoable", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: false);
            Assert.True(plan.Undo.IsEligibleCandidate);
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult applied = await store.ApplyAsync(
                Request(source, plan));
            using (SQLiteConnection connection = context.OpenRawConnection())
            {
                Assert.Equal(
                    1,
                    connection.ExecuteScalar<long>(
                        "SELECT SourceEpoch FROM ChronicleScopeStates WHERE ScopeKind = 1 AND ScopeId = ?;",
                        account.Id.ToString("D")));
                Assert.Equal(
                    1,
                    connection.ExecuteScalar<int>(
                        "SELECT IsInvalidated FROM ChronicleGenerations WHERE GenerationId = ?;",
                        generationId.ToString("D")));
                Assert.Equal(
                    0,
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM ChronicleEntries WHERE GenerationId = ?;",
                        generationId.ToString("D")));
            }
            DateTimeOffset now =
                DateTimeOffset.Parse("2026-10-23T16:10:00+08:00");
            ArchiveOperationResult undone =
                await ((IArchiveOperationHistoryStore)store).UndoLatestAsync(
                    new ArchiveUndoRequest(
                        OperationId.New(),
                        archive.Id,
                        now,
                        now.AddSeconds(1)));

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
            Assert.Null(await store.GetCurrentAsync(
                new GachaFactReference(account.Id, "undoable")));
            using (SQLiteConnection connection = context.OpenRawConnection())
            {
                Assert.Equal(
                    2,
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM DataChangeSets;"));
                Assert.Equal(
                    0,
                    connection.ExecuteScalar<int>(
                        "SELECT COUNT(*) FROM UndoMaterials;"));
            }
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task CrossDomainMergePreservesSourceRevisionsAsNonUndoableHistory()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            source.AddDomainFixture(SourceArchiveId, SourceAccountId);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult applied = await store.ApplyAsync(
                Request(source, plan));

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Assert.Equal(6, applied.Receipt!.FactCount);
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                3,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveDomainFacts;"));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GachaRevisions;"));
            Assert.Equal(
                3,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveDomainRevisions;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT IsUndoEligible FROM OperationHistory;"));
            Assert.Equal(
                1,
                connection.ExecuteScalar<long>(
                    "SELECT Version FROM GachaRecords;"));
            Assert.Equal(
                2,
                connection.ExecuteScalar<long>(
                    "SELECT AfterVersion FROM GachaRevisions;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task ConfirmedTombstoneReintroductionDeactivatesExactMarker()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            var archive = SqliteRepositoryTestContext.CreateArchive(
                "Main",
                TargetArchiveId);
            var account = SqliteRepositoryTestContext.CreateAccount(
                archive.Id,
                Role.NaturalIdentity.Uid,
                GameServerRegion.ChinaOfficial,
                "Traveler",
                TargetAccountId,
                roleIdentity: Role);
            await context.Archives.AddAsync(archive);
            await context.Accounts.AddAsync(account);
            await context.Gacha.SaveBatchAsync(
                [Record(account.Id, "deleted", "Furina")]);
            GachaFactState current = Assert.IsType<GachaFactState>(
                await context.AtomicGacha.GetCurrentAsync(
                    new GachaFactReference(account.Id, "deleted")));
            DateTimeOffset now =
                DateTimeOffset.Parse("2026-10-23T15:00:00+08:00");
            await context.AtomicGacha.IrreversiblyDeleteAsync(
                new GachaIrreversibleDeleteRequest(
                    OperationId.New(),
                    new GachaFactReference(account.Id, "deleted"),
                    current.Version,
                    now,
                    now));
            MemoryArchiveData source = SourceData(
                "Main",
                [Record(SourceAccountId, "deleted", "Furina")]);
            var factory = CreateTargetFactory(context, root);
            var planner = new PlanArchiveRestore(factory, TimeProvider.System);
            ArchiveRestorePlan warning = await planner.PlanAsync(
                new ArchiveRestorePlanRequest(source));
            ArchiveRestoreConfirmation[] confirmations = warning.Confirmations
                .Where(item => !item.Satisfied)
                .Select(item => new ArchiveRestoreConfirmation(
                    item.Kind,
                    item.Reference,
                    item.MarkerKey,
                    item.MarkerVersion))
                .ToArray();
            ArchiveRestorePlan confirmed = await planner.PlanAsync(
                new ArchiveRestorePlanRequest(
                    source,
                    Confirmations: confirmations));
            Assert.True(confirmed.CanApply);
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult applied = await store.ApplyAsync(
                Request(source, confirmed));

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Assert.NotNull(await store.GetCurrentAsync(
                new GachaFactReference(account.Id, "deleted")));
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT IsActive FROM LocalOnlyTombstones;"));
            Assert.Equal(
                applied.Receipt!.ChangeSetId!.Value.ToString("D"),
                connection.ExecuteScalar<string>(
                    "SELECT ReintroducedByChangeSetId FROM LocalOnlyTombstones;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task InsufficientStorageFailsBeforeAnyArchiveWrite()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            var store = new SqliteGachaAtomicChangeStore(
                context.Database,
                new FixedCapacityProvider(0));

            IOException exception = await Assert.ThrowsAsync<IOException>(() =>
                store.ApplyAsync(Request(source, plan)));

            Assert.Contains("not enough storage", exception.Message);
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task CrossArchiveMergeIsOneChangeSetAndOneUndo()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            Guid sourceArchive2 = Guid.Parse(
                "20000000-0000-0000-0000-000000000011");
            Guid sourceAccount2 = Guid.Parse(
                "20000000-0000-0000-0000-000000000012");
            Guid targetArchive2 = Guid.Parse(
                "20000000-0000-0000-0000-000000000013");
            Guid targetAccount2 = Guid.Parse(
                "20000000-0000-0000-0000-000000000014");
            GameRoleIdentity role2 = GenshinGameRoleIdentity.CreateIdentity(
                "100000002",
                GameServerRegion.ChinaOfficial);
            PlayerArchive firstArchive =
                SqliteRepositoryTestContext.CreateArchive(
                    "First",
                    TargetArchiveId);
            PlayerArchive secondArchive =
                SqliteRepositoryTestContext.CreateArchive(
                    "Second",
                    targetArchive2);
            GameAccount firstAccount =
                SqliteRepositoryTestContext.CreateAccount(
                    firstArchive.Id,
                    Role.NaturalIdentity.Uid,
                    GameServerRegion.ChinaOfficial,
                    "First",
                    TargetAccountId,
                    roleIdentity: Role);
            GameAccount secondAccount =
                SqliteRepositoryTestContext.CreateAccount(
                    secondArchive.Id,
                    role2.NaturalIdentity.Uid,
                    GameServerRegion.ChinaOfficial,
                    "Second",
                    targetAccount2,
                    roleIdentity: role2);
            await context.Archives.AddAsync(firstArchive);
            await context.Archives.AddAsync(secondArchive);
            await context.Accounts.AddAsync(firstAccount);
            await context.Accounts.AddAsync(secondAccount);
            var source = MemoryArchiveData.Source(
                new ArchiveCatalog(
                    [new ArchiveCatalogRoleIdentity(
                         Role.Id.Value,
                         Role.Id.Value,
                         Role.NaturalIdentity),
                     new ArchiveCatalogRoleIdentity(
                         role2.Id.Value,
                         role2.Id.Value,
                         role2.NaturalIdentity)],
                    [Catalog(
                            SourceArchiveId,
                            SourceAccountId,
                            "First",
                            new ArchiveCatalogRoleIdentity(
                                Role.Id.Value,
                                Role.Id.Value,
                                Role.NaturalIdentity))
                        .Archives.Single(),
                     Catalog(
                            sourceArchive2,
                            sourceAccount2,
                            "Second",
                            new ArchiveCatalogRoleIdentity(
                                role2.Id.Value,
                                role2.Id.Value,
                                role2.NaturalIdentity))
                        .Archives.Single()]),
                new ArchivePortableSettings(
                    true,
                    [new ArchivePortableArchiveSetting(SourceArchiveId, 3),
                     new ArchivePortableArchiveSetting(sourceArchive2, 3)]));
            source.Gacha[SourceAccountId] =
                [Record(SourceAccountId, "first", "Furina")];
            source.Gacha[sourceAccount2] =
                [Record(sourceAccount2, "second", "Neuvillette")];
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: false);
            Assert.True(plan.Undo.IsEligibleCandidate);
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult applied = await store.ApplyAsync(
                Request(source, plan));
            DateTimeOffset now =
                DateTimeOffset.Parse("2026-10-23T16:20:00+08:00");
            ArchiveOperationResult undone =
                await ((IArchiveOperationHistoryStore)store).UndoLatestAsync(
                    new ArchiveUndoRequest(
                        OperationId.New(),
                        firstArchive.Id,
                        now,
                        now.AddSeconds(1)));

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
            Assert.Null(await store.GetCurrentAsync(
                new GachaFactReference(firstAccount.Id, "first")));
            Assert.Null(await store.GetCurrentAsync(
                new GachaFactReference(secondAccount.Id, "second")));
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                2,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSetArchives WHERE ChangeSetId = ?;",
                    applied.Receipt!.ChangeSetId!.Value.ToString("D")));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Fact]
    public async Task ConfirmedPortableSettingsAreAppliedInSameTransaction()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            ArchiveCatalog catalog = Catalog(
                SourceArchiveId,
                SourceAccountId,
                "Settings",
                new ArchiveCatalogRoleIdentity(
                    Role.Id.Value,
                    Role.Id.Value,
                    Role.NaturalIdentity));
            MemoryArchiveData source = MemoryArchiveData.Source(
                catalog,
                new ArchivePortableSettings(
                    false,
                    [new ArchivePortableArchiveSetting(SourceArchiveId, 9)]));
            source.Gacha[SourceAccountId] = [];
            var planner = new PlanArchiveRestore(
                CreateTargetFactory(context, root),
                TimeProvider.System);
            ArchiveRestorePlan warning = await planner.PlanAsync(
                new ArchiveRestorePlanRequest(
                    source,
                    ApplyPortableSettings: true));
            ArchiveRestoreConfirmation[] confirmations = warning.Confirmations
                .Where(item => !item.Satisfied)
                .Select(item => new ArchiveRestoreConfirmation(
                    item.Kind,
                    item.Reference,
                    item.MarkerKey,
                    item.MarkerVersion))
                .ToArray();
            ArchiveRestorePlan confirmed = await planner.PlanAsync(
                new ArchiveRestorePlanRequest(
                    source,
                    Confirmations: confirmations,
                    ApplyPortableSettings: true));
            Assert.True(confirmed.CanApply);
            var store = new SqliteGachaAtomicChangeStore(context.Database);

            ArchiveRestoreApplyResult applied = await store.ApplyAsync(
                Request(source, confirmed));

            Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
            Guid archiveId =
                applied.Receipt!.Archives.Single().TargetArchiveId;
            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT RequireUniqueArchiveNames FROM PortableApplicationSettings WHERE Id = 1;"));
            Assert.Equal(
                9,
                connection.ExecuteScalar<int>(
                    "SELECT UndoLimit FROM ArchiveUndoSettings WHERE ArchiveId = ?;",
                    archiveId.ToString("D")));
            Assert.Equal(
                1,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    [Theory]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterParents)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterGacha)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterItemObservations)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterManualAcquisitions)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterChallenges)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterSourceRevisions)]
    [InlineData((int)ArchiveRestoreFailurePoint.BeforeReceipt)]
    [InlineData((int)ArchiveRestoreFailurePoint.AfterReceipt)]
    [InlineData((int)ArchiveRestoreFailurePoint.BeforeCommit)]
    public async Task InjectedFailureRollsBackWholeArchive(
        int failurePointValue)
    {
        var failurePoint = (ArchiveRestoreFailurePoint)failurePointValue;
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = CreateRestoreSnapshotRoot();
        try
        {
            MemoryArchiveData source = SourceData(
                "Restored",
                [Record(SourceAccountId, "1", "Furina")]);
            source.AddDomainFixture(SourceArchiveId, SourceAccountId);
            ArchiveRestorePlan plan = await PlanForTargetAsync(
                context,
                root,
                source,
                confirmNonUndoable: true);
            var store = new SqliteGachaAtomicChangeStore(
                context.Database,
                new UnlimitedCapacityProvider(),
                new ThrowingArchiveRestoreFailureInjector(failurePoint));

            await Assert.ThrowsAsync<InjectedArchiveRestoreException>(() =>
                store.ApplyAsync(Request(source, plan)));

            using SQLiteConnection connection = context.OpenRawConnection();
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM PlayerArchives;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GameAccounts;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GachaRecords;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveDomainFacts;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM ArchiveRestoreReceipts;"));
            Assert.Equal(
                0,
                connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM DataChangeSets;"));
        }
        finally
        {
            DeleteRestoreSnapshotRoot(root);
        }
    }

    private static async Task<ArchiveRestorePlan> PlanForTargetAsync(
        SqliteRepositoryTestContext context,
        string root,
        MemoryArchiveData source,
        bool confirmNonUndoable)
    {
        SqliteArchiveRestoreTargetSnapshotFactory factory =
            CreateTargetFactory(context, root);
        var planner = new PlanArchiveRestore(factory, TimeProvider.System);
        IReadOnlyList<ArchiveRestoreConfirmation>? confirmations =
            confirmNonUndoable
                ? [new ArchiveRestoreConfirmation(
                    ArchiveRestoreConfirmationKind.NonUndoableOperation,
                    source.PackageFingerprint)]
                : null;
        ArchiveRestorePlan plan = await planner.PlanAsync(
            new ArchiveRestorePlanRequest(
                source,
                Confirmations: confirmations));
        Assert.True(plan.CanApply);
        Assert.Equal(8, plan.TargetSchemaVersion);
        return plan;
    }

    private static SqliteArchiveRestoreTargetSnapshotFactory
        CreateTargetFactory(
            SqliteRepositoryTestContext context,
            string root) =>
        new(new SqliteArchiveExportSnapshotFactory(
            context.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(root)));

    private static ArchiveRestoreApplyRequest Request(
        MemoryArchiveData source,
        ArchiveRestorePlan plan)
    {
        DateTimeOffset now =
            DateTimeOffset.Parse("2026-10-23T16:00:00+08:00");
        return new ArchiveRestoreApplyRequest(
            source,
            plan,
            OperationId.New(),
            now,
            now);
    }

    private static string CreateRestoreSnapshotRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteRestoreSnapshotRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class ThrowingArchiveRestoreFailureInjector(
        ArchiveRestoreFailurePoint failurePoint)
        : IArchiveRestoreFailureInjector
    {
        public void ThrowIfRequested(ArchiveRestoreFailurePoint point)
        {
            if (point == failurePoint)
            {
                throw new InjectedArchiveRestoreException(point);
            }
        }
    }

    private sealed class InjectedArchiveRestoreException(
        ArchiveRestoreFailurePoint failurePoint)
        : Exception($"Injected Archive restore failure at {failurePoint}.");

    private sealed class UnlimitedCapacityProvider
        : IHistoryStorageCapacityProvider
    {
        public HistoryStorageCapacity GetCapacity() => new(long.MaxValue);
    }

    private sealed class FixedCapacityProvider(long availableBytes)
        : IHistoryStorageCapacityProvider
    {
        public HistoryStorageCapacity GetCapacity() => new(availableBytes);
    }

    private sealed class ArchiveRestoreChangeSetIdRow
    {
        public string ChangeSetId { get; set; } = string.Empty;

        public Guid Parse() => Guid.Parse(ChangeSetId);
    }
}

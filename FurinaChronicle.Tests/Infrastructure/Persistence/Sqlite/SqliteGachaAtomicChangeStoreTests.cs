// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGachaAtomicChangeStoreTests
{
    [Fact]
    public async Task CommitAsync_InsertPersistsFactChangeSetRevisionAndUndoMaterial()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaRecord record = CreateRecord(accountId, "1001");
        GachaAtomicChangeRequest request = CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "1001"),
                record));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.CommitAsync(request);

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.NotNull(result.ChangeSetId);
        GachaFactState state = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(
                new GachaFactReference(accountId, "1001")));
        Assert.Equal(1, state.Version.Value);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "DataChangeSets"));
        Assert.Equal(1, Count(raw, "GachaRevisions"));
        Assert.Equal(1, Count(raw, "UndoMaterials"));
        Assert.Equal(1, Count(raw, "OperationCommitResults"));
    }

    [Fact]
    public async Task CommitAsync_SameOperationIdReturnsPriorResultWithoutDuplicateWrites()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaAtomicChangeRequest request = CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "1001"),
                CreateRecord(accountId, "1001")));

        GachaAtomicChangeResult first =
            await context.AtomicGacha.CommitAsync(request);
        GachaAtomicChangeResult second =
            await context.AtomicGacha.CommitAsync(request);

        Assert.Equal(ChangeExecutionStatus.Applied, first.Status);
        Assert.Equal(ChangeExecutionStatus.AlreadyCommitted, second.Status);
        Assert.Equal(ChangeExecutionStatus.Applied, second.OriginalStatus);
        Assert.Equal(first.ChangeSetId, second.ChangeSetId);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "GachaRecords"));
        Assert.Equal(1, Count(raw, "DataChangeSets"));
        Assert.Equal(1, Count(raw, "GachaRevisions"));
    }

    [Fact]
    public async Task CommitAsync_AcquisitionOnlyUpdateAdvancesVersionWithoutBusinessRevision()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState before = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        DateTimeOffset fetchedAt =
            new(2026, 10, 6, 15, 0, 0, TimeSpan.FromHours(8));
        GachaRecord refreshed = before.Record with
        {
            Provenance = new RecordProvenance(
                before.Record.Provenance.Origin,
                new RecordTimestamps(FetchedAt: fetchedAt),
                acquisitionBatchId: AcquisitionBatchId.New())
        };

        GachaAtomicChangeResult result =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Refresh,
                new GachaFactMutation(
                    reference,
                    refreshed,
                    before.Version)));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Null(result.ChangeSetId);
        GachaFactState after = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal(2, after.Version.Value);
        Assert.Equal(fetchedAt, after.Record.Provenance.Timestamps.FetchedAt);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "DataChangeSets"));
        Assert.Equal(1, Count(raw, "GachaRevisions"));
        Assert.Equal(2, Count(raw, "OperationCommitResults"));
    }

    [Fact]
    public async Task CommitAsync_SubstantiveUpdateCreatesRevisionWithBeforeAndAfter()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Correction,
                new GachaFactMutation(
                    reference,
                    current.Record with { ItemName = "Corrected" },
                    current.Version)));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        GachaFactState corrected = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Corrected", corrected.Record.ItemName);
        Assert.Equal(2, corrected.Version.Value);
        using SQLiteConnection raw = context.OpenRawConnection();
        SnapshotPairRow revision = Assert.Single(
            raw.Query<SnapshotPairRow>(
                """
                SELECT BeforeSnapshotJson, AfterSnapshotJson
                FROM GachaRevisions
                WHERE ChangeSetId = ?;
                """,
                result.ChangeSetId!.Value.ToString("D")));
        Assert.Contains("Furina", revision.BeforeSnapshotJson);
        Assert.Contains("Corrected", revision.AfterSnapshotJson);
    }

    [Fact]
    public async Task CommitAsync_RevisionFailureRollsBackBusinessAndHistory()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        await context.Database.InitializeAsync();
        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            raw.Execute(
                """
                CREATE TRIGGER FailGachaRevision
                BEFORE INSERT ON GachaRevisions
                BEGIN
                    SELECT RAISE(ABORT, 'forced revision failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<SQLiteException>(
            () => context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Import,
                new GachaFactMutation(
                    new GachaFactReference(accountId, "1001"),
                    CreateRecord(accountId, "1001")))));

        using SQLiteConnection reopened = context.OpenRawConnection();
        Assert.Equal(0, Count(reopened, "GachaRecords"));
        Assert.Equal(0, Count(reopened, "DataChangeSets"));
        Assert.Equal(0, Count(reopened, "EntityChanges"));
        Assert.Equal(0, Count(reopened, "OperationCommitResults"));
    }

    [Fact]
    public async Task CommitAsync_OldWriterAtoBtoADetectsVersionConflict()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaRecord original = CreateRecord(accountId, "1001");
        await context.Gacha.SaveBatchAsync([original]);
        GachaFactReference reference = new(accountId, "1001");
        GachaFactState baseline = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.Gacha.SaveBatchAsync(
            [original with { ItemName = "Changed" }],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);
        await context.Gacha.SaveBatchAsync(
            [original],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);

        GachaAtomicChangeResult result =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Correction,
                new GachaFactMutation(
                    reference,
                    original with { ItemName = "Proposed" },
                    baseline.Version)));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        GachaFactState final = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Furina", final.Record.ItemName);
        Assert.Equal(3, final.Version.Value);
    }

    [Fact]
    public async Task UndoLatestAsync_InsertCreatesInverseChangeSetAndRemovesFact()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState inserted = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(inserted.ArchiveId));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(reference));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(2, Count(raw, "DataChangeSets"));
        Assert.Equal(2, Count(raw, "GachaRevisions"));
        Assert.Equal(0, Count(raw, "UndoMaterials"));
        Assert.Equal(
            (int)UndoIneligibilityReason.AlreadyUndone,
            raw.ExecuteScalar<int>(
                """
                SELECT IneligibilityReason
                FROM OperationHistory
                WHERE UndoneByChangeSetId IS NOT NULL;
                """));
    }

    [Fact]
    public async Task UndoLatestAsync_UpdateRestoresOriginalFactAndProvenanceWithNewVersion()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState before = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        DateTimeOffset importedAt =
            new(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(8));
        GachaRecord corrected = before.Record with
        {
            ItemName = "Incorrect",
            Provenance = new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ImportedAt: importedAt))
        };
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Correction,
            new GachaFactMutation(reference, corrected, before.Version)));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(before.ArchiveId));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        GachaFactState restored = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Furina", restored.Record.ItemName);
        Assert.Equal(
            DataOrigin.StandardImport,
            restored.Record.Provenance.Origin);
        Assert.Null(restored.Record.Provenance.Timestamps.ImportedAt);
        Assert.Equal(3, restored.Version.Value);
    }

    [Fact]
    public async Task UndoLatestAsync_DeleteRestoresOriginalFact()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Delete,
            new GachaFactMutation(reference, null, current.Version)));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(current.ArchiveId));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        GachaFactState restored = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Furina", restored.Record.ItemName);
        Assert.Equal(2, restored.Version.Value);
    }

    [Fact]
    public async Task UndoLatestAsync_LaterLegacyWriteReturnsConflictWithoutPartialChange()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        Guid accountId = await AddAccountAsync(context);
        GachaRecord record = CreateRecord(accountId, "1001");
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(reference, record)));
        GachaFactState inserted = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.Gacha.SaveBatchAsync(
            [record with { ItemName = "Later" }],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);

        GachaAtomicChangeResult result =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(inserted.ArchiveId));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Later", current.Record.ItemName);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "DataChangeSets"));
    }

    [Fact]
    public async Task UndoLatestAsync_CrossArchiveOperationIsRevertedAsOneUnit()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid firstArchiveId, Guid firstAccountId) =
            await AddArchiveAccountAsync(context, "First");
        (_, Guid secondAccountId) =
            await AddArchiveAccountAsync(context, "Second");
        GachaFactReference first = new(firstAccountId, "1001");
        GachaFactReference second = new(secondAccountId, "2001");
        GachaAtomicChangeResult committed =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Import,
                new GachaFactMutation(
                    first,
                    CreateRecord(firstAccountId, "1001")),
                new GachaFactMutation(
                    second,
                    CreateRecord(secondAccountId, "2001"))));

        GachaAtomicChangeResult undone =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(firstArchiveId));

        Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(first));
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(second));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(
            2,
            raw.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM DataChangeSetArchives
                WHERE ChangeSetId = ?;
                """,
                committed.ChangeSetId!.Value.ToString("D")));
        Assert.Equal(
            2,
            raw.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM DataChangeSetArchives
                WHERE ChangeSetId = ?;
                """,
                undone.ChangeSetId!.Value.ToString("D")));
    }

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
        (_, Guid accountId) =
            await AddArchiveAccountAsync(context, "测试档案");
        return accountId;
    }

    private static async Task<(Guid ArchiveId, Guid AccountId)>
        AddArchiveAccountAsync(
            SqliteRepositoryTestContext context,
            string archiveName)
    {
        var archive =
            SqliteRepositoryTestContext.CreateArchive(archiveName);
        var account =
            SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return (archive.Id, account.Id);
    }

    private static GachaAtomicChangeRequest CreateRequest(
        DataChangeOperationKind operationKind,
        params GachaFactMutation[] mutations)
    {
        DateTimeOffset time =
            new(2026, 10, 6, 14, 0, 0, TimeSpan.FromHours(8));
        return new GachaAtomicChangeRequest(
            OperationId.New(),
            operationKind,
            DataOrigin.FurinaImport,
            "test change",
            time,
            time.AddSeconds(1),
            mutations);
    }

    private static GachaRecord CreateRecord(
        Guid accountId,
        string externalRecordId) =>
        new(
            accountId,
            externalRecordId,
            "Furina",
            5,
            new DateTimeOffset(
                2026,
                10,
                1,
                12,
                0,
                0,
                TimeSpan.FromHours(8)))
        {
            ItemId = "10000089",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(DataOrigin.StandardImport)
        };

    private static GachaUndoRequest CreateUndoRequest(Guid archiveId)
    {
        DateTimeOffset time =
            new(2026, 10, 6, 16, 0, 0, TimeSpan.FromHours(8));
        return new GachaUndoRequest(
            OperationId.New(),
            archiveId,
            time,
            time.AddSeconds(1));
    }

    private static int Count(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table};");

    private sealed class SnapshotPairRow
    {
        public string BeforeSnapshotJson { get; set; } = string.Empty;

        public string AfterSnapshotJson { get; set; } = string.Empty;
    }
}

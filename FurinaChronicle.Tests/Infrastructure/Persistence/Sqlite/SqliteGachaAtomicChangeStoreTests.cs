// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGachaAtomicChangeStoreTests
{
    [Theory]
    [InlineData(DataChangeOperationKind.Undo)]
    [InlineData(DataChangeOperationKind.IrreversibleDelete)]
    public void Request_DedicatedOperationKindIsRejected(
        DataChangeOperationKind operationKind)
    {
        Guid accountId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => CreateRequest(
            operationKind,
            new GachaFactMutation(
                new GachaFactReference(accountId, "1001"),
                CreateRecord(accountId, "1001"))));
    }

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
    public async Task CommitAsync_ExistingFactWithoutExpectedVersionConflicts()
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

        GachaAtomicChangeResult result =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Correction,
                new GachaFactMutation(
                    reference,
                    CreateRecord(accountId, "1001") with
                    {
                        ItemName = "Blind overwrite"
                    })));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Furina", current.Record.ItemName);
    }

    [Fact]
    public async Task CommitAsync_DeleteThenReinsertAdvancesStableReferenceVersion()
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
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Delete,
            new GachaFactMutation(reference, null, inserted.Version)));

        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));

        GachaFactState reinserted = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal(2, reinserted.Version.Value);
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
    public async Task UndoLatestAsync_SameVersionButChangedContentConflicts()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "After content");
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Correction,
            new GachaFactMutation(
                reference,
                current.Record with { ItemName = "Corrected" },
                current.Version)));
        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            raw.Execute(
                "UPDATE GachaRecords SET ItemName = ? WHERE GameAccountId = ? AND ExternalRecordId = ?;",
                "Changed without version",
                accountId.ToString("D"),
                reference.ExternalRecordId);
        }

        GachaAtomicChangeResult result =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(archiveId));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        GachaFactState final = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Changed without version", final.Record.ItemName);
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

    [Fact]
    public async Task UndoLimit_DefaultIsThree_ZeroRetainsOldMaterialAndStopsNewCapture()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "History");
        Assert.Equal(
            UndoRetentionPolicy.DefaultLimit,
            await context.AtomicGacha.GetUndoLimitAsync(archiveId));
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "1001"),
                CreateRecord(accountId, "1001"))));

        UndoLimitChangeResult changed =
            await context.AtomicGacha.SetUndoLimitAsync(archiveId, 0);
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "1002"),
                CreateRecord(accountId, "1002"))));

        Assert.Equal(3, changed.PreviousLimit);
        Assert.Equal(0, changed.CurrentLimit);
        Assert.Empty(changed.EvictedChangeSetIds);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "UndoMaterials"));
        OperationHistoryItem[] history =
            (await context.AtomicGacha.GetHistoryAsync(archiveId, 10))
            .ToArray();
        Assert.Equal(2, history.Length);
        Assert.False(history[0].IsUndoEligible);
        Assert.Equal(
            UndoIneligibilityReason.Disabled,
            history[0].IneligibilityReason);
        Assert.True(history[1].IsUndoEligible);
    }

    [Fact]
    public async Task UndoLimit_LoweringImmediatelyEvictsOldestMaterialButKeepsRevisions()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "History");
        for (int index = 1; index <= 3; index++)
        {
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Import,
                new GachaFactMutation(
                    new GachaFactReference(
                        accountId,
                        $"100{index}"),
                    CreateRecord(accountId, $"100{index}"))));
        }

        UndoLimitChangeResult result =
            await context.AtomicGacha.SetUndoLimitAsync(archiveId, 1);

        Assert.Equal(2, result.EvictedChangeSetIds.Count);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "UndoMaterials"));
        Assert.Equal(3, Count(raw, "GachaRevisions"));
        Assert.Equal(
            2,
            raw.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM OperationHistory
                WHERE IneligibilityReason = ?;
                """,
                (int)UndoIneligibilityReason.CapacityEvicted));
    }

    [Fact]
    public async Task UndoLimit_AboveMaximumIsRejectedWithoutChangingConfiguration()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, _) =
            await AddArchiveAccountAsync(context, "History");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => context.AtomicGacha.SetUndoLimitAsync(
                archiveId,
                UndoRetentionPolicy.MaximumLimit + 1));

        Assert.Equal(
            UndoRetentionPolicy.DefaultLimit,
            await context.AtomicGacha.GetUndoLimitAsync(archiveId));
    }

    [Fact]
    public async Task CommitAsync_LowCapacityRequiresConfirmationAndRejectingChangesNothing()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "Capacity");
        string largeName = new('A', 24_000);
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "large"),
                CreateRecord(accountId, "large") with
                {
                    ItemName = largeName
                })));
        var constrained = new SqliteGachaAtomicChangeStore(
            context.Database,
            new FixedCapacityProvider(0));
        GachaAtomicChangeRequest request = CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "small"),
                CreateRecord(accountId, "small")));

        GachaAtomicChangeResult result =
            await constrained.CommitAsync(request);

        Assert.Equal(
            ChangeExecutionStatus.NeedsConfirmation,
            result.Status);
        HistoryCleanupPlan plan = Assert.IsType<HistoryCleanupPlan>(
            result.CleanupPlan);
        Assert.NotEmpty(plan.Candidates);
        Assert.Contains(
            plan.Candidates,
            candidate => candidate.ArchiveIds.Contains(archiveId));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "GachaRecords"));
        Assert.Equal(1, Count(raw, "UndoMaterials"));
        Assert.Equal(1, Count(raw, "DataChangeSets"));
    }

    [Fact]
    public async Task CommitAsync_ConfirmedCleanupIsRevalidatedAndCommittedAtomically()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (_, Guid accountId) =
            await AddArchiveAccountAsync(context, "Capacity");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                new GachaFactReference(accountId, "large"),
                CreateRecord(accountId, "large") with
                {
                    ItemName = new string('A', 24_000)
                })));
        var constrained = new SqliteGachaAtomicChangeStore(
            context.Database,
            new FixedCapacityProvider(0));
        OperationId operationId = OperationId.New();
        DateTimeOffset time =
            new(2026, 10, 6, 17, 0, 0, TimeSpan.FromHours(8));
        GachaFactMutation mutation = new(
            new GachaFactReference(accountId, "small"),
            CreateRecord(accountId, "small"));
        var initial = new GachaAtomicChangeRequest(
            operationId,
            DataChangeOperationKind.Import,
            DataOrigin.FurinaImport,
            "capacity test",
            time,
            time.AddSeconds(1),
            [mutation]);
        GachaAtomicChangeResult pending =
            await constrained.CommitAsync(initial);
        HistoryCleanupPlan plan = Assert.IsType<HistoryCleanupPlan>(
            pending.CleanupPlan);
        var confirmed = new GachaAtomicChangeRequest(
            operationId,
            DataChangeOperationKind.Import,
            DataOrigin.FurinaImport,
            "capacity test",
            time,
            time.AddSeconds(1),
            [mutation],
            cleanupConfirmationId: plan.ConfirmationId);

        GachaAtomicChangeResult result =
            await constrained.CommitAsync(confirmed);

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.NotNull(
            await constrained.GetCurrentAsync(
                new GachaFactReference(accountId, "small")));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(2, Count(raw, "GachaRecords"));
        Assert.Equal(
            1,
            raw.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM OperationHistory
                WHERE IneligibilityReason = ?;
                """,
                (int)UndoIneligibilityReason.CapacityEvicted));
    }

    [Fact]
    public async Task IrreversiblyDeleteAsync_RemovesFactRecoveryMaterialAndCreatesMinimalTombstone()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "Delete");
        GachaFactReference reference = new(accountId, "secret-record");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, reference.ExternalRecordId))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));

        GachaAtomicChangeResult result =
            await context.AtomicGacha.IrreversiblyDeleteAsync(
                CreateIrreversibleDeleteRequest(
                    reference,
                    current.Version));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(reference));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(0, Count(raw, "GachaRevisions"));
        Assert.Equal(0, Count(raw, "UndoMaterials"));
        TombstoneInspectionRow tombstone = Assert.Single(
            raw.Query<TombstoneInspectionRow>(
                """
                SELECT TombstoneKey, TombstoneVersion, ArchiveId, IsActive
                FROM LocalOnlyTombstones;
                """));
        Assert.Equal(64, tombstone.TombstoneKey.Length);
        Assert.DoesNotContain(
            reference.ExternalRecordId,
            tombstone.TombstoneKey,
            StringComparison.Ordinal);
        Assert.Equal(archiveId.ToString("D"), tombstone.ArchiveId);
        Assert.True(tombstone.IsActive);

        GachaAtomicChangeResult undo =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(archiveId));
        Assert.Equal(ChangeExecutionStatus.Conflict, undo.Status);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(reference));
    }

    [Fact]
    public async Task CommitAsync_ActiveTombstoneSuppressesChangedContentUntilExplicitConfirmation()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (_, Guid accountId) =
            await AddArchiveAccountAsync(context, "Delete");
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.AtomicGacha.IrreversiblyDeleteAsync(
            CreateIrreversibleDeleteRequest(reference, current.Version));
        OperationId operationId = OperationId.New();
        DateTimeOffset time =
            new(2026, 10, 6, 18, 0, 0, TimeSpan.FromHours(8));
        GachaFactMutation mutation = new(
            reference,
            CreateRecord(accountId, "1001") with
            {
                ItemName = "Changed after deletion"
            });
        var automatic = new GachaAtomicChangeRequest(
            operationId,
            DataChangeOperationKind.Import,
            DataOrigin.StandardImport,
            "automatic reintroduction",
            time,
            time.AddSeconds(1),
            [mutation]);

        GachaAtomicChangeResult suppressed =
            await context.AtomicGacha.CommitAsync(automatic);

        Assert.Equal(ChangeExecutionStatus.Suppressed, suppressed.Status);
        TombstoneReintroductionWarning warning =
            Assert.IsType<TombstoneReintroductionWarning>(
                suppressed.ReintroductionWarning);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(reference));
        var explicitRequest = new GachaAtomicChangeRequest(
            operationId,
            DataChangeOperationKind.Import,
            DataOrigin.StandardImport,
            "explicit reintroduction",
            time,
            time.AddSeconds(1),
            [mutation],
            reintroductionConfirmations:
            [
                new TombstoneReintroductionConfirmation(
                    warning.TombstoneKey,
                    warning.TombstoneVersion,
                    reference)
            ]);

        GachaAtomicChangeResult applied =
            await context.AtomicGacha.CommitAsync(explicitRequest);

        Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
        GachaFactState restored = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Changed after deletion", restored.Record.ItemName);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(
            0,
            raw.ExecuteScalar<int>(
                "SELECT IsActive FROM LocalOnlyTombstones;"));
        Assert.Equal(
            applied.ChangeSetId!.Value.ToString("D"),
            raw.ExecuteScalar<string>(
                """
                SELECT ReintroducedByChangeSetId
                FROM LocalOnlyTombstones;
                """));
    }

    [Fact]
    public async Task UndoLatestAsync_ReintroductionRestoresOriginalActiveTombstone()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid archiveId, Guid accountId) =
            await AddArchiveAccountAsync(context, "Reintroduction undo");
        GachaFactReference reference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(CreateRequest(
            DataChangeOperationKind.Import,
            new GachaFactMutation(
                reference,
                CreateRecord(accountId, "1001"))));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        await context.AtomicGacha.IrreversiblyDeleteAsync(
            CreateIrreversibleDeleteRequest(reference, current.Version));

        GachaFactMutation mutation = new(
            reference,
            CreateRecord(accountId, "1001"));
        GachaAtomicChangeResult suppressed =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Import,
                mutation));
        TombstoneReintroductionWarning warning =
            Assert.IsType<TombstoneReintroductionWarning>(
                suppressed.ReintroductionWarning);
        DateTimeOffset time =
            new(2026, 10, 6, 18, 0, 0, TimeSpan.FromHours(8));
        var confirmed = new GachaAtomicChangeRequest(
            OperationId.New(),
            DataChangeOperationKind.Import,
            DataOrigin.StandardImport,
            "explicit reintroduction",
            time,
            time.AddSeconds(1),
            [mutation],
            reintroductionConfirmations:
            [
                new TombstoneReintroductionConfirmation(
                    warning.TombstoneKey,
                    warning.TombstoneVersion,
                    reference)
            ]);
        GachaAtomicChangeResult reintroduced =
            await context.AtomicGacha.CommitAsync(confirmed);
        Assert.Equal(ChangeExecutionStatus.Applied, reintroduced.Status);

        GachaAtomicChangeResult undo =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(archiveId));

        Assert.Equal(ChangeExecutionStatus.Applied, undo.Status);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(reference));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(
            1,
            raw.ExecuteScalar<int>(
                "SELECT IsActive FROM LocalOnlyTombstones;"));
        Assert.Null(raw.ExecuteScalar<string?>(
            "SELECT ReintroducedByChangeSetId FROM LocalOnlyTombstones;"));
    }

    [Fact]
    public async Task IrreversiblyDeleteAsync_OneFactInvalidatesWholeCrossArchiveUndo()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        (Guid firstArchiveId, Guid firstAccountId) =
            await AddArchiveAccountAsync(context, "First");
        (_, Guid secondAccountId) =
            await AddArchiveAccountAsync(context, "Second");
        GachaFactReference first = new(firstAccountId, "1001");
        GachaFactReference second = new(secondAccountId, "2001");
        GachaAtomicChangeResult imported =
            await context.AtomicGacha.CommitAsync(CreateRequest(
                DataChangeOperationKind.Import,
                new GachaFactMutation(
                    first,
                    CreateRecord(firstAccountId, "1001")),
                new GachaFactMutation(
                    second,
                    CreateRecord(secondAccountId, "2001"))));
        GachaFactState firstState = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(first));

        await context.AtomicGacha.IrreversiblyDeleteAsync(
            CreateIrreversibleDeleteRequest(
                first,
                firstState.Version));

        Assert.Null(await context.AtomicGacha.GetCurrentAsync(first));
        Assert.NotNull(await context.AtomicGacha.GetCurrentAsync(second));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(
            (int)UndoIneligibilityReason.IrreversibleDeletion,
            raw.ExecuteScalar<int>(
                """
                SELECT IneligibilityReason
                FROM OperationHistory
                WHERE ChangeSetId = ?;
                """,
                imported.ChangeSetId!.Value.ToString("D")));
        Assert.Equal(
            0,
            raw.ExecuteScalar<int>(
                """
                SELECT COUNT(*)
                FROM UndoMaterials
                WHERE ChangeSetId = ?;
                """,
                imported.ChangeSetId.Value.ToString("D")));

        GachaAtomicChangeResult undo =
            await context.AtomicGacha.UndoLatestAsync(
                CreateUndoRequest(firstArchiveId));
        Assert.Equal(ChangeExecutionStatus.Conflict, undo.Status);
        Assert.NotNull(await context.AtomicGacha.GetCurrentAsync(second));
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

    private static GachaIrreversibleDeleteRequest
        CreateIrreversibleDeleteRequest(
            GachaFactReference reference,
            FactVersion expectedVersion)
    {
        DateTimeOffset time =
            new(2026, 10, 6, 18, 0, 0, TimeSpan.FromHours(8));
        return new GachaIrreversibleDeleteRequest(
            OperationId.New(),
            reference,
            expectedVersion,
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

    private sealed class FixedCapacityProvider(long availableBytes)
        : IHistoryStorageCapacityProvider
    {
        public HistoryStorageCapacity GetCapacity() =>
            new(availableBytes);
    }

    private sealed class TombstoneInspectionRow
    {
        public string TombstoneKey { get; set; } = string.Empty;

        public long TombstoneVersion { get; set; }

        public string ArchiveId { get; set; } = string.Empty;

        public bool IsActive { get; set; }
    }
}

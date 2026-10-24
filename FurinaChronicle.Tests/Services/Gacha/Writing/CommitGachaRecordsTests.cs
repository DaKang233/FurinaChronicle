// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Services.Gacha.Writing;

public sealed class CommitGachaRecordsTests
{
    [Fact]
    public async Task PreserveExisting_FillEmptyCreatesRevisionAndCanUndo()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (Guid archiveId, Guid accountId) = await AddAccountAsync(context);
        GachaRecord incomplete = CreateRecord(accountId, "1001") with
        {
            ItemId = null,
        };
        await context.Gacha.SaveBatchAsync([incomplete]);
        var service = new CommitGachaRecords(context.AtomicGacha);

        CommitGachaRecordsResult result = await service.ExecuteAsync(
            Request(
                [CreateRecord(accountId, "1001")],
                GachaRecordConflictPolicy.PreserveExisting));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Equal(1, result.UpdatedCount);
        GachaFactState saved = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(
                new GachaFactReference(accountId, "1001")));
        Assert.Equal("10000089", saved.Record.ItemId);
        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            Assert.Equal(
                1,
                raw.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM GachaRevisions;"));
        }

        GachaAtomicChangeResult undo =
            await context.AtomicGacha.UndoLatestAsync(new GachaUndoRequest(
                OperationId.New(),
                archiveId,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        Assert.Equal(ChangeExecutionStatus.Applied, undo.Status);
        GachaFactState restored = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(
                new GachaFactReference(accountId, "1001")));
        Assert.Null(restored.Record.ItemId);
    }

    [Fact]
    public async Task PreserveExisting_DifferentNonEmptyFactConflictsWithoutWrite()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, Guid accountId) = await AddAccountAsync(context);
        GachaRecord existing = CreateRecord(accountId, "1001");
        await context.Gacha.SaveBatchAsync([existing]);
        var service = new CommitGachaRecords(context.AtomicGacha);

        CommitGachaRecordsResult result = await service.ExecuteAsync(
            Request(
                [existing with { ItemName = "Different" }],
                GachaRecordConflictPolicy.PreserveExisting));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        Assert.Equal(1, result.ConflictCount);
        GachaFactState saved = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(
                new GachaFactReference(accountId, "1001")));
        Assert.Equal("Furina", saved.Record.ItemName);
    }

    [Fact]
    public async Task RefreshPolicy_SuppressesDeletedFactAndCommitsRemainingFacts()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        (_, Guid accountId) = await AddAccountAsync(context);
        GachaFactReference deletedReference = new(accountId, "1001");
        await context.AtomicGacha.CommitAsync(new GachaAtomicChangeRequest(
            OperationId.New(),
            DataChangeOperationKind.Import,
            DataOrigin.StandardImport,
            "seed",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            [new GachaFactMutation(
                deletedReference,
                CreateRecord(accountId, "1001"))]));
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(deletedReference));
        await context.AtomicGacha.IrreversiblyDeleteAsync(
            new GachaIrreversibleDeleteRequest(
                OperationId.New(),
                deletedReference,
                current.Version,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        var service = new CommitGachaRecords(context.AtomicGacha);

        CommitGachaRecordsResult result = await service.ExecuteAsync(
            Request(
                [
                    CreateRecord(accountId, "1001"),
                    CreateRecord(accountId, "1002"),
                ],
                GachaRecordConflictPolicy.ReplaceExisting,
                suppressTombstonesAndContinue: true));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Equal(1, result.InsertedCount);
        Assert.Equal(1, result.SuppressedCount);
        Assert.Null(await context.AtomicGacha.GetCurrentAsync(deletedReference));
        Assert.NotNull(await context.AtomicGacha.GetCurrentAsync(
            new GachaFactReference(accountId, "1002")));
    }

    private static CommitGachaRecordsRequest Request(
        IReadOnlyCollection<GachaRecord> records,
        GachaRecordConflictPolicy policy,
        bool suppressTombstonesAndContinue = false)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new CommitGachaRecordsRequest(
            OperationId.New(),
            DataChangeOperationKind.Import,
            DataOrigin.StandardImport,
            "test import",
            now,
            now,
            records,
            policy,
            suppressTombstonesAndContinue);
    }

    private static async Task<(Guid ArchiveId, Guid AccountId)>
        AddAccountAsync(SqliteRepositoryTestContext context)
    {
        var archive = SqliteRepositoryTestContext.CreateArchive();
        var account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        return (archive.Id, account.Id);
    }

    private static GachaRecord CreateRecord(Guid accountId, string id) =>
        new(
            accountId,
            id,
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
            Provenance = new RecordProvenance(DataOrigin.StandardImport),
        };
}

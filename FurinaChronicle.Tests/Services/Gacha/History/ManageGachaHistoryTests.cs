// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Services.Gacha.History;

public sealed class ManageGachaHistoryTests
{
    [Fact]
    public async Task CorrectAsync_MarksCurrentAsUserEnteredAndKeepsOriginalInRevision()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        var archive = SqliteRepositoryTestContext.CreateArchive();
        var account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        var reference = new GachaFactReference(account.Id, "1001");
        DateTimeOffset importedAt =
            new(2026, 10, 7, 10, 0, 0, TimeSpan.FromHours(8));
        var original = new GachaRecord(
            account.Id,
            reference.ExternalRecordId,
            "Old Name",
            5,
            importedAt.AddDays(-1))
        {
            ItemId = "old-id",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(
                DataOrigin.StandardImport,
                new RecordTimestamps(ImportedAt: importedAt))
        };
        await context.AtomicGacha.CommitAsync(
            new GachaAtomicChangeRequest(
                OperationId.New(),
                DataChangeOperationKind.Import,
                DataOrigin.StandardImport,
                "seed",
                importedAt,
                importedAt,
                [new GachaFactMutation(reference, original)]));
        var service = new ManageGachaHistory(context.AtomicGacha);
        DateTimeOffset correctedAt = importedAt.AddHours(1);

        GachaAtomicChangeResult result = await service.CorrectAsync(
            OperationId.New(),
            reference,
            new GachaCorrectionInput(
                "  Corrected  ",
                "  new-id  ",
                4,
                correctedAt));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        GachaFactState current = Assert.IsType<GachaFactState>(
            await context.AtomicGacha.GetCurrentAsync(reference));
        Assert.Equal("Corrected", current.Record.ItemName);
        Assert.Equal("new-id", current.Record.ItemId);
        Assert.Equal(4, current.Record.RankType);
        Assert.Equal(correctedAt, current.Record.Time);
        Assert.Equal(DataOrigin.UserEntered, current.Record.Provenance.Origin);
        Assert.Equal(RecordTimestamps.Unknown, current.Record.Provenance.Timestamps);

        GachaRevisionItem revision = Assert.Single(
            await context.AtomicGacha.GetRevisionsAsync(
                result.ChangeSetId!.Value));
        Assert.Equal(DataOrigin.StandardImport, revision.Before!.Provenance.Origin);
        Assert.Equal(importedAt, revision.Before.Provenance.Timestamps.ImportedAt);
        Assert.Equal(DataOrigin.UserEntered, revision.After!.Provenance.Origin);
        Assert.Equal(RecordTimestamps.Unknown, revision.After.Provenance.Timestamps);
    }
}

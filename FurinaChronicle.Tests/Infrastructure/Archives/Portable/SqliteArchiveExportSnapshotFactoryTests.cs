// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Infrastructure.Archives.Portable;

public sealed class SqliteArchiveExportSnapshotFactoryTests
{
    [Fact]
    public async Task OnlineBackupSnapshotExcludesLaterProductionWrite()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        var archive = SqliteRepositoryTestContext.CreateArchive("Snapshot");
        var account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        await context.Gacha.SaveBatchAsync([Record(account.Id, "1")]);
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        var factory = new SqliteArchiveExportSnapshotFactory(
            context.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(root));

        IArchiveExportSnapshot snapshot = await factory.OpenAsync(
            ArchiveExportScope.All());
        await context.Gacha.SaveBatchAsync([Record(account.Id, "2")]);
        List<GachaRecord> records = await ReadAllAsync(
            snapshot.ReadGachaRecordsAsync(account.Id));

        Assert.Single(records);
        Assert.Equal("1", records[0].ExternalRecordId);
        Assert.Equal(archive.Name, Assert.Single(snapshot.Catalog.Archives).Name);
        Assert.Equal(3, Assert.Single(snapshot.Settings.Archives).UndoLimit);

        await snapshot.DisposeAsync();
        Assert.True(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any());
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task SelectedScopeRejectsMissingArchiveAndCleansSession()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        var factory = new SqliteArchiveExportSnapshotFactory(
            context.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(root));

        await Assert.ThrowsAsync<KeyNotFoundException>(() => factory.OpenAsync(
            ArchiveExportScope.Selected([Guid.NewGuid()])));

        Assert.True(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any());
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task LogicalExporterProducesValidatedCrossDomainArchive()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        var archive = SqliteRepositoryTestContext.CreateArchive("Cross domain");
        var account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        await context.Gacha.SaveBatchAsync([Record(account.Id, "1")]);
        DateTimeOffset now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        var observation = new ItemStateObservation(
            ItemObservationId.New(),
            account.Id,
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
                new RecordTimestamps(ObservedAt: now, FetchedAt: now)));
        await context.AtomicGacha.CommitItemObservationsAsync(
            new ItemObservationCommitRequest(
                OperationId.New(),
                DataChangeOperationKind.Refresh,
                DataOrigin.OfficialApi,
                "Archive fixture",
                now,
                now,
                [observation],
                captureUndo: false));
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        var factory = new SqliteArchiveExportSnapshotFactory(
            context.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(root));
        var exporter = new ArchiveLogicalExporter(
            factory,
            new ArchivePackageWriter(),
            new ArchivePackageReader());
        using var output = new MemoryStream();

        ArchiveWriteResult result = await exporter.ExportAsync(
            output,
            ArchiveExportScope.All(),
            new ArchiveGeneratorInfo(
                "FurinaChronicle",
                "0.8.10.175",
                "8C.1",
                "0000000",
                "test"));

        Assert.Equal(1, result.Summary.GachaCurrentFactCount);
        Assert.Equal(1, result.Summary.DomainFactCount);
        Assert.True(result.Summary.IsComplete);
        Assert.True(!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any());
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task CancelledExportDisposesAndDeletesSnapshotSession()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        var archive = SqliteRepositoryTestContext.CreateArchive("Cancelled export");
        await context.Archives.AddAsync(archive);
        string root = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        var factory = new SqliteArchiveExportSnapshotFactory(
            context.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(root));
        var exporter = new ArchiveLogicalExporter(
            factory,
            new CancellingWriter(),
            new ArchivePackageReader());
        using var output = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            exporter.ExportAsync(
                output,
                ArchiveExportScope.All(),
                new ArchiveGeneratorInfo(
                    "FurinaChronicle",
                    "0.8.10.175",
                    "8C.1",
                    "0000000",
                    "test")));

        Assert.True(
            !Directory.Exists(root) ||
            !Directory.EnumerateFileSystemEntries(root).Any());
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    private static GachaRecord Record(Guid accountId, string id) =>
        new(accountId, id, "Furina", 5, DateTimeOffset.UtcNow)
        {
            ItemId = "10000002",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
        };

    private static async Task<List<T>> ReadAllAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (T item in source) result.Add(item);
        return result;
    }

    private sealed class CancellingWriter : IArchivePackageWriter
    {
        public Task<ArchiveWriteResult> WriteAsync(
            Stream destination,
            IArchiveExportSnapshot snapshot,
            ArchiveGeneratorInfo generator,
            CancellationToken cancellationToken = default) =>
            Task.FromException<ArchiveWriteResult>(
                new OperationCanceledException("Synthetic cancellation."));
    }
}

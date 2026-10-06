// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Core.History;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class GachaPortableDatabaseExportTests
{
    [Fact]
    public async Task ExportAsync_StagesValidPackageFromDatabase()
    {
        await using TestContext context = await TestContext.CreateAsync(1_250);
        string destination = Path.Combine(context.Directory, "export.fcgp");
        var service = context.CreateExportService();

        GachaPortableWriteResult result = await service.ExportAsync(
            destination,
            context.Archive.Id,
            [context.Account.Id]);

        Assert.Equal(1, result.AccountCount);
        Assert.Equal(1_250, result.RecordCount);
        Assert.True(File.Exists(destination));
        await using FileStream input = File.OpenRead(destination);
        GachaPortableReadResult read =
            await new GachaPortablePackageReader().ReadAsync(input);
        GachaPortableAccount account = Assert.Single(read.Package.Accounts);
        Assert.Equal(context.Account.Id, account.AccountReference);
        Assert.Equal(1_250, account.Records.Count);
        Assert.Equal(
            Enumerable.Range(1, 1_250).Select(index => index.ToString("D19")),
            account.Records.Select(record => record.ExternalRecordId));
    }

    [Fact]
    public async Task Snapshot_ExcludesConcurrentInsertFromBothPayloads()
    {
        await using TestContext context = await TestContext.CreateAsync(2);
        var factory = context.CreateSnapshotFactory();
        await using IGachaPortableExportSnapshot snapshot =
            await factory.OpenAsync(
                context.Archive.Id,
                [context.Account.Id]);

        Task<GachaSaveResult> pendingInsert = context.Gacha.SaveBatchAsync(
            [CreateRecord(context.Account.Id, 3)]);
        using var output = new MemoryStream();
        GachaPortableWriteResult result =
            await new GachaPortablePackageWriter().WriteAsync(
                output,
                snapshot);

        Assert.Equal(2, result.RecordCount);
        await snapshot.DisposeAsync();
        GachaSaveResult saveResult = await pendingInsert;
        Assert.Equal(1, saveResult.InsertedCount);

        output.Position = 0;
        GachaPortableReadResult read =
            await new GachaPortablePackageReader().ReadAsync(output);
        Assert.Equal(2, Assert.Single(read.Package.Accounts).Records.Count);
    }

    [Fact]
    public async Task DatabaseAExport_DatabaseBApply_ReExportPreservesSupportedSemantics()
    {
        await using TestContext source = await TestContext.CreateAsync(3);
        using var firstPackageBytes = new MemoryStream();
        await using (IGachaPortableExportSnapshot sourceSnapshot =
            await source.CreateSnapshotFactory().OpenAsync(
                source.Archive.Id,
                [source.Account.Id]))
        {
            await new GachaPortablePackageWriter().WriteAsync(
                firstPackageBytes,
                sourceSnapshot);
        }
        firstPackageBytes.Position = 0;
        GachaPortablePackage firstPackage =
            (await new GachaPortablePackageReader().ReadAsync(
                firstPackageBytes)).Package;

        await using var destination = await EmptyTestContext.CreateAsync();
        GachaPortableImportPlan plan =
            await new PlanGachaPortableImport(
                destination.Archives,
                destination.Accounts,
                destination.Gacha,
                new FixedTimeProvider())
            .ExecuteAsync(new GachaPortableImportPlanRequest(firstPackage));
        GachaPortableApplyResult applied =
            await destination.Atomic.ApplyAsync(
                new GachaPortableApplyRequest(
                    firstPackage,
                    plan,
                    OperationId.New(),
                    new DateTimeOffset(
                        2026,
                        10,
                        6,
                        9,
                        0,
                        0,
                        TimeSpan.FromHours(8)),
                    Guid.NewGuid()));
        Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);

        using var secondPackageBytes = new MemoryStream();
        await using (IGachaPortableExportSnapshot destinationSnapshot =
            await destination.CreateSnapshotFactory().OpenAsync(
                applied.TargetArchiveId!.Value,
                applied.Accounts.Select(account =>
                    account.TargetAccountId).ToArray()))
        {
            await new GachaPortablePackageWriter().WriteAsync(
                secondPackageBytes,
                destinationSnapshot);
        }
        secondPackageBytes.Position = 0;
        GachaPortablePackage secondPackage =
            (await new GachaPortablePackageReader().ReadAsync(
                secondPackageBytes)).Package;

        Assert.Equal(
            firstPackage.SourceArchive.Name,
            secondPackage.SourceArchive.Name);
        GachaPortableAccount firstAccount =
            Assert.Single(firstPackage.Accounts);
        GachaPortableAccount secondAccount =
            Assert.Single(secondPackage.Accounts);
        Assert.Equal(
            firstAccount.RoleIdentity,
            secondAccount.RoleIdentity);
        Assert.Equal(firstAccount.DisplayName, secondAccount.DisplayName);
        Assert.Equal(
            firstAccount.Records.Select(ToSemanticRecord),
            secondAccount.Records.Select(ToSemanticRecord));
    }

    [Fact]
    public async Task ExportAsync_InvalidSourcePreservesExistingTargetAndCleansStage()
    {
        await using TestContext context = await TestContext.CreateAsync(0);
        GachaRecord invalid = CreateRecord(context.Account.Id, 1) with
        {
            ItemId = null,
        };
        await context.Gacha.SaveBatchAsync([invalid]);
        string destination = Path.Combine(context.Directory, "existing.fcgp");
        byte[] original = "existing-successful-export"u8.ToArray();
        await File.WriteAllBytesAsync(destination, original);

        GachaPortableException exception =
            await Assert.ThrowsAsync<GachaPortableException>(
                () => context.CreateExportService().ExportAsync(
                    destination,
                    context.Archive.Id,
                    [context.Account.Id]));

        Assert.Equal(
            GachaPortableErrorCode.MissingRequiredField,
            exception.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(
            context.Directory,
            ".existing.fcgp.*.tmp"));
    }

    [Fact]
    public async Task ExportAsync_CancellationPreservesExistingTargetAndCleansStage()
    {
        await using TestContext context = await TestContext.CreateAsync(1);
        string destination = Path.Combine(context.Directory, "existing.fcgp");
        byte[] original = "existing-successful-export"u8.ToArray();
        await File.WriteAllBytesAsync(destination, original);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            context.CreateExportService().ExportAsync(
                destination,
                context.Archive.Id,
                [context.Account.Id],
                cancellation.Token));

        Assert.Equal(original, await File.ReadAllBytesAsync(destination));
        Assert.Empty(Directory.GetFiles(
            context.Directory,
            ".existing.fcgp.*.tmp"));
    }

    private static GachaRecord CreateRecord(Guid accountId, int index)
    {
        DateTimeOffset fetchedAt = new(
            2026,
            10,
            6,
            8,
            0,
            0,
            TimeSpan.FromHours(8));
        return new GachaRecord(
            accountId,
            index.ToString("D19"),
            $"Item {index}",
            index % 100 == 0 ? 5 : 3,
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.FromHours(8)).AddMinutes(index))
        {
            ItemId = (10_000_000 + index).ToString(),
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Count = 1,
            Provenance = new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(FetchedAt: fetchedAt),
                acquisitionBatchId: new AcquisitionBatchId(
                    Guid.Parse("40000000-0000-0000-0000-000000000001"))),
        };
    }

    private static object ToSemanticRecord(GachaRecord record) =>
        new
        {
            record.ExternalRecordId,
            record.ItemName,
            record.ItemId,
            record.ItemType,
            record.GachaType,
            record.UigfGachaType,
            record.RankType,
            record.Count,
            record.Time,
            record.Provenance
        };

    private sealed class TestContext : IAsyncDisposable
    {
        private TestContext(
            string directory,
            SqliteDatabaseOptions options,
            FurinaDatabase database,
            PlayerArchive archive,
            GameAccount account)
        {
            Directory = directory;
            Options = options;
            Database = database;
            Archive = archive;
            Account = account;
            Gacha = new SqliteGachaRecordRepository(database);
        }

        public string Directory { get; }

        public SqliteDatabaseOptions Options { get; }

        public FurinaDatabase Database { get; }

        public PlayerArchive Archive { get; }

        public GameAccount Account { get; }

        public SqliteGachaRecordRepository Gacha { get; }

        public static async Task<TestContext> CreateAsync(int recordCount)
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var options = new SqliteDatabaseOptions(
                Path.Combine(directory, "test.db3"));
            var database = new FurinaDatabase(options);
            DateTimeOffset now = new(
                2026,
                10,
                6,
                0,
                0,
                0,
                TimeSpan.Zero);
            var archive = new PlayerArchive(
                Guid.NewGuid(),
                " Portable Archive ",
                now,
                now);
            GameRoleIdentity roleIdentity =
                GenshinGameRoleIdentity.CreateIdentity(
                    "100000001",
                    GameServerRegion.ChinaOfficial);
            var account = new GameAccount(
                Guid.NewGuid(),
                archive.Id,
                "100000001",
                GameServerRegion.ChinaOfficial,
                "测试账号",
                false,
                now,
                now,
                roleIdentity);
            await new SqlitePlayerArchiveRepository(database).AddAsync(archive);
            await new SqliteGameAccountRepository(database).AddAsync(account);
            var context = new TestContext(
                directory,
                options,
                database,
                archive,
                account);
            for (int offset = 0; offset < recordCount; offset += 250)
            {
                GachaRecord[] records = Enumerable
                    .Range(offset + 1, Math.Min(250, recordCount - offset))
                    .Select(index => CreateRecord(account.Id, index))
                    .ToArray();
                await context.Gacha.SaveBatchAsync(records);
            }

            return context;
        }

        public SqliteGachaPortableExportSnapshotFactory CreateSnapshotFactory()
        {
            return new SqliteGachaPortableExportSnapshotFactory(
                Database,
                Options,
                new FixedTimeProvider());
        }

        public GachaPortableExportFileService CreateExportService()
        {
            return new GachaPortableExportFileService(
                CreateSnapshotFactory(),
                new GachaPortablePackageWriter());
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(
            2026,
            10,
            6,
            0,
            0,
            0,
            TimeSpan.Zero);
    }

    private sealed class EmptyTestContext : IAsyncDisposable
    {
        private EmptyTestContext(
            string directory,
            SqliteDatabaseOptions options,
            FurinaDatabase database)
        {
            Directory = directory;
            Options = options;
            Database = database;
            Archives = new SqlitePlayerArchiveRepository(database);
            Accounts = new SqliteGameAccountRepository(database);
            Gacha = new SqliteGachaRecordRepository(database);
            Atomic = new SqliteGachaAtomicChangeStore(database);
        }

        public string Directory { get; }

        public SqliteDatabaseOptions Options { get; }

        public FurinaDatabase Database { get; }

        public SqlitePlayerArchiveRepository Archives { get; }

        public SqliteGameAccountRepository Accounts { get; }

        public SqliteGachaRecordRepository Gacha { get; }

        public SqliteGachaAtomicChangeStore Atomic { get; }

        public static async Task<EmptyTestContext> CreateAsync()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleTests",
                Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            var options = new SqliteDatabaseOptions(
                Path.Combine(directory, "target.db3"));
            var database = new FurinaDatabase(options);
            await database.InitializeAsync();
            return new EmptyTestContext(directory, options, database);
        }

        public SqliteGachaPortableExportSnapshotFactory
            CreateSnapshotFactory() =>
            new(Database, Options, new FixedTimeProvider());

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(
                    Directory,
                    recursive: true);
            }
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Portable;

public sealed class SqliteGachaPortableApplyTests
{
    private static readonly DateTimeOffset ReceivedAt =
        new(2026, 10, 6, 19, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task ApplyAsync_NewArchiveMultipleAccountsCommitsOneChangeSetAndReceipt()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        GachaPortablePackage package = CreatePackage(
            " Portable Archive ",
            ("100000001", "source-1", "record-1"),
            ("100000002", "source-2", "record-2"));
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        OperationId operationId = OperationId.New();

        GachaPortableApplyResult result =
            await context.AtomicGacha.ApplyAsync(new GachaPortableApplyRequest(
                package,
                plan,
                operationId,
                ReceivedAt,
                Guid.NewGuid()));

        Assert.Equal(ChangeExecutionStatus.Applied, result.Status);
        Assert.Equal(2, result.InsertedRecordCount);
        Assert.Equal(2, result.Accounts.Count);
        Assert.NotNull(result.TargetArchiveId);
        Assert.All(
            result.Accounts,
            account => Assert.Equal(1, account.InsertedRecordCount));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(
            " Portable Archive ",
            raw.ExecuteScalar<string>(
                "SELECT Name FROM PlayerArchives WHERE Id = ?;",
                result.TargetArchiveId!.Value.ToString("D")));
        Assert.Equal(2, Count(raw, "GameAccounts"));
        Assert.Equal(2, Count(raw, "GachaRecords"));
        Assert.Equal(1, Count(raw, "DataChangeSets"));
        Assert.Equal(1, Count(raw, "PortableImportReceipts"));
        Assert.Equal(2, Count(raw, "PortableImportReceiptAccounts"));
        Assert.Equal(
            (int)DataOrigin.OfficialApi,
            raw.ExecuteScalar<int>(
                "SELECT Origin FROM GachaRecords LIMIT 1;"));
        Assert.Equal(
            ReceivedAt.UtcDateTime.Ticks,
            raw.ExecuteScalar<long>(
                """
                SELECT ReceivedAtUtcTicks
                FROM PortableImportReceipts;
                """));
    }

    [Fact]
    public async Task ApplyAsync_SecondAccountFailureRollsBackArchiveAccountsFactsAndHistory()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        GachaPortablePackage package = CreatePackage(
            "Atomic",
            ("100000001", "source-1", "record-ok"),
            ("100000002", "source-2", "record-fail"));
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            raw.Execute(
                """
                CREATE TRIGGER FailSecondPortableAccount
                BEFORE INSERT ON GachaRecords
                WHEN NEW.ExternalRecordId = 'record-fail'
                BEGIN
                    SELECT RAISE(ABORT, 'forced second account failure');
                END;
                """);
        }

        await Assert.ThrowsAsync<SQLiteException>(
            () => context.AtomicGacha.ApplyAsync(
                new GachaPortableApplyRequest(
                    package,
                    plan,
                    OperationId.New(),
                    ReceivedAt,
                    Guid.NewGuid())));

        using SQLiteConnection reopened = context.OpenRawConnection();
        Assert.Equal(0, Count(reopened, "PlayerArchives"));
        Assert.Equal(0, Count(reopened, "GameRoleIdentities"));
        Assert.Equal(0, Count(reopened, "GameAccounts"));
        Assert.Equal(0, Count(reopened, "GachaRecords"));
        Assert.Equal(0, Count(reopened, "DataChangeSets"));
        Assert.Equal(0, Count(reopened, "PortableImportReceipts"));
    }

    [Fact]
    public async Task ApplyAsync_SameCountChangedContentAfterPlanReturnsConflict()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("Target");
        GameRoleNaturalIdentity natural =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        var identity = new GameRoleIdentity(
            GameRoleIdentityId.FromNaturalIdentity(natural),
            natural);
        GameAccount account =
            SqliteRepositoryTestContext.CreateAccount(
                archive.Id,
                "100000001",
                GameServerRegion.ChinaOfficial,
                roleIdentity: identity);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        GachaRecord target = CreateRecord(
            account.Id,
            "record-1",
            "Original");
        await context.Gacha.SaveBatchAsync([target]);
        GachaPortablePackage package = new(
            ReceivedAt,
            new GachaPortableArchive(Guid.NewGuid(), "Target"),
            [
                new GachaPortableAccount(
                    Guid.NewGuid(),
                    identity,
                    "Source",
                    [target with { GameAccountId = Guid.NewGuid() }])
            ]);
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        await context.Gacha.SaveBatchAsync(
            [target with { ItemName = "Changed after plan" }],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);

        GachaPortableApplyResult result =
            await context.AtomicGacha.ApplyAsync(
                new GachaPortableApplyRequest(
                    package,
                    plan,
                    OperationId.New(),
                    ReceivedAt,
                    Guid.NewGuid()));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(0, Count(raw, "DataChangeSets"));
        Assert.Equal(0, Count(raw, "PortableImportReceipts"));
    }

    [Fact]
    public async Task ApplyAsync_AliasAndOperationRetryArePersistedExactlyOnce()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("Alias");
        GameRoleNaturalIdentity natural =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        var targetIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(
                Guid.Parse("10000000-0000-0000-0000-000000000001")),
            natural);
        GameAccount target =
            SqliteRepositoryTestContext.CreateAccount(
                archive.Id,
                "100000001",
                GameServerRegion.ChinaOfficial,
                roleIdentity: targetIdentity);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(target);
        var sourceIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(
                Guid.Parse("20000000-0000-0000-0000-000000000001")),
            natural);
        Guid sourceAccount = Guid.NewGuid();
        GachaPortablePackage package = new(
            ReceivedAt,
            new GachaPortableArchive(Guid.NewGuid(), "Alias"),
            [
                new GachaPortableAccount(
                    sourceAccount,
                    sourceIdentity,
                    "Source",
                    [CreateRecord(sourceAccount, "record-1", "Furina")])
            ]);
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        OperationId operationId = OperationId.New();
        var request = new GachaPortableApplyRequest(
            package,
            plan,
            operationId,
            ReceivedAt,
            Guid.NewGuid());

        GachaPortableApplyResult first =
            await context.AtomicGacha.ApplyAsync(request);
        GachaPortableApplyResult retry =
            await context.AtomicGacha.ApplyAsync(request);

        Assert.Equal(ChangeExecutionStatus.Applied, first.Status);
        Assert.Equal(ChangeExecutionStatus.AlreadyCommitted, retry.Status);
        Assert.Equal(first.ChangeSetId, retry.ChangeSetId);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "RoleIdentityAliases"));
        Assert.Equal(1, Count(raw, "GachaRecords"));
        Assert.Equal(1, Count(raw, "PortableImportReceipts"));
        Assert.Equal(
            targetIdentity.Id.ToString(),
            raw.ExecuteScalar<string>(
                """
                SELECT TargetIdentityId
                FROM RoleIdentityAliases
                WHERE SourceIdentityId = ?;
                """,
                sourceIdentity.Id.ToString()));
    }

    [Fact]
    public async Task ApplyAsync_NewOperationForPureDuplicateIsNoOpWithoutReceipt()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        GachaPortablePackage package = CreatePackage(
            "Duplicate",
            ("100000001", "source-1", "record-1"));
        GachaPortableImportPlan firstPlan =
            await PlanAsync(context, package);
        await context.AtomicGacha.ApplyAsync(
            new GachaPortableApplyRequest(
                package,
                firstPlan,
                OperationId.New(),
                ReceivedAt,
                Guid.NewGuid()));
        GachaPortableImportPlan duplicatePlan =
            await PlanAsync(context, package);

        GachaPortableApplyResult duplicate =
            await context.AtomicGacha.ApplyAsync(
                new GachaPortableApplyRequest(
                    package,
                    duplicatePlan,
                    OperationId.New(),
                    ReceivedAt.AddMinutes(1),
                    Guid.NewGuid()));

        Assert.Equal(ChangeExecutionStatus.NoOp, duplicate.Status);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "DataChangeSets"));
        Assert.Equal(1, Count(raw, "PortableImportReceipts"));
        Assert.Equal(2, Count(raw, "OperationCommitResults"));
    }

    [Fact]
    public async Task ApplyAsync_PackageMutatedAfterPlanReturnsConflictWithoutWrites()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        GameRoleNaturalIdentity natural =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        Guid sourceAccountId = Guid.NewGuid();
        var records = new List<GachaRecord>
        {
            CreateRecord(sourceAccountId, "record-1", "Original")
        };
        GachaPortablePackage package = new(
            ReceivedAt,
            new GachaPortableArchive(Guid.NewGuid(), "Mutable"),
            [
                new GachaPortableAccount(
                    sourceAccountId,
                    new GameRoleIdentity(
                        GameRoleIdentityId.FromNaturalIdentity(natural),
                        natural),
                    "Source",
                    records)
            ]);
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        records[0] = records[0] with { ItemName = "Mutated" };

        GachaPortableApplyResult result =
            await context.AtomicGacha.ApplyAsync(
                new GachaPortableApplyRequest(
                    package,
                    plan,
                    OperationId.New(),
                    ReceivedAt,
                    Guid.NewGuid()));

        Assert.Equal(ChangeExecutionStatus.Conflict, result.Status);
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(0, Count(raw, "PlayerArchives"));
        Assert.Equal(0, Count(raw, "DataChangeSets"));
        Assert.Equal(0, Count(raw, "PortableImportReceipts"));
    }

    [Fact]
    public async Task ApplyAsync_ExistingAccountImportCanUndoInsertedFactsWithoutTouchingAccount()
    {
        await using SqliteRepositoryTestContext context =
            await CreateContextAsync();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive("Undo");
        GameRoleNaturalIdentity natural =
            GenshinGameRoleIdentity.Create(
                "100000001",
                GameServerRegion.ChinaOfficial);
        var identity = new GameRoleIdentity(
            GameRoleIdentityId.FromNaturalIdentity(natural),
            natural);
        GameAccount account =
            SqliteRepositoryTestContext.CreateAccount(
                archive.Id,
                natural.Uid,
                GameServerRegion.ChinaOfficial,
                roleIdentity: identity);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);
        Guid sourceAccountId = Guid.NewGuid();
        GachaPortablePackage package = new(
            ReceivedAt,
            new GachaPortableArchive(Guid.NewGuid(), "Undo"),
            [
                new GachaPortableAccount(
                    sourceAccountId,
                    identity,
                    "Source",
                    [
                        CreateRecord(
                            sourceAccountId,
                            "record-1",
                            "Furina")
                    ])
            ]);
        GachaPortableImportPlan plan =
            await PlanAsync(context, package);
        GachaPortableApplyResult applied =
            await context.AtomicGacha.ApplyAsync(
                new GachaPortableApplyRequest(
                    package,
                    plan,
                    OperationId.New(),
                    ReceivedAt,
                    Guid.NewGuid()));

        GachaAtomicChangeResult undone =
            await context.AtomicGacha.UndoLatestAsync(
                new GachaUndoRequest(
                    OperationId.New(),
                    archive.Id,
                    ReceivedAt.AddMinutes(1),
                    ReceivedAt.AddMinutes(1).AddSeconds(1)));

        Assert.Equal(ChangeExecutionStatus.Applied, applied.Status);
        Assert.Equal(ChangeExecutionStatus.Applied, undone.Status);
        Assert.Empty(await context.Gacha.GetRecentAsync(account.Id, 10));
        Assert.NotNull(await context.Accounts.GetByIdAsync(account.Id));
        using SQLiteConnection raw = context.OpenRawConnection();
        Assert.Equal(1, Count(raw, "PortableImportReceipts"));
        Assert.Equal(2, Count(raw, "DataChangeSets"));
    }

    private static async Task<SqliteRepositoryTestContext> CreateContextAsync()
    {
        SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        return context;
    }

    private static Task<GachaPortableImportPlan> PlanAsync(
        SqliteRepositoryTestContext context,
        GachaPortablePackage package) =>
        new PlanGachaPortableImport(
            context.Archives,
            context.Accounts,
            context.Gacha,
            new FixedTimeProvider())
        .ExecuteAsync(new GachaPortableImportPlanRequest(package));

    private static GachaPortablePackage CreatePackage(
        string archiveName,
        params (string Uid, string AccountSeed, string RecordId)[] accounts)
    {
        return new GachaPortablePackage(
            ReceivedAt,
            new GachaPortableArchive(Guid.NewGuid(), archiveName),
            accounts.Select(account =>
            {
                GameRoleNaturalIdentity natural =
                    GenshinGameRoleIdentity.Create(
                        account.Uid,
                        GameServerRegion.ChinaOfficial);
                var identity = new GameRoleIdentity(
                    GameRoleIdentityId.FromNaturalIdentity(natural),
                    natural);
                Guid accountReference = DeterministicGuid(
                    account.AccountSeed);
                return new GachaPortableAccount(
                    accountReference,
                    identity,
                    account.AccountSeed,
                    [
                        CreateRecord(
                            accountReference,
                            account.RecordId,
                            "Furina")
                    ]);
            }).ToArray());
    }

    private static GachaRecord CreateRecord(
        Guid accountId,
        string externalId,
        string itemName) =>
        new(
            accountId,
            externalId,
            itemName,
            5,
            new DateTimeOffset(
                2026,
                1,
                1,
                0,
                0,
                0,
                TimeSpan.FromHours(8)))
        {
            ItemId = "10000089",
            ItemType = "Character",
            GachaType = "301",
            UigfGachaType = "301",
            Provenance = new RecordProvenance(DataOrigin.OfficialApi)
        };

    private static Guid DeterministicGuid(string value)
    {
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static int Count(SQLiteConnection connection, string table) =>
        connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table};");

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ReceivedAt;
    }
}

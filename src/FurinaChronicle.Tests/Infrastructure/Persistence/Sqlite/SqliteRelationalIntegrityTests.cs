// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteRelationalIntegrityTests
{
    [Fact]
    public async Task SaveGachaForUnknownAccount_ThrowsForeignKeyConstraint()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GachaRecord record = CreateGacha(Guid.NewGuid(), "gacha-1");

        SQLiteException error = await Assert.ThrowsAsync<SQLiteException>(
            () => context.Gacha.SaveBatchAsync([record]));

        Assert.Equal(SQLite3.Result.Constraint, error.Result);
    }

    [Fact]
    public async Task DeleteAccount_CascadesItsGachaOnly()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount deletedAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000001");
        GameAccount retainedAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "100000002");
        await context.Accounts.AddAsync(deletedAccount);
        await context.Accounts.AddAsync(retainedAccount);
        await context.Gacha.SaveBatchAsync(
            [
                CreateGacha(deletedAccount.Id, "gacha-1"),
                CreateGacha(retainedAccount.Id, "gacha-2")
            ]);

        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            raw.Execute(
                "DELETE FROM GameAccounts WHERE Id = ?;",
                deletedAccount.Id.ToString("D"));
        }

        Assert.Empty(await context.Gacha.GetRecentAsync(deletedAccount.Id, 20));
        Assert.Single(await context.Gacha.GetRecentAsync(retainedAccount.Id, 20));
    }

    [Fact]
    public async Task DeleteArchive_CascadesAccountsAndGacha()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(account);
        await context.Gacha.SaveBatchAsync([CreateGacha(account.Id, "gacha-1")]);

        using (SQLiteConnection raw = context.OpenRawConnection())
        {
            raw.Execute(
                "DELETE FROM PlayerArchives WHERE Id = ?;",
                archive.Id.ToString("D"));
        }

        Assert.Empty(await context.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Empty(await context.Gacha.GetRecentAsync(account.Id, 20));
    }

    private static GachaRecord CreateGacha(Guid accountId, string externalId)
    {
        return new GachaRecord(
            accountId,
            externalId,
            "芙宁娜",
            5,
            new DateTimeOffset(2026, 7, 16, 18, 30, 0, TimeSpan.FromHours(8)));
    }
}

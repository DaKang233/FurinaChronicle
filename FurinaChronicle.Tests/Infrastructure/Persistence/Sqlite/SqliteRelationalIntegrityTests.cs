using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteRelationalIntegrityTests
{
    [Fact]
    public async Task SaveWishForUnknownAccount_ThrowsForeignKeyConstraint()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        WishRecord record = CreateWish(Guid.NewGuid(), "wish-1");

        SQLiteException error = await Assert.ThrowsAsync<SQLiteException>(
            () => context.Wishes.SaveBatchAsync([record]));

        Assert.Equal(SQLite3.Result.Constraint, error.Result);
    }

    [Fact]
    public async Task DeleteAccount_CascadesItsWishesOnly()
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
        await context.Wishes.SaveBatchAsync(
            [
                CreateWish(deletedAccount.Id, "wish-1"),
                CreateWish(retainedAccount.Id, "wish-2")
            ]);

        await context.Accounts.DeleteAsync(deletedAccount.Id);

        Assert.Empty(await context.Wishes.GetRecentAsync(deletedAccount.Id, 20));
        Assert.Single(await context.Wishes.GetRecentAsync(retainedAccount.Id, 20));
    }

    [Fact]
    public async Task DeleteArchive_CascadesAccountsAndWishes()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(account);
        await context.Wishes.SaveBatchAsync([CreateWish(account.Id, "wish-1")]);

        await context.Archives.DeleteAsync(archive.Id);

        Assert.Empty(await context.Accounts.GetByArchiveIdAsync(archive.Id));
        Assert.Empty(await context.Wishes.GetRecentAsync(account.Id, 20));
    }

    private static WishRecord CreateWish(Guid accountId, string externalId)
    {
        return new WishRecord(
            accountId,
            externalId,
            "芙宁娜",
            5,
            new DateTimeOffset(2026, 7, 16, 18, 30, 0, TimeSpan.FromHours(8)));
    }
}

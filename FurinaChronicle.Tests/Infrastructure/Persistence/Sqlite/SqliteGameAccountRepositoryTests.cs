using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGameAccountRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsAllFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);

        await context.Accounts.AddAsync(account);

        Assert.Equal(account, await context.Accounts.GetByIdAsync(account.Id));
    }

    [Fact]
    public async Task GetByArchiveIdAsync_ReturnsOnlyAccountsInRequestedArchive()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive firstArchive = await AddArchiveAsync(context, "档案 A");
        PlayerArchive secondArchive = await AddArchiveAsync(context, "档案 B");
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            firstArchive.Id,
            uid: "100000001");
        GameAccount second = SqliteRepositoryTestContext.CreateAccount(
            secondArchive.Id,
            uid: "100000002");
        await context.Accounts.AddAsync(first);
        await context.Accounts.AddAsync(second);

        IReadOnlyList<GameAccount> loaded =
            await context.Accounts.GetByArchiveIdAsync(firstArchive.Id);

        Assert.Equal([first], loaded);
    }

    [Fact]
    public async Task FindByUidAsync_MatchesBothRegionAndUid()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount asiaAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "123456789",
            region: GameServerRegion.Asia);
        GameAccount americaAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "123456789",
            region: GameServerRegion.America);
        await context.Accounts.AddAsync(asiaAccount);
        await context.Accounts.AddAsync(americaAccount);

        GameAccount? loaded = await context.Accounts.FindByUidAsync(
            GameServerRegion.America,
            "123456789");

        Assert.Equal(americaAccount, loaded);
    }

    [Fact]
    public async Task FindByUidAsync_UnknownUid_ReturnsNull()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        GameAccount? loaded = await context.Accounts.FindByUidAsync(
            GameServerRegion.Asia,
            "999999999");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task UpdateAsync_ExistingAccount_PersistsChanges()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount original = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(original);
        GameAccount updated = original with
        {
            DisplayName = "新账号名",
            IsPlaceholder = true,
            UpdatedAt = original.UpdatedAt.AddMinutes(30)
        };

        await context.Accounts.UpdateAsync(updated);

        Assert.Equal(updated, await context.Accounts.GetByIdAsync(original.Id));
    }

    [Fact]
    public async Task UpdateAsync_UnknownAccount_ThrowsKeyNotFoundException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => context.Accounts.UpdateAsync(account));
    }

    [Fact]
    public async Task DeleteAsync_ExistingAccount_RemovesIt()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(account);

        await context.Accounts.DeleteAsync(account.Id);

        Assert.Null(await context.Accounts.GetByIdAsync(account.Id));
    }

    [Fact]
    public async Task AddAsync_SameRegionAndUidTwice_ThrowsSqliteException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        GameAccount duplicate = SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Accounts.AddAsync(first);

        await Assert.ThrowsAsync<SQLiteException>(
            () => context.Accounts.AddAsync(duplicate));
    }

    [Fact]
    public async Task AddAsync_SameUidInDifferentRegions_IsAllowed()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "123456789",
            region: GameServerRegion.Asia));
        await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "123456789",
            region: GameServerRegion.America));

        IReadOnlyList<GameAccount> loaded =
            await context.Accounts.GetByArchiveIdAsync(archive.Id);

        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public async Task AddAsync_UnknownArchive_ThrowsForeignKeyConstraint()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(Guid.NewGuid());

        SQLiteException error = await Assert.ThrowsAsync<SQLiteException>(
            () => context.Accounts.AddAsync(account));

        Assert.Equal(SQLite3.Result.Constraint, error.Result);
    }

    private static async Task<PlayerArchive> AddArchiveAsync(
        SqliteRepositoryTestContext context,
        string name = "测试档案")
    {
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive(name);
        await context.Archives.AddAsync(archive);
        return archive;
    }
}

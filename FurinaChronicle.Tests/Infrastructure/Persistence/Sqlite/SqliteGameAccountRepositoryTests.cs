// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Archives;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteGameAccountRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsAllFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameRoleIdentity roleIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia,
            roleIdentity: roleIdentity);

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
    public async Task GetByArchiveIdAndNaturalIdentityAsync_MatchesExactIdentity()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive1 = await AddArchiveAsync(context);
        PlayerArchive archive2 = await AddArchiveAsync(context);
        GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
            "800000001",
            GameServerRegion.Asia);
        GameAccount account1 = SqliteRepositoryTestContext.CreateAccount(
            archive1.Id,
            uid: "800000001",
            region: GameServerRegion.Asia,
            roleIdentity: identity);
        GameAccount account2 = SqliteRepositoryTestContext.CreateAccount(
            archive2.Id,
            uid: "800000001",
            region: GameServerRegion.Asia,
            roleIdentity: identity);
        await context.Accounts.AddAsync(account1);
        await context.Accounts.AddAsync(account2);

        GameAccount? loaded =
            await context.Accounts.GetByArchiveIdAndNaturalIdentityAsync(
            archive1.Id,
            identity.NaturalIdentity);

        Assert.Equal(account1, loaded);
    }

    [Fact]
    public async Task FindByNaturalIdentityAsync_UnknownIdentity_ReturnsNull()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        var archive = await AddArchiveAsync(context);
        GameRoleNaturalIdentity identity = GenshinGameRoleIdentity.Create(
            "800000001",
            GameServerRegion.Asia);

        GameAccount? loaded =
            await context.Accounts.GetByArchiveIdAndNaturalIdentityAsync(
                archive.Id,
                identity);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task FindByNaturalIdentityAsync_SameUidDifferentFallbackRegions_AreDistinct()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameRoleIdentity asia = GenshinGameRoleIdentity.CreateIdentity(
            "400000001",
            GameServerRegion.Asia);
        GameRoleIdentity america = GenshinGameRoleIdentity.CreateIdentity(
            "400000001",
            GameServerRegion.America);
        GameAccount asiaAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "400000001",
            region: GameServerRegion.Asia,
            roleIdentity: asia);
        GameAccount americaAccount = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "400000001",
            region: GameServerRegion.America,
            roleIdentity: america);
        await context.Accounts.AddAsync(asiaAccount);
        await context.Accounts.AddAsync(americaAccount);

        Assert.Equal(
            asiaAccount,
            await context.Accounts.GetByArchiveIdAndNaturalIdentityAsync(
                archive.Id,
                asia.NaturalIdentity));
        Assert.Equal(
            americaAccount,
            await context.Accounts.GetByArchiveIdAndNaturalIdentityAsync(
                archive.Id,
                america.NaturalIdentity));
    }

    [Fact]
    public async Task GetUnresolvedAsync_ReturnsOnlyAccountsWithoutRoleIdentity()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount unresolved = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "legacy-id",
            region: GameServerRegion.Unknown,
            isPlaceholder: true);
        GameRoleIdentity identity = GenshinGameRoleIdentity.CreateIdentity(
            "800000001",
            GameServerRegion.Asia);
        GameAccount resolved = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia,
            roleIdentity: identity);
        await context.Accounts.AddAsync(unresolved);
        await context.Accounts.AddAsync(resolved);

        Assert.Equal([unresolved], await context.Accounts.GetUnresolvedAsync());
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
    public async Task AddAsync_SameArchiveAndUidTwice_ThrowsSqliteException()
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
    public async Task AddAsync_SameUidInDifferentArchives_IsAllowed()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive1 = await AddArchiveAsync(context);
        PlayerArchive archive2 = await AddArchiveAsync(context);
        await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
            archive1.Id,
            uid: "123456789",
            region: GameServerRegion.Asia));
        await context.Accounts.AddAsync(SqliteRepositoryTestContext.CreateAccount(
            archive2.Id,
            uid: "123456789",
            region: GameServerRegion.America));

        IReadOnlyList<GameAccount> loaded1 = await context.Accounts.GetByArchiveIdAsync(archive1.Id);
        IReadOnlyList<GameAccount> loaded2 = await context.Accounts.GetByArchiveIdAsync(archive2.Id);

        Assert.Equal(2, loaded1.Count + loaded2.Count);
    }

    [Fact]
    public async Task AddAsync_SameResolvedIdentityInDifferentArchives_SharesIdentityId()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive firstArchive = await AddArchiveAsync(context, "档案 A");
        PlayerArchive secondArchive = await AddArchiveAsync(context, "档案 B");
        GameRoleIdentity roleIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            firstArchive.Id,
            uid: roleIdentity.NaturalIdentity.Uid,
            roleIdentity: roleIdentity);
        GameAccount second = SqliteRepositoryTestContext.CreateAccount(
            secondArchive.Id,
            uid: roleIdentity.NaturalIdentity.Uid,
            roleIdentity: roleIdentity);

        await context.Accounts.AddAsync(first);
        await context.Accounts.AddAsync(second);

        GameAccount loadedFirst = Assert.IsType<GameAccount>(
            await context.Accounts.GetByIdAsync(first.Id));
        GameAccount loadedSecond = Assert.IsType<GameAccount>(
            await context.Accounts.GetByIdAsync(second.Id));
        Assert.Equal(
            GameRoleIdentityResolutionState.Resolved,
            loadedFirst.IdentityResolutionState);
        Assert.Equal(loadedFirst.RoleIdentity, loadedSecond.RoleIdentity);
    }

    [Fact]
    public async Task AddAsync_SameResolvedIdentityInOneArchive_IsRejected()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameRoleIdentity roleIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: roleIdentity.NaturalIdentity.Uid,
            roleIdentity: roleIdentity);
        GameAccount duplicate = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: roleIdentity.NaturalIdentity.Uid,
            roleIdentity: roleIdentity);
        await context.Accounts.AddAsync(first);

        await Assert.ThrowsAsync<SQLiteException>(
            () => context.Accounts.AddAsync(duplicate));
    }

    [Fact]
    public async Task AddAsync_SameIdentityIdForDifferentNaturalIdentity_IsRejected()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive firstArchive = await AddArchiveAsync(context, "档案 A");
        PlayerArchive secondArchive = await AddArchiveAsync(context, "档案 B");
        GameRoleIdentity firstIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        var conflictingIdentity = new GameRoleIdentity(
            firstIdentity.Id,
            new GameRoleNaturalIdentity(
                "hk4e_global",
                "os_usa",
                "600000001"));
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            firstArchive.Id,
            uid: firstIdentity.NaturalIdentity.Uid,
            roleIdentity: firstIdentity);
        GameAccount conflicting = SqliteRepositoryTestContext.CreateAccount(
            secondArchive.Id,
            uid: conflictingIdentity.NaturalIdentity.Uid,
            region: GameServerRegion.America,
            roleIdentity: conflictingIdentity);
        await context.Accounts.AddAsync(first);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Accounts.AddAsync(conflicting));
        Assert.Null(await context.Accounts.GetByIdAsync(conflicting.Id));
    }

    [Fact]
    public async Task AddAsync_SameNaturalIdentityWithDifferentId_IsRejected()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive firstArchive = await AddArchiveAsync(context, "档案 A");
        PlayerArchive secondArchive = await AddArchiveAsync(context, "档案 B");
        GameRoleIdentity firstIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        var conflictingIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(Guid.NewGuid()),
            firstIdentity.NaturalIdentity);
        GameAccount first = SqliteRepositoryTestContext.CreateAccount(
            firstArchive.Id,
            uid: firstIdentity.NaturalIdentity.Uid,
            roleIdentity: firstIdentity);
        GameAccount conflicting = SqliteRepositoryTestContext.CreateAccount(
            secondArchive.Id,
            uid: conflictingIdentity.NaturalIdentity.Uid,
            roleIdentity: conflictingIdentity);
        await context.Accounts.AddAsync(first);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => context.Accounts.AddAsync(conflicting));
        Assert.Null(await context.Accounts.GetByIdAsync(conflicting.Id));
    }

    [Fact]
    public async Task AddAsync_AccountFieldsDoNotMatchResolvedIdentity_IsRejected()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameRoleIdentity roleIdentity =
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "600000001",
            region: GameServerRegion.America,
            roleIdentity: roleIdentity);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Accounts.AddAsync(account));
        Assert.Null(await context.Accounts.GetByIdAsync(account.Id));
    }

    [Fact]
    public async Task AddAsync_EmptyAccountId_IsRejectedBeforeSqliteWrite()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            id: Guid.Empty);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Accounts.AddAsync(account));
    }

    [Fact]
    public async Task AddAsync_EmptyArchiveId_IsRejectedBeforeSqliteWrite()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        GameAccount account = SqliteRepositoryTestContext.CreateAccount(
            Guid.Empty);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => context.Accounts.AddAsync(account));
    }

    [Fact]
    public async Task CompleteUnresolvedAccount_ConflictLeavesAccountAndGachaUnchanged()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = await AddArchiveAsync(context);
        GameAccount existing = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia);
        GameAccount unresolved = SqliteRepositoryTestContext.CreateAccount(
            archive.Id,
            uid: "legacy-id",
            region: GameServerRegion.Unknown,
            isPlaceholder: true);
        await context.Accounts.AddAsync(existing);
        await context.Accounts.AddAsync(unresolved);
        var record = new GachaRecord(
            unresolved.Id,
            "p1-conflict-record",
            "Test Item",
            5,
            new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero));
        await context.Gacha.SaveBatchAsync([record]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateGameAccount(context.Accounts).ExecuteAsync(
                unresolved.Id,
                existing.Uid,
                existing.ServerRegion,
                null));

        Assert.Equal(
            unresolved,
            await context.Accounts.GetByIdAsync(unresolved.Id));
        Assert.Equal(
            [record],
            await context.Gacha.GetRecentAsync(unresolved.Id, 10));
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

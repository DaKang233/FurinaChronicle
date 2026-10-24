// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives;

public sealed class GameAccountUseCaseTests
{
    [Fact]
    public async Task Add_NormalizesFieldsAndPersistsAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = new AddGameAccount(archives, accounts);

        GameAccount created = await service.ExecuteAsync(
            archive.Id,
            " 800000001 ",
            GameServerRegion.Asia,
            "  主账号  ");

        Assert.Equal("800000001", created.Uid);
        Assert.Equal("主账号", created.DisplayName);
        Assert.False(created.IsPlaceholder);
        Assert.Equal(
            GameRoleIdentityResolutionState.Resolved,
            created.IdentityResolutionState);
        Assert.Equal(
            GenshinGameRoleIdentity.CreateIdentity(
                "800000001",
                GameServerRegion.Asia),
            created.RoleIdentity);
        Assert.Equal(created, await accounts.GetByIdAsync(created.Id));
    }

    [Fact]
    public async Task Add_UnknownArchive_ThrowsKeyNotFoundException()
    {
        var service = new AddGameAccount(
            new InMemoryPlayerArchiveRepository(),
            new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(
                Guid.NewGuid(),
                "800000001",
                GameServerRegion.Asia,
                null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc123")]
    public async Task Add_InvalidUid_ThrowsArgumentException(string uid)
    {
        var archives = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = new AddGameAccount(archives, new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExecuteAsync(archive.Id, uid, GameServerRegion.Asia, null));
    }

    [Fact]
    public async Task Add_KnownUid_DerivesRegionWithoutManualFallback()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = new AddGameAccount(archives, new InMemoryGameAccountRepository());

        GameAccount account = await service.ExecuteAsync(
            archive.Id,
            "800000001",
            GameServerRegion.Unknown,
            null);

        Assert.Equal(GameServerRegion.Asia, account.ServerRegion);
    }

    [Fact]
    public async Task Add_DuplicateRegionAndUid_ThrowsInvalidOperationException()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        await accounts.AddAsync(ArchiveTestData.Account(archive.Id));
        var service = new AddGameAccount(archives, accounts);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(
                archive.Id,
                "800000001",
                GameServerRegion.Asia,
                null));
    }

    [Fact]
    public async Task Update_ExistingPlaceholder_NormalizesAndPersistsFields()
    {
        var accounts = new InMemoryGameAccountRepository();
        var archiveId = Guid.NewGuid();
        GameAccount original = ArchiveTestData.Account(
            archiveId,
            uid: "legacy-id",
            region: GameServerRegion.Unknown,
            isPlaceholder: true);
        await accounts.AddAsync(original);
        var service = new UpdateGameAccount(accounts);

        GameAccount updated = await service.ExecuteAsync(
            original.Id,
            " 800000001 ",
            GameServerRegion.Asia,
            "  已补全  ");

        Assert.Equal("800000001", updated.Uid);
        Assert.Equal(GameServerRegion.Asia, updated.ServerRegion);
        Assert.Equal("已补全", updated.DisplayName);
        Assert.False(updated.IsPlaceholder);
        Assert.Equal(
            GameRoleIdentityResolutionState.Resolved,
            updated.IdentityResolutionState);
        Assert.Equal(updated, await accounts.GetByIdAsync(original.Id));
    }

    [Fact]
    public async Task Update_ConflictingArchiveAndUid_ThrowsInvalidOperationException()
    {
        var accounts = new InMemoryGameAccountRepository();
        var archiveId = Guid.NewGuid();
        GameAccount first = ArchiveTestData.Account(
            archiveId,
            uid: "100000001",
            region: GameServerRegion.ChinaOfficial);
        GameAccount second = ArchiveTestData.Account(
            archiveId,
            uid: "100000002",
            region: GameServerRegion.ChinaOfficial);
        await accounts.AddAsync(first);
        await accounts.AddAsync(second);
        var service = new UpdateGameAccount(accounts);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ExecuteAsync(
                second.Id,
                first.Uid,
                first.ServerRegion,
                null));
    }

    [Fact]
    public async Task Update_UnknownAccount_ThrowsKeyNotFoundException()
    {
        var service = new UpdateGameAccount(new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(
                Guid.NewGuid(),
                "800000001",
                GameServerRegion.Asia,
                null));
    }

    [Fact]
    public async Task Add_SameNaturalIdentityInAnotherArchive_ReusesExistingNonV5Id()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive firstArchive = ArchiveTestData.Archive("A");
        PlayerArchive secondArchive = ArchiveTestData.Archive("B");
        await archives.AddAsync(firstArchive);
        await archives.AddAsync(secondArchive);
        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(
                "800000001",
                GameServerRegion.Asia);
        var importedIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(Guid.NewGuid()),
            naturalIdentity);
        await accounts.AddAsync(ArchiveTestData.Account(
            firstArchive.Id,
            uid: "800000001",
            region: GameServerRegion.Asia,
            roleIdentity: importedIdentity));

        GameAccount created = await new AddGameAccount(archives, accounts)
            .ExecuteAsync(
                secondArchive.Id,
                "800000001",
                GameServerRegion.Asia,
                null);

        Assert.Equal(importedIdentity, created.RoleIdentity);
    }

    [Fact]
    public async Task Update_ResolvedAccountDisplayName_PreservesNonV5Identity()
    {
        var accounts = new InMemoryGameAccountRepository();
        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(
                "800000001",
                GameServerRegion.Asia);
        var importedIdentity = new GameRoleIdentity(
            new GameRoleIdentityId(Guid.NewGuid()),
            naturalIdentity);
        GameAccount original = ArchiveTestData.Account(
            Guid.NewGuid(),
            uid: "800000001",
            roleIdentity: importedIdentity);
        await accounts.AddAsync(original);

        GameAccount updated = await new UpdateGameAccount(accounts)
            .ExecuteAsync(
                original.Id,
                original.Uid,
                original.ServerRegion,
                "新备注");

        Assert.Equal(importedIdentity, updated.RoleIdentity);
        Assert.Equal("新备注", updated.DisplayName);
    }

    [Fact]
    public async Task Update_ResolvedAccountNaturalIdentityChange_IsRejectedWithoutMutation()
    {
        var accounts = new InMemoryGameAccountRepository();
        GameAccount original = ArchiveTestData.Account(Guid.NewGuid());
        await accounts.AddAsync(original);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateGameAccount(accounts).ExecuteAsync(
                original.Id,
                "600000001",
                GameServerRegion.America,
                null));

        Assert.Equal(original, await accounts.GetByIdAsync(original.Id));
    }

    [Fact]
    public async Task Update_UnresolvedAccountConflict_IsRejectedWithoutMutation()
    {
        var accounts = new InMemoryGameAccountRepository();
        Guid archiveId = Guid.NewGuid();
        GameAccount existing = ArchiveTestData.Account(archiveId);
        GameAccount unresolved = ArchiveTestData.Account(
            archiveId,
            uid: "legacy-id",
            region: GameServerRegion.Unknown,
            isPlaceholder: true);
        await accounts.AddAsync(existing);
        await accounts.AddAsync(unresolved);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new UpdateGameAccount(accounts).ExecuteAsync(
                unresolved.Id,
                existing.Uid,
                existing.ServerRegion,
                null));

        Assert.Equal(unresolved, await accounts.GetByIdAsync(unresolved.Id));
    }

    [Fact]
    public async Task Delete_ExistingAccount_RemovesIt()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        PlayerArchive archive =
            SqliteRepositoryTestContext.CreateArchive();
        GameAccount account =
            SqliteRepositoryTestContext.CreateAccount(archive.Id);
        await context.Archives.AddAsync(archive);
        await context.Accounts.AddAsync(account);

        await new DeleteGameAccount(context.AtomicGacha)
            .ExecuteAsync(account.Id);

        Assert.Null(await context.Accounts.GetByIdAsync(account.Id));
    }

    [Fact]
    public async Task Delete_UnknownAccount_ThrowsKeyNotFoundException()
    {
        await using SqliteRepositoryTestContext context =
            SqliteRepositoryTestContext.Create();
        await context.Database.InitializeAsync();
        var service = new DeleteGameAccount(context.AtomicGacha);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetGameAccounts_UnknownArchive_ThrowsKeyNotFoundException()
    {
        var service = new GetGameAccounts(
            new InMemoryPlayerArchiveRepository(),
            new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(Guid.NewGuid()));
    }
}

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
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
            " 123456789 ",
            GameServerRegion.Asia,
            "  主账号  ");

        Assert.Equal("123456789", created.Uid);
        Assert.Equal("主账号", created.DisplayName);
        Assert.False(created.IsPlaceholder);
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
                "123456789",
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
    public async Task Add_UnknownRegion_ThrowsArgumentException()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var service = new AddGameAccount(archives, new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExecuteAsync(
                archive.Id,
                "123456789",
                GameServerRegion.Unknown,
                null));
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
                "123456789",
                GameServerRegion.Asia,
                null));
    }

    [Fact]
    public async Task Update_ExistingPlaceholder_NormalizesAndPersistsFields()
    {
        var accounts = new InMemoryGameAccountRepository();
        GameAccount original = ArchiveTestData.Account(
            Guid.NewGuid(),
            uid: "legacy-id",
            region: GameServerRegion.Unknown,
            isPlaceholder: true);
        await accounts.AddAsync(original);
        var service = new UpdateGameAccount(accounts);

        GameAccount updated = await service.ExecuteAsync(
            original.Id,
            " 123456789 ",
            GameServerRegion.Asia,
            "  已补全  ");

        Assert.Equal("123456789", updated.Uid);
        Assert.Equal(GameServerRegion.Asia, updated.ServerRegion);
        Assert.Equal("已补全", updated.DisplayName);
        Assert.False(updated.IsPlaceholder);
        Assert.Equal(updated, await accounts.GetByIdAsync(original.Id));
    }

    [Fact]
    public async Task Update_ConflictingRegionAndUid_ThrowsInvalidOperationException()
    {
        var accounts = new InMemoryGameAccountRepository();
        GameAccount first = ArchiveTestData.Account(Guid.NewGuid(), uid: "100000001");
        GameAccount second = ArchiveTestData.Account(Guid.NewGuid(), uid: "100000002");
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
                "123456789",
                GameServerRegion.Asia,
                null));
    }

    [Fact]
    public async Task Delete_ExistingAccount_RemovesIt()
    {
        var accounts = new InMemoryGameAccountRepository();
        GameAccount account = ArchiveTestData.Account(Guid.NewGuid());
        await accounts.AddAsync(account);

        await new DeleteGameAccount(accounts).ExecuteAsync(account.Id);

        Assert.Null(await accounts.GetByIdAsync(account.Id));
    }

    [Fact]
    public async Task Delete_UnknownAccount_ThrowsKeyNotFoundException()
    {
        var service = new DeleteGameAccount(new InMemoryGameAccountRepository());

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

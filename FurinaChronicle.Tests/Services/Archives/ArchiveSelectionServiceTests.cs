using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives;

public sealed class ArchiveSelectionServiceTests
{
    [Fact]
    public async Task GetCurrent_ValidSavedSelection_ReturnsWithoutRewritingIt()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount account = ArchiveTestData.Account(archive.Id);
        await archives.AddAsync(archive);
        await accounts.AddAsync(account);
        var saved = new ArchiveSelection(archive.Id, account.Id);
        var store = new InMemoryArchiveSelectionStore { Current = saved };
        var service = new ArchiveSelectionService(store, archives, accounts);

        ArchiveSelection? result = await service.GetCurrentAsync();

        Assert.Equal(saved, result);
        Assert.Equal(0, store.SaveCallCount);
        Assert.Equal(0, store.ClearCallCount);
    }

    [Fact]
    public async Task GetCurrent_DeletedSelectedAccount_SelectsAnotherAvailableAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount remaining = ArchiveTestData.Account(archive.Id);
        await archives.AddAsync(archive);
        await accounts.AddAsync(remaining);
        var store = new InMemoryArchiveSelectionStore
        {
            Current = new ArchiveSelection(archive.Id, Guid.NewGuid())
        };
        var service = new ArchiveSelectionService(store, archives, accounts);

        ArchiveSelection? result = await service.GetCurrentAsync();

        var expected = new ArchiveSelection(archive.Id, remaining.Id);
        Assert.Equal(expected, result);
        Assert.Equal(expected, store.Current);
        Assert.Equal(1, store.SaveCallCount);
    }

    [Fact]
    public async Task GetCurrent_AccountBelongsToDifferentArchive_RepairsSelection()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive firstArchive = ArchiveTestData.Archive("档案 A");
        PlayerArchive secondArchive = ArchiveTestData.Archive("档案 B");
        GameAccount account = ArchiveTestData.Account(secondArchive.Id);
        await archives.AddAsync(firstArchive);
        await archives.AddAsync(secondArchive);
        await accounts.AddAsync(account);
        var store = new InMemoryArchiveSelectionStore
        {
            Current = new ArchiveSelection(firstArchive.Id, account.Id)
        };
        var service = new ArchiveSelectionService(store, archives, accounts);

        ArchiveSelection? result = await service.GetCurrentAsync();

        Assert.Equal(new ArchiveSelection(secondArchive.Id, account.Id), result);
        Assert.Equal(result, store.Current);
    }

    [Fact]
    public async Task GetCurrent_NoAvailableAccount_ClearsSelectionAndReturnsNull()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await archives.AddAsync(archive);
        var store = new InMemoryArchiveSelectionStore
        {
            Current = new ArchiveSelection(archive.Id, Guid.NewGuid())
        };
        var service = new ArchiveSelectionService(
            store,
            archives,
            new InMemoryGameAccountRepository());

        ArchiveSelection? result = await service.GetCurrentAsync();

        Assert.Null(result);
        Assert.Null(store.Current);
        Assert.Equal(1, store.ClearCallCount);
    }

    [Fact]
    public async Task Select_ExistingAccount_SavesMatchingArchiveAndAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount account = ArchiveTestData.Account(archive.Id);
        await archives.AddAsync(archive);
        await accounts.AddAsync(account);
        var store = new InMemoryArchiveSelectionStore();
        var service = new ArchiveSelectionService(store, archives, accounts);

        await service.SelectAsync(account.Id);

        Assert.Equal(new ArchiveSelection(archive.Id, account.Id), store.Current);
    }

    [Fact]
    public async Task Select_UnknownAccount_ThrowsKeyNotFoundException()
    {
        var service = new ArchiveSelectionService(
            new InMemoryArchiveSelectionStore(),
            new InMemoryPlayerArchiveRepository(),
            new InMemoryGameAccountRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.SelectAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Clear_RemovesSavedSelection()
    {
        var store = new InMemoryArchiveSelectionStore
        {
            Current = new ArchiveSelection(Guid.NewGuid(), Guid.NewGuid())
        };
        var service = new ArchiveSelectionService(
            store,
            new InMemoryPlayerArchiveRepository(),
            new InMemoryGameAccountRepository());

        await service.ClearAsync();

        Assert.Null(store.Current);
        Assert.Equal(1, store.ClearCallCount);
    }
    [Fact]
    public async Task GetForArchive_RestoresThatArchivesLastSelectedAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive firstArchive = ArchiveTestData.Archive("Archive A");
        PlayerArchive secondArchive = ArchiveTestData.Archive("Archive B");
        GameAccount firstAccount = ArchiveTestData.Account(
            firstArchive.Id,
            uid: "800000001");
        GameAccount lastFirstArchiveAccount = ArchiveTestData.Account(
            firstArchive.Id,
            uid: "800000002");
        GameAccount secondAccount = ArchiveTestData.Account(
            secondArchive.Id,
            uid: "800000003");
        await archives.AddAsync(firstArchive);
        await archives.AddAsync(secondArchive);
        await accounts.AddAsync(firstAccount);
        await accounts.AddAsync(lastFirstArchiveAccount);
        await accounts.AddAsync(secondAccount);
        var store = new InMemoryArchiveSelectionStore();
        await store.SaveAsync(
            new ArchiveSelection(firstArchive.Id, lastFirstArchiveAccount.Id));
        await store.SaveAsync(
            new ArchiveSelection(secondArchive.Id, secondAccount.Id));
        var service = new ArchiveSelectionService(store, archives, accounts);

        ArchiveSelection? restored =
            await service.GetForArchiveAsync(firstArchive.Id);

        var expected =
            new ArchiveSelection(firstArchive.Id, lastFirstArchiveAccount.Id);
        Assert.Equal(expected, restored);
        Assert.Equal(expected, store.Current);
    }

    [Fact]
    public async Task GetForArchive_DeletedSavedAccount_FallsBackToAvailableAccount()
    {
        var archives = new InMemoryPlayerArchiveRepository();
        var accounts = new InMemoryGameAccountRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        GameAccount available = ArchiveTestData.Account(
            archive.Id,
            uid: "800000001");
        GameAccount deleted = ArchiveTestData.Account(
            archive.Id,
            uid: "800000002");
        await archives.AddAsync(archive);
        await accounts.AddAsync(available);
        await accounts.AddAsync(deleted);
        var store = new InMemoryArchiveSelectionStore();
        await store.SaveAsync(new ArchiveSelection(archive.Id, deleted.Id));
        await accounts.DeleteAsync(deleted.Id);
        var service = new ArchiveSelectionService(store, archives, accounts);

        ArchiveSelection? restored =
            await service.GetForArchiveAsync(archive.Id);

        Assert.Equal(
            new ArchiveSelection(archive.Id, available.Id),
            restored);
        Assert.Equal(restored, store.Current);
    }


}

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Tests.TestDoubles;

internal sealed class InMemoryPlayerArchiveRepository : IPlayerArchiveRepository
{
    private readonly Dictionary<Guid, PlayerArchive> archives = [];

    public Task<IReadOnlyList<PlayerArchive>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<PlayerArchive>>(archives.Values.ToArray());
    }

    public Task<PlayerArchive?> GetByIdAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        archives.TryGetValue(archiveId, out PlayerArchive? archive);
        return Task.FromResult(archive);
    }

    public Task AddAsync(
        PlayerArchive archive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!archives.TryAdd(archive.Id, archive))
        {
            throw new InvalidOperationException("档案已经存在。");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        PlayerArchive archive,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!archives.ContainsKey(archive.Id))
        {
            throw new KeyNotFoundException();
        }

        archives[archive.Id] = archive;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!archives.Remove(archiveId))
        {
            throw new KeyNotFoundException();
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryGameAccountRepository : IGameAccountRepository
{
    private readonly Dictionary<Guid, GameAccount> accounts = [];

    public Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<GameAccount> result = accounts.Values
            .Where(account => account.PlayerArchiveId == archiveId)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<GameAccount?> GetByIdAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        accounts.TryGetValue(gameAccountId, out GameAccount? account);
        return Task.FromResult(account);
    }

    public Task<GameAccount?> FindByUidAsync(
        GameServerRegion serverRegion,
        string uid,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        GameAccount? result = accounts.Values.FirstOrDefault(
            account => account.ServerRegion == serverRegion && account.Uid == uid);
        return Task.FromResult(result);
    }

    public Task AddAsync(
        GameAccount account,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!accounts.TryAdd(account.Id, account))
        {
            throw new InvalidOperationException("账号已经存在。");
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        GameAccount account,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!accounts.ContainsKey(account.Id))
        {
            throw new KeyNotFoundException();
        }

        accounts[account.Id] = account;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        Guid gameAccountId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!accounts.Remove(gameAccountId))
        {
            throw new KeyNotFoundException();
        }

        return Task.CompletedTask;
    }
}

internal sealed class InMemoryArchiveSelectionStore : IArchiveSelectionStore
{
    public ArchiveSelection? Current { get; set; }

    public int SaveCallCount { get; private set; }

    public int ClearCallCount { get; private set; }

    public Task<ArchiveSelection?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Current);
    }

    public Task SaveAsync(
        ArchiveSelection selection,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = selection;
        SaveCallCount++;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Current = null;
        ClearCallCount++;
        return Task.CompletedTask;
    }
}

internal static class ArchiveTestData
{
    public static PlayerArchive Archive(string name = "测试档案", Guid? id = null)
    {
        DateTimeOffset now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
        return new PlayerArchive(id ?? Guid.NewGuid(), name, now, now);
    }

    public static GameAccount Account(
        Guid archiveId,
        string uid = "123456789",
        GameServerRegion region = GameServerRegion.Asia,
        Guid? id = null,
        string? displayName = "测试账号",
        bool isPlaceholder = false)
    {
        DateTimeOffset now = new(2026, 7, 16, 10, 0, 0, TimeSpan.Zero);
        return new GameAccount(
            id ?? Guid.NewGuid(),
            archiveId,
            uid,
            region,
            displayName,
            isPlaceholder,
            now,
            now);
    }
}

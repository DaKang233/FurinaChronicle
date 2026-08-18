using FurinaChronicle.Core.Archives;
using SQLite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqlitePlayerArchiveRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsAllFields()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();

        await context.Archives.AddAsync(archive);

        PlayerArchive? loaded = await context.Archives.GetByIdAsync(archive.Id);

        Assert.Equal(archive, loaded);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        PlayerArchive? loaded = await context.Archives.GetByIdAsync(Guid.NewGuid());

        Assert.Null(loaded);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEveryArchive()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive first = SqliteRepositoryTestContext.CreateArchive("档案 A");
        PlayerArchive second = SqliteRepositoryTestContext.CreateArchive("档案 B");
        await context.Archives.AddAsync(first);
        await context.Archives.AddAsync(second);

        IReadOnlyList<PlayerArchive> loaded = await context.Archives.GetAllAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Contains(first, loaded);
        Assert.Contains(second, loaded);
    }

    [Fact]
    public async Task UpdateAsync_ExistingArchive_PersistsChanges()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive original = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(original);
        PlayerArchive updated = original with
        {
            Name = "重命名后的档案",
            UpdatedAt = original.UpdatedAt.AddHours(1)
        };

        await context.Archives.UpdateAsync(updated);

        Assert.Equal(updated, await context.Archives.GetByIdAsync(original.Id));
    }

    [Fact]
    public async Task UpdateAsync_UnknownArchive_ThrowsKeyNotFoundException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => context.Archives.UpdateAsync(archive));
    }

    [Fact]
    public async Task DeleteAsync_ExistingArchive_RemovesIt()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);

        await context.Archives.DeleteAsync(archive.Id);

        Assert.Null(await context.Archives.GetByIdAsync(archive.Id));
    }

    [Fact]
    public async Task DeleteAsync_UnknownArchive_ThrowsKeyNotFoundException()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => context.Archives.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AddAsync_DuplicateId_ThrowsSqliteException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        PlayerArchive archive = SqliteRepositoryTestContext.CreateArchive();
        await context.Archives.AddAsync(archive);

        await Assert.ThrowsAsync<SQLiteException>(
            () => context.Archives.AddAsync(archive));
    }

    [Fact]
    public async Task GetByIdAsync_EmptyId_ThrowsArgumentException()
    {
        await using var context = SqliteRepositoryTestContext.Create();

        await Assert.ThrowsAsync<ArgumentException>(
            () => context.Archives.GetByIdAsync(Guid.Empty));
    }

    [Fact]
    public async Task AddAsync_CancelledToken_ThrowsOperationCancelledException()
    {
        await using var context = SqliteRepositoryTestContext.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => context.Archives.AddAsync(
                SqliteRepositoryTestContext.CreateArchive(),
                cancellation.Token));
    }
}

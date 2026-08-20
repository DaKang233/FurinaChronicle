using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Services.Archives;

public sealed class PlayerArchiveUseCaseTests
{
    [Fact]
    public async Task Create_NormalizesNameAndPersistsArchive()
    {
        var repository = new InMemoryPlayerArchiveRepository();
        var service = new CreatePlayerArchive(repository);
        DateTimeOffset before = DateTimeOffset.UtcNow;

        PlayerArchive created = await service.ExecuteAsync("  我的档案  ");

        DateTimeOffset after = DateTimeOffset.UtcNow;
        Assert.Equal("我的档案", created.Name);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.InRange(created.CreatedAt, before, after);
        Assert.Equal(created.CreatedAt, created.UpdatedAt);
        Assert.Equal(created, await repository.GetByIdAsync(created.Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_ThrowsArgumentException(string name)
    {
        var service = new CreatePlayerArchive(new InMemoryPlayerArchiveRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => service.ExecuteAsync(name));
    }

    [Fact]
    public async Task Create_NameLongerThanFiftyCharacters_ThrowsArgumentException()
    {
        var service = new CreatePlayerArchive(new InMemoryPlayerArchiveRepository());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ExecuteAsync(new string('a', 51)));
    }

    [Fact]
    public async Task Rename_NormalizesAndPersistsNewName()
    {
        var repository = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await repository.AddAsync(archive);
        var service = new RenamePlayerArchive(repository);

        PlayerArchive renamed = await service.ExecuteAsync(archive.Id, "  新名称  ");

        Assert.Equal("新名称", renamed.Name);
        Assert.True(renamed.UpdatedAt >= archive.UpdatedAt);
        Assert.Equal(renamed, await repository.GetByIdAsync(archive.Id));
    }

    [Fact]
    public async Task Rename_UnknownArchive_ThrowsKeyNotFoundException()
    {
        var service = new RenamePlayerArchive(new InMemoryPlayerArchiveRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(Guid.NewGuid(), "新名称"));
    }

    [Fact]
    public async Task Delete_ExistingArchive_RemovesIt()
    {
        var repository = new InMemoryPlayerArchiveRepository();
        PlayerArchive archive = ArchiveTestData.Archive();
        await repository.AddAsync(archive);
        var service = new DeletePlayerArchive(repository);

        await service.ExecuteAsync(archive.Id);

        Assert.Null(await repository.GetByIdAsync(archive.Id));
    }

    [Fact]
    public async Task Delete_UnknownArchive_ThrowsKeyNotFoundException()
    {
        var service = new DeletePlayerArchive(new InMemoryPlayerArchiveRepository());

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.ExecuteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetPlayerArchives_ReturnsRepositoryContents()
    {
        var repository = new InMemoryPlayerArchiveRepository();
        PlayerArchive first = ArchiveTestData.Archive("档案 A");
        PlayerArchive second = ArchiveTestData.Archive("档案 B");
        await repository.AddAsync(first);
        await repository.AddAsync(second);

        IReadOnlyList<PlayerArchive> result =
            await new GetPlayerArchives(repository).ExecuteAsync();

        Assert.Equal(2, result.Count);
        Assert.Contains(first, result);
        Assert.Contains(second, result);
    }
}

using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence;
using Xunit;

namespace FurinaChronicle.Tests.Infrastructure.Persistence;

public sealed class InMemoryWishRecordRepositoryTests
{
    private static readonly Guid AccountId1 = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");
    private static readonly Guid AccountId2 = Guid.Parse("f3e1c8a0-4b5d-4c9e-9f7a-1d2e3f4b5c6d");

    [Fact]
    public async Task GetRecentAsync_ReturnsRequestedNumberOfRecords()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        // Act
        IReadOnlyList<WishRecord> records =
            await repository.GetRecentAsync(AccountId1, 2);

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsRecordsInDescendingTimeOrder()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        // Act
        IReadOnlyList<WishRecord> records =
            await repository.GetRecentAsync(AccountId1, 20);

        // Assert
        WishRecord[] expectedOrder = records
            .OrderByDescending(record => record.Time)
            .ToArray();

        Assert.Equal(expectedOrder, records);
    }

    [Fact]
    public async Task GetRecentAsync_WhenCountIsLargerThanTotal_ReturnsAll()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        // Act
        IReadOnlyList<WishRecord> records =
            await repository.GetRecentAsync(AccountId1, 100);

        // Assert
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task GetRecentAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        cancellationTokenSource.Cancel();

        // Act + Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () =>
            {
                await repository.GetRecentAsync(
                    AccountId1,
                    20,
                    cancellationTokenSource.Token);
            });
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsRecordsWithRequiredValues()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        // Act
        IReadOnlyList<WishRecord> records =
            await repository.GetRecentAsync(AccountId1, 20);

        // Assert
        Assert.All(
            records,
            record =>
            {
                Assert.NotEqual(Guid.Empty, record.GameAccountId);
                Assert.NotEmpty(record.ExternalRecordId);
                Assert.NotEmpty(record.ItemName);
                Assert.InRange(record.RankType, 3, 5);
            });
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsDifferentWishRecordsWhenIdIsNotSame()
    {
        var repository = new InMemoryWishRecordRepository();

        IReadOnlyList<WishRecord> records1 = await repository.GetRecentAsync(AccountId1, 20);
        IReadOnlyList<WishRecord> records2 = await repository.GetRecentAsync(AccountId2, 20);

        Assert.NotEqual(records1, records2);
        Assert.NotEqual(records1.Count, records2.Count);
        Assert.All(records1, record => Assert.Equal(AccountId1, record.GameAccountId));
        Assert.All(records2, record => Assert.Equal(AccountId2, record.GameAccountId));
    }

    [Fact]
    public async Task SaveBatchAsync_SameIdForDifferentAccounts_IsAllowed()
    {
        var repository = new InMemoryWishRecordRepository(Array.Empty<WishRecord>());
        Guid firstAccoundId = Guid.NewGuid();
        Guid secondAccoundId = Guid.NewGuid();

        WishRecord[] records =
            [
                new WishRecord(firstAccoundId, "same-id", "Furina", 5, DateTimeOffset.UtcNow),
                new WishRecord(secondAccoundId, "same-id", "Furina", 5, DateTimeOffset.UtcNow),
            ];
        var result = await repository.SaveBatchAsync(records);

        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(0, result.DuplicateCount);
    }
}
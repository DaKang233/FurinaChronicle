using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence;
using Xunit;

namespace FurinaChronicle.Tests.Infrastructure.Persistence;

public sealed class InMemoryWishRecordRepositoryTests
{
    [Fact]
    public async Task GetRecentAsync_ReturnsRequestedNumberOfRecords()
    {
        // Arrange
        var repository =
            new InMemoryWishRecordRepository();

        // Act
        IReadOnlyList<WishRecord> records =
            await repository.GetRecentAsync(2);

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
            await repository.GetRecentAsync(20);

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
            await repository.GetRecentAsync(100);

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
            await repository.GetRecentAsync(20);

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
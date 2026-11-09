// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha;
using Xunit;

namespace FurinaChronicle.Tests.Infrastructure.Persistence;

public sealed class InMemoryGachaRecordRepositoryTests
{
    private static readonly Guid AccountId1 = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");
    private static readonly Guid AccountId2 = Guid.Parse("f3e1c8a0-4b5d-4c9e-9f7a-1d2e3f4b5c6d");

    [Fact]
    public async Task GetRecentAsync_ReturnsRequestedNumberOfRecords()
    {
        // Arrange
        var repository =
            new InMemoryGachaRecordRepository();

        // Act
        IReadOnlyList<GachaRecord> records =
            await repository.GetRecentAsync(AccountId1, 2);

        // Assert
        Assert.Equal(2, records.Count);
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsRecordsInDescendingTimeOrder()
    {
        // Arrange
        var repository =
            new InMemoryGachaRecordRepository();

        // Act
        IReadOnlyList<GachaRecord> records =
            await repository.GetRecentAsync(AccountId1, 20);

        // Assert
        GachaRecord[] expectedOrder = records
            .OrderByDescending(record => record.Time)
            .ToArray();

        Assert.Equal(expectedOrder, records);
    }

    [Fact]
    public async Task GetRecentAsync_WhenCountIsLargerThanTotal_ReturnsAll()
    {
        // Arrange
        var repository =
            new InMemoryGachaRecordRepository();

        // Act
        IReadOnlyList<GachaRecord> records =
            await repository.GetRecentAsync(AccountId1, 100);

        // Assert
        Assert.Equal(3, records.Count);
    }

    [Fact]
    public async Task GetRecentAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        // Arrange
        var repository =
            new InMemoryGachaRecordRepository();

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
            new InMemoryGachaRecordRepository();

        // Act
        IReadOnlyList<GachaRecord> records =
            await repository.GetRecentAsync(AccountId1, 20);

        // Assert
        Assert.All(
            records,
            record =>
            {
                Assert.NotEqual(Guid.Empty, record.GameAccountId);
                Assert.NotEmpty(record.ExternalRecordId);
                Assert.NotEmpty(record.ItemName!);
                Assert.InRange(record.RankType!.Value, 3, 5);
            });
    }

    [Fact]
    public async Task GetRecentAsync_ReturnsDifferentGachaRecordsWhenIdIsNotSame()
    {
        var repository = new InMemoryGachaRecordRepository();

        IReadOnlyList<GachaRecord> records1 = await repository.GetRecentAsync(AccountId1, 20);
        IReadOnlyList<GachaRecord> records2 = await repository.GetRecentAsync(AccountId2, 20);

        Assert.NotEqual(records1, records2);
        Assert.NotEqual(records1.Count, records2.Count);
        Assert.All(records1, record => Assert.Equal(AccountId1, record.GameAccountId));
        Assert.All(records2, record => Assert.Equal(AccountId2, record.GameAccountId));
    }

    [Fact]
    public async Task SaveBatchAsync_SameIdForDifferentAccounts_IsAllowed()
    {
        var repository = new InMemoryGachaRecordRepository(Array.Empty<GachaRecord>());
        Guid firstAccoundId = Guid.NewGuid();
        Guid secondAccoundId = Guid.NewGuid();

        GachaRecord[] records =
            [
                new GachaRecord(firstAccoundId, "same-id", "Furina", 5, DateTimeOffset.UtcNow),
                new GachaRecord(secondAccoundId, "same-id", "Furina", 5, DateTimeOffset.UtcNow),
            ];
        var result = await repository.SaveBatchAsync(records);

        Assert.Equal(2, result.InsertedCount);
        Assert.Equal(0, result.DuplicateCount);
    }

    [Fact]
    public async Task SaveBatchAsync_ReplaceExisting_UpdatesSameAccountRecord()
    {
        Guid accountId = Guid.NewGuid();
        DateTimeOffset time = DateTimeOffset.UtcNow;
        var existing = new GachaRecord(
            accountId,
            "same-id",
            "旧名称",
            5,
            time)
        {
            ItemId = "old-id",
            ItemType = "Avatar"
        };
        var incoming = new GachaRecord(
            accountId,
            "same-id",
            "新名称",
            4,
            time.AddMinutes(1))
        {
            ItemId = "new-id",
            ItemType = "Weapon"
        };
        var repository = new InMemoryGachaRecordRepository([existing]);

        GachaSaveResult result = await repository.SaveBatchAsync(
            [incoming],
            conflictPolicy: GachaRecordConflictPolicy.ReplaceExisting);

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.DuplicateCount);
        Assert.Equal(
            incoming,
            Assert.Single(await repository.GetRecentAsync(accountId, 20)));
    }
}

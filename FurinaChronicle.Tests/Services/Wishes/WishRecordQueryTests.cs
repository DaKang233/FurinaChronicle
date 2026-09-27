using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Wishes;

namespace FurinaChronicle.Tests.Services.Wishes;

public sealed class WishRecordQueryTests
{
    [Fact]
    public async Task QueryAsync_CombinesAccountsAndAppliesAllFilters()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        DateTimeOffset start = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        WishRecord[] records =
        [
            Record(first, "1", 5, "301", start.AddHours(1)),
            Record(second, "2", 5, "400", start.AddHours(2)),
            Record(second, "3", 4, "301", start.AddHours(3)),
            Record(first, "4", 5, "302", start.AddHours(4)),
            Record(Guid.NewGuid(), "5", 5, "301", start.AddHours(5)),
            Record(first, "6", 5, "301", start.AddDays(-1))
        ];
        var repository = new InMemoryWishRecordRepository(records);
        var query = new WishRecordQuery(
            [first, second],
            RankTypes: new HashSet<int> { 5 },
            PoolGroups: new HashSet<WishPoolGroup>
            {
                WishPoolGroup.CharacterEvent
            },
            StartTime: start,
            SortOrder: WishRecordSortOrder.OldestFirst);

        IReadOnlyList<WishRecord> result =
            await repository.QueryAsync(query);
        int count = await repository.CountAsync(query);

        Assert.Equal(2, count);
        Assert.Equal(["1", "2"], result.Select(record => record.ExternalRecordId));
    }

    [Fact]
    public void Validate_InvalidRange_ThrowsArgumentException()
    {
        var query = new WishRecordQuery(
            [Guid.NewGuid()],
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Throws<ArgumentException>(query.Validate);
    }

    private static WishRecord Record(
        Guid accountId,
        string id,
        int rank,
        string type,
        DateTimeOffset time)
    {
        return new WishRecord(accountId, id, id, rank, time)
        {
            UigfGachaType = type
        };
    }
}

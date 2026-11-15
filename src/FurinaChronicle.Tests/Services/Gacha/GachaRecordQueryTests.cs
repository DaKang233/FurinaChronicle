// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Tests.Services.Gacha;

public sealed class GachaRecordQueryTests
{
    [Fact]
    public async Task QueryAsync_CombinesAccountsAndAppliesAllFilters()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        DateTimeOffset start = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);
        GachaRecord[] records =
        [
            Record(first, "1", 5, "301", start.AddHours(1)),
            Record(second, "2", 5, "400", start.AddHours(2)),
            Record(second, "3", 4, "301", start.AddHours(3)),
            Record(first, "4", 5, "302", start.AddHours(4)),
            Record(Guid.NewGuid(), "5", 5, "301", start.AddHours(5)),
            Record(first, "6", 5, "301", start.AddDays(-1))
        ];
        var repository = new InMemoryGachaRecordRepository(records);
        var query = new GachaRecordQuery(
            [first, second],
            RankTypes: new HashSet<int> { 5 },
            PoolGroups: new HashSet<GachaPoolGroup>
            {
                GachaPoolGroup.CharacterEvent
            },
            StartTime: start,
            SortOrder: GachaRecordSortOrder.OldestFirst);

        IReadOnlyList<GachaRecord> result =
            await repository.QueryAsync(query);
        int count = await repository.CountAsync(query);

        Assert.Equal(2, count);
        Assert.Equal(["1", "2"], result.Select(record => record.ExternalRecordId));
    }

    [Fact]
    public void Validate_InvalidRange_ThrowsArgumentException()
    {
        var query = new GachaRecordQuery(
            [Guid.NewGuid()],
            StartTime: DateTimeOffset.UtcNow,
            EndTime: DateTimeOffset.UtcNow.AddDays(-1));

        Assert.Throws<ArgumentException>(query.Validate);
    }

    private static GachaRecord Record(
        Guid accountId,
        string id,
        int rank,
        string type,
        DateTimeOffset time)
    {
        return new GachaRecord(accountId, id, id, rank, time)
        {
            UigfGachaType = type
        };
    }
}

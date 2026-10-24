// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Tests.Services.Gacha;

public sealed class GetGachaRecordPageTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsRequestedPageAndTotalCount()
    {
        Guid accountId = Guid.NewGuid();
        GachaRecord[] records = Enumerable.Range(1, 120)
            .Select(index => new GachaRecord(
                accountId,
                index.ToString("D3"),
                $"Item {index}",
                3,
                new DateTimeOffset(
                    2026,
                    1,
                    1,
                    0,
                    0,
                    0,
                    TimeSpan.Zero).AddMinutes(index)))
            .ToArray();
        var repository = new InMemoryGachaRecordRepository(records);
        var service = new GetGachaRecordPage(repository);

        GachaRecordPage page =
            await service.ExecuteAsync(
                accountId,
                pageNumber: 2,
                pageSize: 50);

        Assert.Equal(120, page.TotalCount);
        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.PageNumber);
        Assert.Equal(50, page.Records.Count);
        Assert.Equal("070", page.Records[0].ExternalRecordId);
        Assert.Equal("021", page.Records[^1].ExternalRecordId);
    }

    [Fact]
    public async Task ExecuteAsync_PageBeyondEnd_ReturnsLastPage()
    {
        Guid accountId = Guid.NewGuid();
        GachaRecord[] records = Enumerable.Range(1, 12)
            .Select(index => new GachaRecord(
                accountId,
                index.ToString(),
                $"Item {index}",
                3,
                DateTimeOffset.UnixEpoch.AddMinutes(index)))
            .ToArray();
        var service = new GetGachaRecordPage(
            new InMemoryGachaRecordRepository(records));

        GachaRecordPage page =
            await service.ExecuteAsync(
                accountId,
                pageNumber: 99,
                pageSize: 5);

        Assert.Equal(3, page.PageNumber);
        Assert.Equal(2, page.Records.Count);
    }

    [Fact]
    public async Task ExecuteAsync_QuerySupportsMultipleAccountsAndFilters()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        GachaRecord[] records =
        [
            new(first, "1", "Five A", 5, DateTimeOffset.UnixEpoch)
            {
                UigfGachaType = "301"
            },
            new(second, "2", "Five B", 5, DateTimeOffset.UnixEpoch.AddMinutes(1))
            {
                UigfGachaType = "400"
            },
            new(first, "3", "Four", 4, DateTimeOffset.UnixEpoch.AddMinutes(2))
            {
                UigfGachaType = "301"
            }
        ];
        var service = new GetGachaRecordPage(
            new InMemoryGachaRecordRepository(records));
        var query = new GachaRecordQuery(
            [first, second],
            RankTypes: new HashSet<int> { 5 },
            PoolGroups: new HashSet<GachaPoolGroup>
            {
                GachaPoolGroup.CharacterEvent
            });

        GachaRecordPage page =
            await service.ExecuteAsync(query, pageNumber: 1, pageSize: 20);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(
            ["2", "1"],
            page.Records.Select(record => record.ExternalRecordId));
    }
}

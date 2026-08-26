using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Infrastructure.Persistence;
using FurinaChronicle.Services.Wishes;

namespace FurinaChronicle.Tests.Services.Wishes;

public sealed class GetWishRecordPageTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsRequestedPageAndTotalCount()
    {
        Guid accountId = Guid.NewGuid();
        WishRecord[] records = Enumerable.Range(1, 120)
            .Select(index => new WishRecord(
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
        var repository = new InMemoryWishRecordRepository(records);
        var service = new GetWishRecordPage(repository);

        WishRecordPage page =
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
        WishRecord[] records = Enumerable.Range(1, 12)
            .Select(index => new WishRecord(
                accountId,
                index.ToString(),
                $"Item {index}",
                3,
                DateTimeOffset.UnixEpoch.AddMinutes(index)))
            .ToArray();
        var service = new GetWishRecordPage(
            new InMemoryWishRecordRepository(records));

        WishRecordPage page =
            await service.ExecuteAsync(
                accountId,
                pageNumber: 99,
                pageSize: 5);

        Assert.Equal(3, page.PageNumber);
        Assert.Equal(2, page.Records.Count);
    }
}

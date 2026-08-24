using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Wishes;
using Xunit;

namespace FurinaChronicle.Tests.Services.Wishes;

public sealed class GetRecentWishRecordsTests
{
    private static readonly Guid AccountId = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");

    [Fact]
    public async Task ExecuteAsync_ReturnsRecordsProvidedByRepository()
    {
        // Arrange：准备测试环境
        IReadOnlyList<WishRecord> expectedRecords =
        [
            new WishRecord(
                AccountId,
                "100000000000000001",
                "芙宁娜",
                5,
                new DateTimeOffset(
                    2026, 7, 16, 18, 30, 0,
                    TimeSpan.FromHours(8)))
        ];

        var repository = new StubWishRecordRepository
        {
            Result = expectedRecords
        };

        var service = new GetRecentWishRecords(repository);

        // Act：执行被测试的代码
        IReadOnlyList<WishRecord> actualRecords =
            await service.ExecuteAsync(AccountId, 1);

        // Assert：检查结果
        Assert.Equal(expectedRecords, actualRecords);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsCountAndCancellationToken()
    {
        // Arrange
        var repository = new StubWishRecordRepository();
        var service = new GetRecentWishRecords(repository);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        CancellationToken cancellationToken =
            cancellationTokenSource.Token;

        // Act
        await service.ExecuteAsync(
            gameAccountId: AccountId,
            count: 7,
            cancellationToken);

        // Assert
        Assert.Equal(7, repository.ReceivedCount);
        Assert.Equal(
            cancellationToken,
            repository.ReceivedCancellationToken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task ExecuteAsync_WhenCountIsNotPositive_ThrowsException(
        int count)
    {
        // Arrange
        var repository = new StubWishRecordRepository();
        var service = new GetRecentWishRecords(repository);

        // Act + Assert
        ArgumentOutOfRangeException exception =
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => service.ExecuteAsync(AccountId, count));

        Assert.Equal("count", exception.ParamName);
    }

    [Fact]
    public async Task ExecuteAsync_WhenRepositoryIsEmpty_ReturnsEmptyList()
    {
        // Arrange
        var repository = new StubWishRecordRepository
        {
            Result = []
        };

        var service = new GetRecentWishRecords(repository);

        // Act
        IReadOnlyList<WishRecord> records =
            await service.ExecuteAsync(AccountId);

        // Assert
        Assert.Empty(records);
    }

    /// <summary>
    /// 只为 GetRecentWishRecords 测试服务的仓储替身。
    /// 它不进行真实存储，只记录上层传入了什么。
    /// </summary>
    private sealed class StubWishRecordRepository : IWishRecordRepository
    {
        public IReadOnlyList<WishRecord> Result { get; init; } = [];

        public Guid ReceviedGameAccountId { get; private set; }

        public int? ReceivedCount { get; private set; }

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public Task<IReadOnlyList<WishRecord>> GetRecentAsync(
            Guid gameAccountId,
            int count,
            CancellationToken cancellationToken = default)
        {
            ReceviedGameAccountId = gameAccountId;
            ReceivedCount = count;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<WishRecord>> GetPageAsync(
            Guid gameAccountId,
            int offset,
            int count,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "该测试替身只用于测试最近记录查询。");
        }

        public Task<int> CountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "该测试替身只用于测试最近记录查询。");
        }


        public Task<WishSaveResult> SaveBatchAsync(IReadOnlyCollection<WishRecord> records, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("该测试替身只用于测试查询功能。");
        }
    }
}
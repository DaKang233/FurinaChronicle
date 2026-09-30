// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Gacha;
using Xunit;

namespace FurinaChronicle.Tests.Services.Gacha;

public sealed class GetRecentGachaRecordsTests
{
    private static readonly Guid AccountId = Guid.Parse("90ca2f7f-327b-47bb-9562-0fdb3217dd26");

    [Fact]
    public async Task ExecuteAsync_ReturnsRecordsProvidedByRepository()
    {
        // Arrange：准备测试环境
        IReadOnlyList<GachaRecord> expectedRecords =
        [
            new GachaRecord(
                AccountId,
                "100000000000000001",
                "芙宁娜",
                5,
                new DateTimeOffset(
                    2026, 7, 16, 18, 30, 0,
                    TimeSpan.FromHours(8)))
        ];

        var repository = new StubGachaRecordRepository
        {
            Result = expectedRecords
        };

        var service = new GetRecentGachaRecords(repository);

        // Act：执行被测试的代码
        IReadOnlyList<GachaRecord> actualRecords =
            await service.ExecuteAsync(AccountId, 1);

        // Assert：检查结果
        Assert.Equal(expectedRecords, actualRecords);
    }

    [Fact]
    public async Task ExecuteAsync_ForwardsCountAndCancellationToken()
    {
        // Arrange
        var repository = new StubGachaRecordRepository();
        var service = new GetRecentGachaRecords(repository);

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
        var repository = new StubGachaRecordRepository();
        var service = new GetRecentGachaRecords(repository);

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
        var repository = new StubGachaRecordRepository
        {
            Result = []
        };

        var service = new GetRecentGachaRecords(repository);

        // Act
        IReadOnlyList<GachaRecord> records =
            await service.ExecuteAsync(AccountId);

        // Assert
        Assert.Empty(records);
    }

    /// <summary>
    /// 只为 GetRecentGachaRecords 测试服务的仓储替身。
    /// 它不进行真实存储，只记录上层传入了什么。
    /// </summary>
    private sealed class StubGachaRecordRepository : IGachaRecordRepository
    {
        public IReadOnlyList<GachaRecord> Result { get; init; } = [];

        public Guid ReceviedGameAccountId { get; private set; }

        public int? ReceivedCount { get; private set; }

        public CancellationToken ReceivedCancellationToken
        {
            get;
            private set;
        }

        public Task<IReadOnlyList<GachaRecord>> GetRecentAsync(
            Guid gameAccountId,
            int count,
            CancellationToken cancellationToken = default)
        {
            ReceviedGameAccountId = gameAccountId;
            ReceivedCount = count;
            ReceivedCancellationToken = cancellationToken;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<GachaRecord>> GetPageAsync(
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

        public Task<IReadOnlyList<GachaRecord>> QueryAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "该测试替身只用于测试最近记录查询。");
        }

        public Task<int> CountAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "该测试替身只用于测试最近记录查询。");
        }


        public Task<GachaSaveResult> SaveBatchAsync(
            IReadOnlyCollection<GachaRecord> records,
            CancellationToken cancellationToken = default,
            GachaRecordConflictPolicy conflictPolicy =
                GachaRecordConflictPolicy.PreserveExisting)
        {
            throw new NotSupportedException("该测试替身只用于测试查询功能。");
        }
    }
}

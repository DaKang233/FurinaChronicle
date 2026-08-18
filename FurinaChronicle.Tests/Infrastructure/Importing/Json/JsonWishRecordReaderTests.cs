using System.Text;
using FurinaChronicle.Infrastructure.Importing.Json;
using FurinaChronicle.Services.Wishes.Importing;
using Xunit;

namespace FurinaChronicle.Tests.Infrastructure.Importing.Json;

public sealed class JsonWishRecordReaderTests
{
    private static readonly Guid AccountId =
        Guid.Parse("11ec0244-0f33-450a-988a-029b805cbb20");

    [Fact]
    public async Task ReadAsync_ReturnsValidRecordsAndInvalidErrors()
    {
        // Arrange
        var reader = new JsonWishRecordReader();

        await using MemoryStream stream = CreateStream(SampleJson);

        // Act
        WishReadResult result = await reader.ReadAsync(stream, AccountId);

        // Assert
        // 重复记录在 Reader 阶段仍然算有效，
        // 去重由 ImportWishRecords 完成。
        Assert.Equal(4, result.Records.Count);
        Assert.Single(result.Errors);

        Assert.Equal("missing_id",result.Errors[0].Code);

        Assert.All(result.Records, record => Assert.Equal(AccountId, record.GameAccountId));
    }

    [Fact]
    public async Task ReadAsync_WhenJsonIsMalformed_ThrowsFormatException()
    {
        // Arrange
        var reader = new JsonWishRecordReader();

        await using MemoryStream stream =
            CreateStream(
                """
                { "list": [
                """);

        // Act + Assert
        await Assert.ThrowsAsync<WishImportFormatException>(async () => await reader.ReadAsync(stream, AccountId));
    }

    private static MemoryStream CreateStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    private const string SampleJson =
        """
        {
          "list": [
            {
              "id": "200000000000000001",
              "name": "芙宁娜",
              "rank_type": "5",
              "time": "2026-07-16T18:30:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "200000000000000003",
              "name": "黎明神剑",
              "rank_type": "3",
              "time": "2026-07-16T18:28:00+08:00"
            },
            {
              "id": "200000000000000002",
              "name": "夏洛蒂",
              "rank_type": "4",
              "time": "2026-07-16T18:29:00+08:00"
            },
            {
              "id": "",
              "name": "无效记录",
              "rank_type": "4",
              "time": "2026-07-16T18:27:00+08:00"
            }
          ]
        }
        """;
}
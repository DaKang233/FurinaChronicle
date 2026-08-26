using System.Text;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Uigf.V4_2;

public sealed class UigfV42GachaReaderTests
{
    [Fact]
    public async Task ReadAsync_ValidHk4e_ParsesRequiredAndOptionalFields()
    {
        const string json =
            """
            {
              "info": {
                "export_timestamp": "1787500000",
                "export_app": "FurinaChronicle.Tests",
                "export_app_version": "1.0.0",
                "version": "v4.2"
              },
              "hk4e": [{
                "uid": 800000001,
                "timezone": 8,
                "lang": "zh-cn",
                "list": [{
                  "uigf_gacha_type": "301",
                  "gacha_type": "400",
                  "item_id": "10000089",
                  "count": "1",
                  "time": "2026-08-24 12:30:00",
                  "name": "Furina",
                  "item_type": "Avatar",
                  "rank_type": "5",
                  "id": "1234567890123456789"
                }]
              }]
            }
            """;

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal("v4.2", result.Info.FormatVersion);
        Assert.Equal(1, result.TotalRecordCount);
        GachaSourceAccount account = Assert.Single(result.Accounts);
        Assert.Equal("800000001", account.Uid);
        Assert.Equal(8, account.Timezone);
        GachaSourceRecord record = Assert.Single(account.Records);
        Assert.Equal("10000089", record.ItemId);
        Assert.Equal("400", record.GachaType);
        Assert.Equal("301", record.UigfGachaType);
        Assert.Equal(5, record.RankType);
        Assert.Equal(TimeSpan.FromHours(8), record.Time.Offset);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task ReadAsync_MissingOptionalMetadata_IsStillValid()
    {
        string json = TestUigfJson.Create(TestUigfJson.Record("1", "10000089"));

        GachaSourceRecord record = Assert.Single(
            Assert.Single((await ReadAsync(json)).Accounts).Records);

        Assert.Null(record.ItemName);
        Assert.Null(record.ItemType);
        Assert.Null(record.RankType);
        Assert.Equal(1, record.Count);
    }

    [Fact]
    public async Task ReadAsync_InvalidRecord_ReturnsErrorWithoutDiscardingValidRecord()
    {
        string json = TestUigfJson.Create(
            TestUigfJson.Record("1", "10000089"),
            """{"uigf_gacha_type":"301","gacha_type":"301","item_id":"10000089","time":"2026-08-24 12:31:00","id":""}""");

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal(2, result.TotalRecordCount);
        Assert.Single(Assert.Single(result.Accounts).Records);
        Assert.Equal("invalid_id", Assert.Single(result.Errors).Code);
    }
    [Fact]
    public async Task ReadAsync_OffsetOverflowingTime_ReturnsRecordErrorAndContinues()
    {
        const string overflowingRecord =
            """
            {
              "uigf_gacha_type": "301",
              "gacha_type": "301",
              "item_id": "10000089",
              "time": "0001-01-01 00:00:00",
              "id": "1"
            }
            """;
        string json = TestUigfJson.Create(
                overflowingRecord,
                TestUigfJson.Record("2", "10000089"))
            .Replace(
                "\"timezone\": 8",
                "\"timezone\": 14",
                StringComparison.Ordinal);

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal(2, result.TotalRecordCount);
        GachaSourceRecord validRecord = Assert.Single(
            Assert.Single(result.Accounts).Records);
        Assert.Equal("2", validRecord.ExternalRecordId);
        GachaReadError error = Assert.Single(result.Errors);
        Assert.Equal("invalid_time", error.Code);
        Assert.Equal(1, error.RecordIndex);
    }


    [Fact]
    public async Task ReadAsync_UnsupportedVersion_ThrowsFormatException()
    {
        string json = TestUigfJson.Create(TestUigfJson.Record("1", "10000089"))
            .Replace("\"v4.2\"", "\"v4.1\"", StringComparison.Ordinal);

        await Assert.ThrowsAsync<GachaImportFormatException>(() => ReadAsync(json));
    }

    private static async Task<GachaReadResult> ReadAsync(string json)
    {
        var reader = new UigfV42GachaReader();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return await reader.ReadAsync(stream);
    }
}

internal static class TestUigfJson
{
    public static string Record(string id, string itemId)
    {
        return $$"""
            {
              "uigf_gacha_type": "301",
              "gacha_type": "301",
              "item_id": "{{itemId}}",
              "time": "2026-08-24 12:30:00",
              "id": "{{id}}"
            }
            """;
    }

    public static string Create(params string[] records)
    {
        return $$"""
            {
              "info": {
                "export_timestamp": 1787500000,
                "export_app": "FurinaChronicle.Tests",
                "export_app_version": "1.0.0",
                "version": "v4.2"
              },
              "hk4e": [{
                "uid": "800000001",
                "timezone": 8,
                "lang": "zh-cn",
                "list": [{{string.Join(",", records)}}]
              }]
            }
            """;
    }

    public static string CreateTwoAccounts()
    {
        return $$"""
            {
              "info": {
                "export_timestamp": 1787500000,
                "export_app": "FurinaChronicle.Tests",
                "export_app_version": "1.0.0",
                "version": "v4.2"
              },
              "hk4e": [
                {
                  "uid": "800000001",
                  "timezone": 8,
                  "list": [{{Record("1", "10000089")}},{{Record("2", "11401")}}]
                },
                {
                  "uid": "600000001",
                  "timezone": -5,
                  "list": [{{Record("1", "10000089")}}]
                }
              ]
            }
            """;
    }
}

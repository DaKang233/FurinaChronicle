using System.Text;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Services.Gacha.Importing;
using TestUigfJson = FurinaChronicle.Tests.Infrastructure.Gacha.Uigf.V4_2.TestUigfJson;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Uigf.Compatibility;

public sealed class UigfCompatibleGachaReaderTests
{
    [Fact]
    public async Task ReadAsync_V22WithoutItemId_ResolvesNameAndNormalizesDocument()
    {
        const string json =
            """
            {
              "info": {
                "uid": "216316388",
                "lang": "zh-cn",
                "export_timestamp": "1735552482000",
                "export_app": "Legacy Exporter",
                "export_app_version": "3.0.0",
                "uigf_version": "v2.2"
              },
              "list": [{
                "uigf_gacha_type": "301",
                "gacha_type": "400",
                "item_id": "",
                "count": "1",
                "time": "2024-12-30 17:50:00",
                "name": "芙宁娜",
                "item_type": "角色",
                "rank_type": "5",
                "id": "1735471200000558388"
              }]
            }
            """;

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal("v4.2", result.Info.FormatVersion);
        Assert.Equal(1735552482, result.Info.ExportTimestamp);
        GachaSourceAccount account = Assert.Single(result.Accounts);
        Assert.Equal("216316388", account.Uid);
        Assert.Equal(8, account.Timezone);
        Assert.Equal("zh-cn", account.Language);
        GachaSourceRecord record = Assert.Single(account.Records);
        Assert.Equal("10000089", record.ItemId);
        Assert.Equal("芙宁娜", record.ItemName);
        Assert.Equal(5, record.RankType);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("v2.3", null, 8)]
    [InlineData("v2.4", -5, -5)]
    [InlineData("v3.0", 1, 1)]
    public async Task ReadAsync_LegacyVersionWithItemId_UpgradesToV42(
        string version,
        int? regionTimezone,
        int expectedTimezone)
    {
        string timezoneProperty = regionTimezone is null
            ? string.Empty
            : $",\"region_time_zone\":{regionTimezone}";
        string json = $$"""
            {
              "info": {
                "uid": "216316388",
                "uigf_version": "{{version}}"
                {{timezoneProperty}}
              },
              "list": [{
                "uigf_gacha_type": "301",
                "gacha_type": "301",
                "item_id": "10000089",
                "time": "2024-12-30 17:50:00",
                "id": "1735471200000558388"
              }]
            }
            """;

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal("v4.2", result.Info.FormatVersion);
        Assert.Equal(expectedTimezone, Assert.Single(result.Accounts).Timezone);
        Assert.Equal(
            "10000089",
            Assert.Single(Assert.Single(result.Accounts).Records).ItemId);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData("v4.0")]
    [InlineData("v4.1")]
    [InlineData("v4.2")]
    public async Task ReadAsync_V4Document_UsesCurrentReader(string version)
    {
        string json = TestUigfJson
            .Create(TestUigfJson.Record("1", "10000089"))
            .Replace("\"v4.2\"", $"\"{version}\"", StringComparison.Ordinal);

        GachaReadResult result = await ReadAsync(json);

        Assert.Equal("v4.2", result.Info.FormatVersion);
        Assert.Single(Assert.Single(result.Accounts).Records);
    }

    [Fact]
    public async Task ReadAsync_V22UnknownName_ThrowsWithoutDroppingRecord()
    {
        const string json =
            """
            {
              "info": {
                "uid": "216316388",
                "lang": "zh-cn",
                "uigf_version": "v2.2"
              },
              "list": [{
                "uigf_gacha_type": "301",
                "gacha_type": "301",
                "time": "2024-12-30 17:50:00",
                "name": "不存在的物品",
                "id": "1"
              }]
            }
            """;

        GachaImportFormatException exception =
            await Assert.ThrowsAsync<GachaImportFormatException>(
                () => ReadAsync(json));

        Assert.Contains("record 1", exception.Message);
        Assert.Contains("不存在的物品", exception.Message);
    }

    [Fact]
    public async Task ReadAsync_UnknownVersion_ReportsSupportedVersions()
    {
        const string json =
            """{"info":{"uigf_version":"v2.1"},"list":[]}""";

        GachaImportFormatException exception =
            await Assert.ThrowsAsync<GachaImportFormatException>(
                () => ReadAsync(json));

        Assert.Contains("v2.2", exception.Message);
        Assert.Contains("v4.2", exception.Message);
    }

    private static async Task<GachaReadResult> ReadAsync(string json)
    {
        var reader = new UigfCompatibleGachaReader(
            new UigfV42GachaReader(),
            new StubLocalizationSource(),
            TimeProvider.System);
        await using var stream = new MemoryStream(
            Encoding.UTF8.GetBytes(json));
        return await reader.ReadAsync(stream);
    }

    private sealed class StubLocalizationSource : IGachaLocalizationSource
    {
        public Task<IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, string>>> LoadAsync(
                GachaGame game,
                CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
                result = new Dictionary<
                    string,
                    IReadOnlyDictionary<string, string>>
                {
                    ["10000089"] = new Dictionary<string, string>
                    {
                        ["chs"] = "芙宁娜",
                        ["en"] = "Furina"
                    }
                };
            return Task.FromResult(result);
        }
    }
}

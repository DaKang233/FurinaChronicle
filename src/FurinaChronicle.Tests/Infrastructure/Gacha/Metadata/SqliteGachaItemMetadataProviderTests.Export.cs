// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Exporting;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed partial class SqliteGachaItemMetadataProviderTests
{
    [Fact]
    public async Task CachedExportMetadataUsesExistingLocalizationsWithoutRefreshingExpiredContent()
    {
        await using var context = MetadataTestContext.Create();
        await context.Provider.FindByIdAsync(GachaGame.GenshinImpact, "10000089");
        context.Clock.Advance(TimeSpan.FromDays(40));
        context.RemoteSource.Exception = new HttpRequestException("offline");
        var cached = new CachedGachaItemMetadataProvider(context.Database, new GachaMetadataOptions("unused"));
        var byId = await cached.FindByIdAsync(GachaGame.GenshinImpact, " 10000089 ");
        var byName = await cached.FindByNameAsync(GachaGame.GenshinImpact, "芙宁娜");
        Assert.NotNull(byId);
        Assert.Equal("Furina", byId.LocalizedNames!["en"]);
        Assert.Equal("10000089", byName!.ItemId);
        Assert.Equal(1, context.RemoteSource.CallCount);
        Assert.Null(await cached.FindByIdAsync(GachaGame.GenshinImpact, "missing"));
        Assert.Null(await cached.FindByNameAsync(GachaGame.GenshinImpact, "missing"));
        Assert.Equal(1, context.RemoteSource.CallCount);
    }

    [Fact]
    public async Task CachedExportMetadataEmptyCacheAndCancellationNeverCallRemoteSource()
    {
        await using var context = MetadataTestContext.Create();
        var cached = new CachedGachaItemMetadataProvider(context.Database, new GachaMetadataOptions("unused"));
        Assert.Null(await cached.FindByIdAsync(GachaGame.GenshinImpact, "10000089"));
        Assert.Null(await cached.FindByNameAsync(GachaGame.GenshinImpact, "芙宁娜"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cached.FindByIdAsync(GachaGame.GenshinImpact, "10000089", cancellation.Token).AsTask());
        Assert.Equal(0, context.RemoteSource.CallCount);
    }

    [Theory]
    [InlineData("zh-cn", "芙宁娜")]
    [InlineData("en-us", "Furina")]
    [InlineData("zh-tw", "原始名称")]
    public async Task CachedMetadataSupportsOfflineUigfAndTableLanguageSelection(string language, string expectedName)
    {
        await using var context = MetadataTestContext.Create();
        await context.Provider.FindByIdAsync(GachaGame.GenshinImpact, "10000089");
        context.Clock.Advance(TimeSpan.FromDays(40));
        context.RemoteSource.Exception = new HttpRequestException("offline");
        var cached = new CachedGachaItemMetadataProvider(context.Database, new GachaMetadataOptions("unused"));
        var account = ArchiveTestData.Account(
            Guid.NewGuid(), uid: "100000001", region: GameServerRegion.ChinaOfficial);
        var record = new GachaRecord(account.Id, "1", "原始名称", 5, InitialTime)
        {
            ItemId = "10000089", ItemType = "角色", GachaType = "301", UigfGachaType = "301", Count = 1,
        };
        var document = new GachaExportDocument(
            InitialTime, [new(account, 8, [record])]);
        await using var uigf = new MemoryStream();
        await new UigfV42GachaWriter(cached).WriteAsync(
            uigf, document, new() { Language = language, IncludeAccountLanguage = true });
        using var json = JsonDocument.Parse(uigf.ToArray());
        var output = json.RootElement.GetProperty("hk4e")[0];
        Assert.Equal(language, output.GetProperty("lang").GetString());
        Assert.Equal(expectedName, output.GetProperty("list")[0].GetProperty("name").GetString());
        Assert.Equal("10000089", output.GetProperty("list")[0].GetProperty("item_id").GetString());
        await using var csv = new MemoryStream();
        await new GachaTableExportWriter(cached).WriteAsync(
            csv, document, new(GachaTableFormat.Csv, language));
        Assert.Contains(expectedName, Encoding.UTF8.GetString(csv.ToArray()));
        Assert.Equal(1, context.RemoteSource.CallCount);
    }

}

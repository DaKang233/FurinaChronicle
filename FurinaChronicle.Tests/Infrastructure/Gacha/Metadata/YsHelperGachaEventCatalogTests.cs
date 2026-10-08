// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Infrastructure.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class YsHelperGachaEventCatalogTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsIndependentPoolsAndUsesDetectedNames()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHandler(
            _ => JsonResponse(SampleResponse)));
        var catalog = CreateCatalog(directory.Path, client);

        IReadOnlyList<GachaEventPeriod> periods = await catalog.GetAllAsync(
            GachaGame.GenshinImpact);

        Assert.Equal(3, periods.Count);
        GachaEventPeriod[] characters = periods
            .Where(period => period.PoolGroup == GachaPoolGroup.CharacterEvent)
            .ToArray();
        Assert.Equal(2, characters.Length);
        Assert.All(characters, period => Assert.Single(period.Banners));
        Assert.Equal(
            ["煦风欢舞时", "涌浪叙歌"],
            characters.Select(period => period.Banners[0].Name));
        Assert.Equal(
            [301, 400],
            characters.Select(period => period.Banners[0].GachaType));
        Assert.All(characters, period =>
            Assert.Equal("api.yshelper.com", period.Source));
        Assert.Equal(
            4,
            characters[0].Banners[0].FeaturedItems.Count);
        Assert.True(File.Exists(Path.Combine(directory.Path, "events.json")));
    }

    [Fact]
    public async Task GetAllAsync_UsesStaleCacheWhenRefreshFails()
    {
        using var directory = new TemporaryDirectory();
        string cachePath = Path.Combine(directory.Path, "events.json");
        await File.WriteAllTextAsync(cachePath, SampleResponse);
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddDays(-5));
        using var client = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var catalog = CreateCatalog(directory.Path, client);

        IReadOnlyList<GachaEventPeriod> periods = await catalog.GetAllAsync(
            GachaGame.GenshinImpact);

        Assert.Equal(3, periods.Count);
        Assert.All(periods, period =>
            Assert.Equal("api.yshelper.com", period.Source));
    }

    [Fact]
    public async Task GetAllAsync_FallsBackToEmbeddedCatalogWithoutValidCache()
    {
        using var directory = new TemporaryDirectory();
        using var client = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var catalog = CreateCatalog(directory.Path, client);

        IReadOnlyList<GachaEventPeriod> periods = await catalog.GetAllAsync(
            GachaGame.GenshinImpact);

        Assert.Equal(298, periods.Count);
        Assert.All(periods, period => Assert.Equal("Snap.Metadata", period.Source));
    }

    private static YsHelperGachaEventCatalog CreateCatalog(
        string directory,
        HttpClient client) => new(
            new EmbeddedGachaEventCatalog(),
            new StubMetadataProvider(),
            new YsHelperGachaEventCatalogOptions(
                Path.Combine(directory, "events.json"),
                TimeSpan.FromDays(1),
                endpoint: new Uri("https://example.test/events")),
            TimeProvider.System,
            client);

    private static HttpResponseMessage JsonResponse(string json) => new(
        HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string SampleResponse = """
        {
          "code": 200,
          "result": [
            {
              "avatar": "https://example.test/vanessa-banner.png",
              "version": "7.1上半",
              "star5_role": ["沃雅妮莎"],
              "star4_role": ["迪奥娜", "珐露珊", "重云"],
              "time": "2026/09/23 - 2026/10/13"
            },
            {
              "avatar": "https://example.test/vysnia-banner.png",
              "version": "7.1上半",
              "star5_role": ["薇斯纳"],
              "star4_role": ["迪奥娜", "珐露珊", "重云"],
              "time": "2026/09/23 - 2026/10/13"
            }
          ],
          "weapon": [
            {
              "avatar": "https://example.test/weapon-banner.png",
              "version": "7.1上半",
              "star5_role": ["测试武器"],
              "star4_role": ["测试四星武器"],
              "time": "2026/09/23 - 2026/10/13"
            }
          ],
          "avatar_list": {
            "沃雅妮莎": "https://example.test/vanessa.png",
            "薇斯纳": "https://example.test/vysnia.png",
            "迪奥娜": "https://example.test/diona.png",
            "珐露珊": "https://example.test/faruzan.png",
            "重云": "https://example.test/chongyun.png"
          }
        }
        """;

    private sealed class StubMetadataProvider : IGachaItemMetadataProvider
    {
        private static readonly IReadOnlyDictionary<string, GachaItemMetadata> Items =
            new Dictionary<string, GachaItemMetadata>(StringComparer.Ordinal)
            {
                ["沃雅妮莎"] = Item("10000143", "沃雅妮莎", 5),
                ["薇斯纳"] = Item("10000140", "薇斯纳", 5),
                ["迪奥娜"] = Item("10000039", "迪奥娜", 4),
                ["珐露珊"] = Item("10000076", "珐露珊", 4),
                ["重云"] = Item("10000036", "重云", 4),
                ["测试武器"] = Item("weapon-5", "测试武器", 5),
                ["测试四星武器"] = Item("weapon-4", "测试四星武器", 4)
            };

        public ValueTask<GachaItemMetadata?> FindByIdAsync(
            GachaGame game,
            string itemId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<GachaItemMetadata?>(
                Items.Values.FirstOrDefault(item => item.ItemId == itemId));

        public ValueTask<GachaItemMetadata?> FindByNameAsync(
            GachaGame game,
            string itemName,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                Items.GetValueOrDefault(itemName));

        private static GachaItemMetadata Item(
            string id,
            string name,
            int rank) => new(
                GachaGame.GenshinImpact,
                id,
                name,
                rank == 5 ? "角色" : "角色",
                rank,
                $"https://example.test/{id}.png");
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "FurinaChronicle.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}

using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Remote;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Metadata;

public sealed class GenshinCalculatorMetadataSourceTests
{
    [Fact]
    public async Task FetchAsync_PostsExpectedPayloadsAndMapsAvatarAndWeapon()
    {
        var handler = new CalculatorApiHandler();
        using var httpClient = new HttpClient(handler);
        using var source = new GenshinCalculatorMetadataSource(
            httpClient,
            retryDelays: []);

        IReadOnlyList<GachaMetadataSourceItem> result =
            await source.FetchAsync(GachaGame.GenshinImpact);

        Assert.Equal(2, result.Count);
        GachaMetadataSourceItem avatar =
            Assert.Single(result, item => item.ItemId == "10000089");
        Assert.Equal("Avatar", avatar.ItemType);
        Assert.Equal(5, avatar.RankType);
        Assert.Equal("Furina", avatar.SourceName);

        GachaMetadataSourceItem weapon =
            Assert.Single(result, item => item.ItemId == "11401");
        Assert.Equal("Weapon", weapon.ItemType);
        Assert.Equal(4, weapon.RankType);

        Assert.Equal(2, handler.Requests.Count);
        CapturedRequest avatarRequest = handler.Requests[0];
        Assert.Equal(
            GenshinCalculatorMetadataSource.AvatarListUrl,
            avatarRequest.Url);
        using (JsonDocument avatarBody = JsonDocument.Parse(avatarRequest.Body))
        {
            Assert.True(avatarBody.RootElement.GetProperty("is_all").GetBoolean());
            Assert.Equal(1000, avatarBody.RootElement.GetProperty("size").GetInt32());
        }

        CapturedRequest weaponRequest = handler.Requests[1];
        Assert.Equal(
            GenshinCalculatorMetadataSource.WeaponListUrl,
            weaponRequest.Url);
        using JsonDocument weaponBody = JsonDocument.Parse(weaponRequest.Body);
        Assert.Equal(
            [1, 2, 3, 4, 5],
            weaponBody.RootElement
                .GetProperty("weapon_levels")
                .EnumerateArray()
                .Select(value => value.GetInt32())
                .ToArray());
    }

    private sealed class CalculatorApiHandler : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(
                request.RequestUri!.AbsoluteUri,
                body));

            string responseBody = request.RequestUri.AbsoluteUri ==
                GenshinCalculatorMetadataSource.AvatarListUrl
                ? """
                  {"data":{"list":[{
                    "id":10000089,
                    "name":"Furina",
                    "icon":"https://example.test/furina.png",
                    "avatar_level":"5"
                  }]}}
                  """
                : """
                  {"data":{"list":[{
                    "id":"11401",
                    "name":"Favonius Sword",
                    "icon":"https://example.test/sword.png",
                    "weapon_level":4
                  }]}}
                  """;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseBody,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed record CapturedRequest(string Url, string Body);
}

using System.Collections.Concurrent;
using System.Net;
using System.Text;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Passport;

public sealed class MiHoYoAccountProfileClientTests
{
    [Fact]
    public async Task GetAsync_ParsesProfileAndGameRoles()
    {
        var requests = new ConcurrentBag<HttpRequestMessage>();
        var handler = new StubHttpHandler(request =>
        {
            requests.Add(request);
            string json = request.RequestUri!.AbsoluteUri.StartsWith(
                MiHoYoAccountProfileClient.UserProfileUrl,
                StringComparison.Ordinal)
                ? """
                  {
                    "retcode": 0,
                    "message": "OK",
                    "data": {
                      "user_info": {
                        "nickname": "芙宁娜",
                        "avatar_url": "https://example.test/avatar.png"
                      }
                    }
                  }
                  """
                : """
                  {
                    "retcode": 0,
                    "message": "OK",
                    "data": {
                      "list": [{
                        "game_biz": "hk4e_cn",
                        "region": "cn_gf01",
                        "game_uid": "100000001",
                        "nickname": "旅行者",
                        "level": 60,
                        "region_name": "天空岛服"
                      }]
                    }
                  }
                  """;
            return Task.FromResult(JsonResponse(json));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoAccountProfileClient(httpClient);
        PassportAccount account = CreateAccount();

        var profile = await client.GetAsync(account);

        Assert.Equal("芙宁娜", profile.DisplayName);
        Assert.Equal("https://example.test/avatar.png", profile.AvatarUrl!.AbsoluteUri);
        var role = Assert.Single(profile.Roles);
        Assert.Equal("100000001", role.Uid);
        Assert.Equal("旅行者", role.Nickname);
        Assert.Equal(60, role.Level);
        Assert.Equal(2, requests.Count);
        Assert.All(requests, request =>
        {
            string cookie = request.Headers.GetValues("Cookie").Single();
            Assert.Contains("stuid=12345", cookie, StringComparison.Ordinal);
            Assert.Contains("stoken=stoken-value", cookie, StringComparison.Ordinal);
            Assert.Equal(
                "stabledevice",
                request.Headers.GetValues("x-rpc-device_id").Single());
        });
    }

    private static PassportAccount CreateAccount()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new PassportAccount(
            Guid.NewGuid(),
            "12345",
            "mid-value",
            null,
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                "stoken-value",
                "ltoken-value",
                "cookie-token-value",
                now,
                now,
                now,
                now),
            new PassportDeviceIdentity("stable-device", null),
            now,
            now);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responder(request);
        }
    }
}

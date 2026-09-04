using System.Net;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Refreshing;

public sealed class MiHoYoGachaClientsTests
{
    [Fact]
    public async Task STokenProvider_GeneratesAuthKeyWithStableAccountDeviceAndRoleRegion()
    {
        string? requestBody = null;
        string? cookie = null;
        string? deviceId = null;
        var handler = new StubHttpHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            cookie = request.Headers.GetValues("Cookie").Single();
            deviceId = request.Headers.GetValues("x-rpc-device_id").Single();
            return JsonResponse(
                """
                {"retcode":0,"message":"OK","data":{"authkey":"a+b/c"}}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new MiHoYoSTokenGachaUrlProvider(httpClient);
        PassportAccount passport = Passport();
        GameAccount gameAccount = Game();

        Uri result = await provider.CreateAsync(passport, gameAccount);

        Assert.Contains("\"game_uid\":123456789", requestBody);
        Assert.Contains("\"region\":\"cn_gf01\"", requestBody);
        Assert.Equal("stuid=12345;stoken=root-token;mid=mid-1", cookie);
        Assert.Equal(
            passport.Device.DeviceId.Replace("-", string.Empty),
            deviceId);
        Assert.Equal("a+b/c", Query(result)["authkey"]);
        Assert.Equal("cn_gf01", Query(result)["region"]);
    }

    [Fact]
    public async Task STokenProvider_ConsecutiveRequests_AlwaysSendPersistedAccountCookie()
    {
        var cookies = new List<string>();
        int callCount = 0;
        var handler = new StubHttpHandler(request =>
        {
            callCount++;
            cookies.Add(request.Headers.GetValues("Cookie").Single());
            HttpResponseMessage response = JsonResponse(
                """{"retcode":0,"message":"OK","data":{"authkey":"key-CALL"}}"""
                    .Replace("CALL", callCount.ToString(), StringComparison.Ordinal));
            response.Headers.TryAddWithoutValidation(
                "Set-Cookie",
                "stoken=expired; Path=/; Max-Age=0");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new MiHoYoSTokenGachaUrlProvider(httpClient);
        PassportAccount passport = Passport();
        GameAccount gameAccount = Game();

        Uri first = await provider.CreateAsync(passport, gameAccount);
        Uri second = await provider.CreateAsync(passport, gameAccount);

        Assert.Equal(
            new[] { "key-1", "key-2" },
            new[] { Query(first)["authkey"], Query(second)["authkey"] });
        Assert.Equal(2, cookies.Count);
        Assert.All(
            cookies,
            cookie => Assert.Equal(
                "stuid=12345;stoken=root-token;mid=mid-1",
                cookie));
    }

    [Fact]
    public async Task STokenProvider_RejectsOverseaPassportBeforeSendingRequest()
    {
        int requestCount = 0;
        var handler = new StubHttpHandler(request =>
        {
            requestCount++;
            return Task.FromResult(JsonResponse("{}"));
        });
        using var httpClient = new HttpClient(handler);
        using var provider = new MiHoYoSTokenGachaUrlProvider(httpClient);

        NotSupportedException exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => provider.CreateAsync(Passport(PassportRealm.Oversea), Game()));

        Assert.Contains("mainland China passport", exception.Message);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task GachaClient_AddsPagingParametersAndParsesServerLocalTime()
    {
        Uri? requestedUrl = null;
        var handler = new StubHttpHandler(request =>
        {
            requestedUrl = request.RequestUri;
            return Task.FromResult(JsonResponse(
                """
                {
                  "retcode": 0,
                  "message": "OK",
                  "data": {
                    "list": [{
                      "uid":"123456789",
                      "id":"9001",
                      "item_id":"1001",
                      "name":"测试武器",
                      "item_type":"武器",
                      "rank_type":"4",
                      "gacha_type":"400",
                      "time":"2026-08-26 12:30:00",
                      "count":"1"
                    }]
                  }
                }
                """));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoGachaLogClient(httpClient);

        var page = await client.GetPageAsync(
            new Uri(
                "https://webstatic.mihoyo.com/hk4e/event/e20190909gacha/index.html" +
                "?authkey=abc&auth_appid=webview_gacha&lang=zh-cn"),
            "301",
            "8123");

        Assert.NotNull(requestedUrl);
        Assert.Equal(
            "public-operation-hk4e.mihoyo.com",
            requestedUrl.Host);
        IReadOnlyDictionary<string, string> query = Query(requestedUrl);
        Assert.Equal("301", query["gacha_type"]);
        Assert.Equal("8123", query["end_id"]);
        Assert.Equal("20", query["size"]);
        var record = Assert.Single(page.Records);
        Assert.Equal("9001", record.ExternalRecordId);
        Assert.Equal(TimeSpan.FromHours(8), record.Time.Offset);
        Assert.Equal("400", record.GachaType);
    }

    [Fact]
    public async Task GachaClient_RetCodeMinus110_RetriesAfterBackoff()
    {
        int callCount = 0;
        var handler = new StubHttpHandler(request =>
        {
            callCount++;
            return Task.FromResult(JsonResponse(callCount == 1
                ? """{"retcode":-110,"message":"visit too frequently","data":null}"""
                : """{"retcode":0,"message":"OK","data":{"list":[]}}"""));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoGachaLogClient(
            httpClient,
            minimumRequestInterval: TimeSpan.Zero,
            rateLimitRetryDelays: [TimeSpan.Zero]);

        GachaRemotePage page = await client.GetPageAsync(
            new Uri(
                "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
                "?authkey=abc&auth_appid=webview_gacha&lang=zh-cn"),
            "301",
            endId: null);

        Assert.Empty(page.Records);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task WindowsCacheProvider_FindsNewestVersionedCacheUrl()
    {
        var provider = new WindowsGachaCacheUrlProvider();
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(provider.IsSupported);
            return;
        }

        string directory = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleTests",
            Guid.NewGuid().ToString("N"));
        string cacheDirectory = Path.Combine(
            directory,
            "YuanShen_Data",
            "webCaches",
            "1.2.3.4",
            "Cache",
            "Cache_Data");
        Directory.CreateDirectory(cacheDirectory);
        string cacheFile = Path.Combine(cacheDirectory, "data_2");
        await File.WriteAllBytesAsync(
            cacheFile,
            Encoding.UTF8.GetBytes(
                "old\0https://webstatic.mihoyo.com/hk4e/event/e20190909gacha/index.html" +
                "?authkey=cache-key&auth_appid=webview_gacha&lang=zh-cn#/log\0"));

        try
        {
            Uri result = await provider.FindAsync(
                directory,
                GameServerRegion.ChinaOfficial);

            Assert.Equal("cache-key", Query(result)["authkey"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PassportAccount Passport(
        PassportRealm realm = PassportRealm.MainlandChina)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new PassportAccount(
            Guid.NewGuid(),
            "12345",
            "mid-1",
            null,
            PassportLoginMethod.MobileCaptcha,
            new PassportCredentials(
                "root-token",
                null,
                null,
                now,
                null,
                null,
                now),
            new PassportDeviceIdentity(
                "31bf26d5-2afe-4b30-aab4-42fcdb3d4b09",
                "fp"),
            now,
            now,
            realm);
    }

    private static GameAccount Game()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new GameAccount(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "123456789",
            GameServerRegion.ChinaOfficial,
            null,
            false,
            now,
            now);
    }

    private static IReadOnlyDictionary<string, string> Query(Uri uri)
    {
        return FurinaChronicle.Services.Gacha.Refreshing.GachaRefreshUrl
            .ParseQuery(uri.Query);
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

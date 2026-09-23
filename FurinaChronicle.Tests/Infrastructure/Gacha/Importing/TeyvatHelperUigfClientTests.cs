using System.Net;
using System.Text;
using FurinaChronicle.Infrastructure.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Tests.Infrastructure.Gacha.Importing;

public sealed class TeyvatHelperUigfClientTests
{
    [Fact]
    public async Task DownloadAsync_PostsExpectedFormAndReturnsLegacyUigf()
    {
        Uri? requestUri = null;
        IReadOnlyDictionary<string, string>? form = null;
        var handler = new StubHttpHandler(async request =>
        {
            requestUri = request.RequestUri;
            form = GachaRefreshUrl.ParseQuery(
                await request.Content!.ReadAsStringAsync());
            return Response(
                "<br>\n" +
                """
                {"info":{"uid":"123456789","uigf_version":"v2.2"},"list":[]}
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new TeyvatHelperUigfClient(httpClient);
        var gachaUrl = new Uri(
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
            "?authkey=a%2Bb%2Fc&region=cn_gf01");

        TeyvatHelperUigfDownload result = await client.DownloadAsync(
            "123456789",
            gachaUrl);

        Assert.Equal(TeyvatHelperUigfClient.ExportUrl, requestUri?.AbsoluteUri);
        Assert.NotNull(form);
        Assert.Equal("123456789", form["uid"]);
        Assert.Equal(gachaUrl.AbsoluteUri, form["gachaurl"]);
        Assert.Equal("zh-Hans", form["lang"]);
        Assert.Equal("TeyvatHelper-UIGF-123456789.json", result.FileName);
        Assert.StartsWith("{", Encoding.UTF8.GetString(result.Content));
        Assert.Contains("\"uigf_version\":\"v2.2\"", Encoding.UTF8.GetString(result.Content));
    }

    [Fact]
    public async Task DownloadAsync_RecognizesBusinessErrorFromHttp200()
    {
        var handler = new StubHttpHandler(_ => Task.FromResult(Response(
            "<br>{\"code\":300,\"result\":\"抽卡分析地址格式错误1\"}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TeyvatHelperUigfClient(httpClient);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadAsync("123456789", GachaUrl("secret")));

        Assert.Contains("300", exception.Message);
        Assert.Contains("抽卡分析地址格式错误1", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_DoesNotEchoAuthKeyFromRemoteError()
    {
        const string secret = "sensitive-authkey-value";
        var handler = new StubHttpHandler(_ => Task.FromResult(Response(
            "{\"code\":300,\"result\":\"bad https://example.test?authkey=" +
            secret + "\"}")));
        using var httpClient = new HttpClient(handler);
        using var client = new TeyvatHelperUigfClient(httpClient);

        InvalidOperationException exception =
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.DownloadAsync("123456789", GachaUrl(secret)));

        Assert.DoesNotContain(secret, exception.Message);
        Assert.DoesNotContain("authkey", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadAsync_RejectsNonUigfResponse()
    {
        var handler = new StubHttpHandler(_ => Task.FromResult(Response(
            "<html><body>server error</body></html>",
            "text/html")));
        using var httpClient = new HttpClient(handler);
        using var client = new TeyvatHelperUigfClient(httpClient);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadAsync("123456789", GachaUrl("secret")));

        Assert.Contains("UIGF", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_RejectsOversizedResponseFromHeader()
    {
        var handler = new StubHttpHandler(_ =>
        {
            HttpResponseMessage response = Response("{}");
            response.Content.Headers.ContentLength = 32L * 1024 * 1024 + 1;
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new TeyvatHelperUigfClient(httpClient);

        InvalidDataException exception =
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                client.DownloadAsync("123456789", GachaUrl("secret")));

        Assert.Contains("过大", exception.Message);
    }

    private static Uri GachaUrl(string authKey)
    {
        return new Uri(
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
            $"?authkey={Uri.EscapeDataString(authKey)}&region=cn_gf01");
    }

    private static HttpResponseMessage Response(
        string content,
        string mediaType = "application/json")
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, mediaType)
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

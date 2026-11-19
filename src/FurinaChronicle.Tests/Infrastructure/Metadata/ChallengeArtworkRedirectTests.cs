// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Infrastructure.Metadata;

namespace FurinaChronicle.Tests.Infrastructure.Metadata;

public sealed class ChallengeArtworkRedirectTests
{
    private const string Api = "https://api.snaphutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png";
    private const string Static = "https://static.snaphutaorp.org/static/raw/MonsterIcon/UI_MonsterIcon_Slime_Grass_02.png";

    [Fact]
    public async Task PublicApiRedirectReachesTheVerifiedStaticAssetWithoutForwardingSensitiveHeaders()
    {
        var seen = new List<Uri>();
        using var client = new HttpClient(new ChallengeArtworkRedirectHandler(new Handler(request =>
        {
            seen.Add(request.RequestUri!);
            Assert.Null(request.Headers.Authorization);
            Assert.False(request.Headers.Contains("Cookie"));
            Assert.False(request.Headers.Contains("x-rpc-device_id"));
            return seen.Count == 1 ? Redirect(Static) : new(HttpStatusCode.OK) { Content = new StringContent("public image") };
        })));
        using var request = new HttpRequestMessage(HttpMethod.Get, Api);
        request.Headers.TryAddWithoutValidation("Cookie", "synthetic-not-a-cookie");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer synthetic");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", "synthetic-device");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { new Uri(Api), new Uri(Static) }, seen);
    }

    [Theory]
    [InlineData("https://example.test/private.png")]
    [InlineData("http://static.snaphutaorp.org/static/raw/MonsterIcon/icon.png")]
    [InlineData("https://static.snaphutaorp.org/static/raw/MonsterIcon/icon.png?token=synthetic")]
    [InlineData("https://static.snaphutaorp.org/private.png")]
    public async Task RedirectCannotEscapeThePublicAssetPolicy(string location)
    {
        int calls = 0;
        using var client = new HttpClient(new ChallengeArtworkRedirectHandler(new Handler(_ =>
        {
            calls++;
            return Redirect(location);
        })));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Api));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RedirectLoopIsBounded()
    {
        int calls = 0;
        using var client = new HttpClient(new ChallengeArtworkRedirectHandler(new Handler(_ =>
        {
            calls++;
            return Redirect(Api);
        })));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(Api));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task RelativeRedirectUsesTheSameNarrowAssetRoute()
    {
        int calls = 0;
        using var client = new HttpClient(new ChallengeArtworkRedirectHandler(new Handler(request =>
        {
            calls++;
            if (calls == 1) return Redirect("/static/raw/MonsterIcon/other.png", UriKind.Relative);
            Assert.Equal("https://api.snaphutaorp.org/static/raw/MonsterIcon/other.png", request.RequestUri!.AbsoluteUri);
            return new(HttpStatusCode.OK);
        })));
        using var response = await client.GetAsync(Api);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, calls);
    }

    private static HttpResponseMessage Redirect(string location, UriKind kind = UriKind.Absolute)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Redirect);
        response.Headers.Location = new(location, kind);
        return response;
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(send(request));
    }
}

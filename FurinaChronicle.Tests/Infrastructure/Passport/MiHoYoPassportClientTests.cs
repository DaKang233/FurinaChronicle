using System.Net;
using System.Text;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.Passport;

public sealed class MiHoYoPassportClientTests
{
    [Fact]
    public async Task CreateAndPollQrSession_ParsesConfirmedSToken()
    {
        int call = 0;
        var handler = new StubHttpHandler(request =>
        {
            call++;
            Assert.Equal("stable-device", request.Headers.GetValues("x-rpc-device_id").Single());
            return Task.FromResult(call == 1
                ? JsonResponse(
                    """
                    {"retcode":0,"message":"OK","data":{"ticket":"ticket-1","url":"https://example.test/qr"}}
                    """)
                : JsonResponse(
                    """
                    {
                      "retcode":0,
                      "message":"OK",
                      "data":{
                        "status":"Confirmed",
                        "user_info":{"aid":"12345","mid":"mid-1","account_name":"Furina"},
                        "tokens":[{"token_type":1,"token":"root-token"}]
                      }
                    }
                    """));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoPassportClient(httpClient);
        var device = new PassportDeviceIdentity("stable-device", null);

        PassportQrSession session = await client.CreateQrSessionAsync(device);
        PassportQrPollResult result = await client.PollQrSessionAsync(session);

        Assert.Equal(PassportQrStatus.Confirmed, result.Status);
        Assert.Equal("root-token", result.Tokens!.SToken);
        Assert.Equal("12345", result.Tokens.Aid);
        Assert.Equal("mid-1", result.Tokens.Mid);
    }

    [Fact]
    public async Task SendMobileCaptcha_ReturnsAigisAndPreservesDevice()
    {
        var handler = new StubHttpHandler(async request =>
        {
            string body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("13800138000", body);
            Assert.True(request.Headers.Contains("DS"));
            var response = JsonResponse(
                """
                {"retcode":0,"message":"OK","data":{"action_type":"login","sent_new":true,"countdown":60}}
                """);
            response.Headers.TryAddWithoutValidation("X-Rpc-Aigis", "risk-session");
            return response;
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoPassportClient(httpClient);
        var device = new PassportDeviceIdentity("stable-device", null);

        MobileCaptchaChallenge challenge = await client.SendMobileCaptchaAsync(
            "13800138000",
            device);

        Assert.Equal("login", challenge.ActionType);
        Assert.Equal("risk-session", challenge.Aigis);
        Assert.Equal("stable-device", challenge.DeviceId);
    }

    [Fact]
    public async Task CompleteWebLogin_ExchangesLoginTicketAndUpgradesSToken()
    {
        int callCount = 0;
        var handler = new StubHttpHandler(request =>
        {
            callCount++;
            if (request.RequestUri!.AbsoluteUri.StartsWith(
                MiHoYoPassportClient.GetMultiTokenByLoginTicketUrl,
                StringComparison.Ordinal))
            {
                Assert.Contains("login_ticket=ticket-1", request.RequestUri.Query);
                return Task.FromResult(JsonResponse(
                    """
                    {
                      "retcode":0,
                      "message":"OK",
                      "data":{"list":[
                        {"name":"stoken","token":"legacy-stoken"},
                        {"name":"ltoken","token":"ticket-ltoken"}
                      ]}
                    }
                    """));
            }

            Assert.Equal(
                MiHoYoPassportClient.UpgradeLegacySTokenUrl,
                request.RequestUri.AbsoluteUri);
            Assert.Contains(
                "stoken=legacy-stoken",
                request.Headers.GetValues("Cookie").Single(),
                StringComparison.Ordinal);
            return Task.FromResult(JsonResponse(
                """
                {
                  "retcode":0,
                  "message":"OK",
                  "data":{
                    "token":{"token_type":1,"token":"root-stoken"},
                    "user_info":{
                      "aid":"12345",
                      "mid":"mid-1",
                      "account_name":"Furina"
                    }
                  }
                }
                """));
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoPassportClient(httpClient);

        PassportLoginTokens tokens = await client.CompleteWebLoginAsync(
            "account_id=12345; cookie_token=cookie-token",
            """
            {
              "retcode":0,
              "message":"OK",
              "data":{
                "token":{"token_type":4,"token":"cookie-token"},
                "user_info":{"aid":"12345","mid":"mid-1"},
                "login_ticket":"ticket-1"
              }
            }
            """,
            new PassportDeviceIdentity("stable-device", null));

        Assert.Equal("12345", tokens.Aid);
        Assert.Equal("mid-1", tokens.Mid);
        Assert.Equal("root-stoken", tokens.SToken);
        Assert.Equal("ticket-ltoken", tokens.LToken);
        Assert.Equal("cookie-token", tokens.CookieToken);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task OverseaPasswordLogin_EncryptsCredentialsAndParsesTokens()
    {
        var handler = new StubHttpHandler(async request =>
        {
            Assert.Equal(
                MiHoYoPassportClient.OverseaPasswordLoginUrl,
                request.RequestUri!.AbsoluteUri);
            string body = await request.Content!.ReadAsStringAsync();
            Assert.DoesNotContain("traveler@example.com", body, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-password", body, StringComparison.Ordinal);
            Assert.Equal(
                "ddxf6vlr1reo",
                request.Headers.GetValues("x-rpc-app_id").Single());
            Assert.Equal(
                "oversea-device",
                request.Headers.GetValues("x-rpc-device_id").Single());
            return JsonResponse(
                """
                {
                  "retcode":0,
                  "message":"OK",
                  "data":{
                    "token":{"token_type":2,"token":"stoken-os"},
                    "user_info":{
                      "aid":"900001",
                      "mid":"mid-os",
                      "account_name":"Traveler"
                    }
                  }
                }
                """);
        });
        using var httpClient = new HttpClient(handler);
        using var client = new MiHoYoPassportClient(httpClient);

        PassportLoginTokens tokens = await client.LoginWithOverseaPasswordAsync(
            "traveler@example.com",
            "secret-password",
            new PassportDeviceIdentity("oversea-device", null));

        Assert.Equal("900001", tokens.Aid);
        Assert.Equal("mid-os", tokens.Mid);
        Assert.Equal("stoken-os", tokens.SToken);
        Assert.Equal("Traveler", tokens.DisplayName);
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

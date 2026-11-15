// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class HoYoGameRecordSpiralAbyssSourceTests
{
    private static readonly GameRoleNaturalIdentity Role =
        new("hk4e_cn", "cn_gf01", "100000001");

    [Theory]
    [InlineData(0, SpiralAbyssCollectionStatus.Empty)]
    [InlineData(-100, SpiralAbyssCollectionStatus.AuthenticationRequired)]
    [InlineData(1034, SpiralAbyssCollectionStatus.VerificationRequired)]
    [InlineData(10101, SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(-110, SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(-1, SpiralAbyssCollectionStatus.Empty)]
    [InlineData(10102, SpiralAbyssCollectionStatus.AccessDenied)]
    [InlineData(5003, SpiralAbyssCollectionStatus.AccessDenied)]
    public async Task FetchUsesCurrentScheduleAndMapsBusinessResult(
        int retcode, SpiralAbyssCollectionStatus expected)
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("api-takumi-record.mihoyo.com", request.RequestUri!.Host);
            Assert.Equal("?role_id=100000001&schedule_type=1&server=cn_gf01", request.RequestUri.Query);
            Assert.Equal("ltuid=1111; ltoken=fixture", Assert.Single(request.Headers.GetValues("Cookie")));
            string[] signature = Assert.Single(request.Headers.GetValues("DS")).Split(',');
            Assert.Equal(3, signature.Length);
            Assert.Matches("^[0-9]{6}$", signature[1]);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$$"""{"retcode":{{{retcode}}},"data":{"schedule_id":1,"floors":[]}}"""),
            };
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchAsync(Passport("fixture"), Role, SpiralAbyssPeriod.Current, default);

        Assert.Equal(expected, result.Status);
        Assert.Equal(retcode == 0 ? null : retcode, result.ProviderCode);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fixture; other=fixture")]
    public async Task RejectsMissingOrInvalidLTokenBeforeSending(string? lToken)
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new InvalidOperationException("Unexpected network request.")));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        Assert.Equal(SpiralAbyssCollectionStatus.AuthenticationRequired,
            (await source.FetchAsync(Passport(lToken), Role, SpiralAbyssPeriod.Current, default)).Status);
    }

    [Fact]
    public async Task PreviousPeriodUsesScheduleTypeTwo()
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal("?role_id=100000001&schedule_type=2&server=cn_gf01", request.RequestUri!.Query);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"retcode":0,"data":{"schedule_id":7,"floors":[]}}"""),
            };
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchAsync(Passport("fixture"), Role, SpiralAbyssPeriod.Previous, default);

        Assert.Equal(SpiralAbyssCollectionStatus.Empty, result.Status);
    }

    [Theory]
    [InlineData(false, 0, false, SpiralAbyssCollectionStatus.Received, 5)]
    [InlineData(true, 0, false, SpiralAbyssCollectionStatus.VerificationCanceled, 3)]
    [InlineData(false, 1, false, SpiralAbyssCollectionStatus.VerificationFailed, 4)]
    [InlineData(false, 0, true, SpiralAbyssCollectionStatus.AccessDenied, 5)]
    [InlineData(false, 10306, false, SpiralAbyssCollectionStatus.Received, 5)]
    [InlineData(false, 10306, true, SpiralAbyssCollectionStatus.VerificationFailed, 5)]
    public async Task RiskResponseRequiresHumanResultAndRetriesAtMostOnce(
        bool cancel, int rejectionCode, bool riskAfterRetry,
        SpiralAbyssCollectionStatus expected, int expectedRequests)
    {
        PassportAccount passport = Passport("fixture");
        bool registrationSetsCookie = rejectionCode != 1;
        const string passportCookie = "ltuid=1111; ltoken=fixture";
        int requests = 0;
        int prompts = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            const string abyss = "/game_record/app/genshin/api/spiralAbyss";
            const string index = "/game_record/app/genshin/api/index";
            Assert.Equal("api-takumi-record.mihoyo.com", request.RequestUri!.Host);
            Assert.Equal("application/json", Assert.Single(request.Headers.Accept).MediaType);
            Assert.Equal(passport.Device.DeviceId, Assert.Single(request.Headers.GetValues("x-rpc-device_id")));
            string cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            if (requests != 4)
            {
                Assert.Equal(passportCookie, cookie);
            }
            switch (requests)
            {
                case 1:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal(index, request.RequestUri.AbsolutePath);
                    Assert.Equal("?role_id=100000001&server=cn_gf01", request.RequestUri.Query);
                    Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    return Json("""{"retcode":0,"data":{"role":{}}}""");
                case 2:
                    Assert.Equal(abyss, request.RequestUri.AbsolutePath);
                    Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    return Json("""{"retcode":1034}""");
                case 3:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.EndsWith("/createVerification", request.RequestUri.AbsolutePath);
                    Assert.Equal("?is_high=true", request.RequestUri.Query);
                    Assert.Equal("https://api-takumi-record.mihoyo.com" + abyss, Assert.Single(request.Headers.GetValues("x-rpc-challenge_path")));
                    var registration = Json("""{"retcode":0,"data":{"gt":"fixture-gt","challenge":"fixture-initial"}}""");
                    if (registrationSetsCookie)
                    {
                        registration.Headers.TryAddWithoutValidation("Set-Cookie", new[]
                        {
                            "challenge_session=fixture-session; Secure; HttpOnly; Path=/game_record/app/card/wapi",
                            "unrelated=fixture-other-path; Secure; Path=/somewhere-else",
                            "ltoken=fixture-other-credential; Secure; Path=/game_record/app/card/wapi",
                        });
                    }
                    return registration;
                case 4:
                    Assert.Equal(HttpMethod.Post, request.Method);
                    // A credential that the registration response rotated wins
                    // over the stored value, exactly like a shared cookie jar.
                    Assert.Equal(registrationSetsCookie
                        ? "ltuid=1111; ltoken=fixture-other-credential; challenge_session=fixture-session"
                        : passportCookie, cookie);
                    Assert.EndsWith("/verifyVerification", request.RequestUri.AbsolutePath);
                    Assert.Equal("https://api-takumi-record.mihoyo.com" + abyss, Assert.Single(request.Headers.GetValues("x-rpc-challenge_path")));
                    string body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                    Assert.Contains("fixture-human-answer", body);
                    Assert.Contains("fixture-sdk-seccode", body);
                    Assert.DoesNotContain("fixture-validation|jordan", body);
                    return rejectionCode != 0
                        ? Json($$"""{"retcode":{{rejectionCode}}}""")
                        : Json("""{"retcode":0,"data":{"challenge":"fixture-proof"}}""");
                case 5:
                    Assert.Equal(abyss, request.RequestUri.AbsolutePath);
                    if (rejectionCode == 10306)
                    {
                        Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    }
                    else
                    {
                        Assert.Equal("fixture-proof", Assert.Single(request.Headers.GetValues("x-rpc-challenge")));
                    }
                    return riskAfterRetry
                        ? Json("""{"retcode":5003}""")
                        : Json("""{"retcode":0,"data":{"schedule_id":7,"floors":[{"index":12,"levels":[{"index":3,"star":3}]}]}}""");
                case 6:
                    Assert.Equal(index, request.RequestUri.AbsolutePath);
                    return Json("""{"retcode":0,"data":{"role":{}}}""");
                case 7:
                    Assert.Equal(abyss, request.RequestUri.AbsolutePath);
                    Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    return Json("""{"retcode":0,"data":{"schedule_id":7,"floors":[]}}""");
                default:
                    throw new InvalidOperationException("Unexpected verification request.");
            }
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);
        var result = await source.FetchAsync(
            passport, Role, SpiralAbyssPeriod.Previous, default, (challenge, _) =>
            {
                prompts++;
                Assert.False(challenge.IsOversea);
                Assert.Equal("fixture-gt", challenge.Gt);
                Assert.Equal("fixture-initial", challenge.Challenge);
                return Task.FromResult<PassportGeetestResult?>(cancel
                    ? null : new("fixture-human-answer", "fixture-validation", "fixture-sdk-seccode"));
            });

        Assert.Equal(expected, result.Status);
        Assert.Equal(expectedRequests, requests);
        if (result.Status == SpiralAbyssCollectionStatus.VerificationFailed)
        {
            Assert.Equal(GameRecordVerificationStage.Submission, result.VerificationStage);
            Assert.Equal(rejectionCode, result.ProviderCode);
            Assert.Equal(registrationSetsCookie, result.VerificationSessionCookieSent);
        }
        else if (!cancel && rejectionCode == 0)
        {
            Assert.Equal(GameRecordVerificationStage.Retry, result.VerificationStage);
            Assert.Equal(riskAfterRetry ? 5003 : null, result.ProviderCode);
        }
        Assert.Equal(1, prompts);

        if (result.Status == SpiralAbyssCollectionStatus.Received)
        {
            var next = await source.FetchAsync(passport, Role,
                SpiralAbyssPeriod.Previous, default,
                (_, _) => throw new InvalidOperationException("The server accepted the normal refresh."));
            Assert.Equal(SpiralAbyssCollectionStatus.Empty, next.Status);
            Assert.Equal(7, requests);
        }
    }

    [Fact]
    public async Task AccountRiskVerifiesOnIndexThenReadsAbyss()
    {
        const string abyss = "/game_record/app/genshin/api/spiralAbyss";
        const string index = "/game_record/app/genshin/api/index";
        const string indexChallengePath = "https://api-takumi-record.mihoyo.com" + index;
        int requests = 0;
        int prompts = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            switch (requests)
            {
                case 1:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.Equal(index, request.RequestUri!.AbsolutePath);
                    Assert.Equal("?role_id=100000001&server=cn_gf01", request.RequestUri.Query);
                    Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    return Json("""{"retcode":1034}""");
                case 2:
                    Assert.Equal(HttpMethod.Get, request.Method);
                    Assert.EndsWith("/createVerification", request.RequestUri!.AbsolutePath);
                    Assert.Equal("?is_high=true", request.RequestUri.Query);
                    Assert.Equal(indexChallengePath,
                        Assert.Single(request.Headers.GetValues("x-rpc-challenge_path")));
                    Assert.Equal("2", Assert.Single(request.Headers.GetValues("x-rpc-challenge_game")));
                    return Json("""{"retcode":0,"data":{"gt":"fixture-gt","challenge":"fixture-initial"}}""");
                case 3:
                    Assert.Equal(HttpMethod.Post, request.Method);
                    Assert.EndsWith("/verifyVerification", request.RequestUri!.AbsolutePath);
                    Assert.Equal(indexChallengePath,
                        Assert.Single(request.Headers.GetValues("x-rpc-challenge_path")));
                    Assert.Contains("fixture-human-answer",
                        request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                    return Json("""{"retcode":0,"data":{"challenge":"fixture-proof"}}""");
                case 4:
                    Assert.Equal(index, request.RequestUri!.AbsolutePath);
                    Assert.Equal("fixture-proof",
                        Assert.Single(request.Headers.GetValues("x-rpc-challenge")));
                    return Json("""{"retcode":0,"data":{"role":{}}}""");
                case 5:
                    Assert.Equal(abyss, request.RequestUri!.AbsolutePath);
                    Assert.False(request.Headers.Contains("x-rpc-challenge"));
                    return Json("""{"retcode":0,"data":{"schedule_id":124,"floors":[{"index":12,"levels":[{"index":3,"star":3}]}]}}""");
                default:
                    throw new InvalidOperationException("Unexpected request.");
            }
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        SpiralAbyssSourceResult result = await source.FetchAsync(
            Passport("fixture"), Role, SpiralAbyssPeriod.Current, default, (challenge, _) =>
            {
                prompts++;
                Assert.False(challenge.IsOversea);
                Assert.Equal("fixture-gt", challenge.Gt);
                Assert.Equal("fixture-initial", challenge.Challenge);
                return Task.FromResult<PassportGeetestResult?>(
                    new("fixture-human-answer", "fixture-validation", "fixture-sdk-seccode"));
            });

        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(GameRecordVerificationStage.Retry, result.VerificationStage);
        Assert.Equal("124", result.Snapshot?.SeasonId);
        Assert.Equal(1, prompts);
        Assert.Equal(5, requests);
    }

    [Fact]
    public async Task AccountRiskWithoutIndexCaptchaReturnsAccessDenied()
    {
        int requests = 0;
        int prompts = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            return request.RequestUri!.AbsolutePath.EndsWith("/spiralAbyss", StringComparison.Ordinal)
                ? Json("""{"retcode":5003}""")
                : Json("""{"retcode":0,"data":{"role":{}}}""");
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        SpiralAbyssSourceResult result = await source.FetchAsync(
            Passport("fixture"), Role, SpiralAbyssPeriod.Current, default, (_, _) =>
            {
                prompts++;
                return Task.FromResult<PassportGeetestResult?>(null);
            });

        Assert.Equal(SpiralAbyssCollectionStatus.AccessDenied, result.Status);
        Assert.Equal(5003, result.ProviderCode);
        Assert.Null(result.VerificationStage);
        Assert.Equal(0, prompts);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task AccountRiskVerificationCanceledOnIndexChallenge()
    {
        int requests = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            return requests switch
            {
                1 => Json("""{"retcode":1034}"""),
                2 => Json("""{"retcode":0,"data":{"gt":"fixture-gt","challenge":"fixture-initial"}}"""),
                _ => throw new InvalidOperationException("Unexpected request after cancellation."),
            };
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        SpiralAbyssSourceResult result = await source.FetchAsync(
            Passport("fixture"), Role, SpiralAbyssPeriod.Current, default,
            (_, _) => Task.FromResult<PassportGeetestResult?>(null));

        Assert.Equal(SpiralAbyssCollectionStatus.VerificationCanceled, result.Status);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task IndexProfileLargerThanLegacyBufferIsStillParsed()
    {
        // A healthy index answer is a full profile document, well above the
        // legacy 64 KiB verification buffer; it must not surface as Unavailable.
        string filler = new('x', 200_000);
        string indexBody =
            "{\"retcode\":0,\"data\":{\"role\":{\"nickname\":\"" + filler + "\"}}}";
        int requests = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            return requests switch
            {
                1 => Json(indexBody),
                2 => Json(
                    """{"retcode":0,"data":{"schedule_id":7,"floors":[{"index":12,"levels":[{"index":3,"star":3}]}]}}"""),
                _ => throw new InvalidOperationException("Unexpected request."),
            };
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        SpiralAbyssSourceResult result = await source.FetchAsync(
            Passport("fixture"), Role, SpiralAbyssPeriod.Current, default,
            (_, _) => Task.FromResult<PassportGeetestResult?>(null));

        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(0, SpiralAbyssCollectionStatus.Received)]
    [InlineData(-100, SpiralAbyssCollectionStatus.AuthenticationRequired)]
    [InlineData(1034, SpiralAbyssCollectionStatus.VerificationRequired)]
    public async Task OverseasUsesItsOwnHostAndSession(int retcode, SpiralAbyssCollectionStatus expected)
    {
        var role = new GameRoleNaturalIdentity("hk4e_global", "os_asia", "800000001");
        var passport = new PassportAccount(Guid.NewGuid(), "1111", "fixture-mid", null,
            PassportLoginMethod.OverseaPassword,
            new PassportCredentials("fixture-stoken", "fixture-ltoken", "fixture-cookie", null, null, null, null),
            PassportDeviceIdentity.Create(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            PassportRealm.Oversea);
        int requests = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://bbs-api-os.hoyolab.com/game_record/app/genshin/api/spiralAbyss?role_id=800000001&schedule_type=2&server=os_asia",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("ltuid_v2=1111; ltoken_v2=fixture-ltoken; account_id_v2=1111; cookie_token_v2=fixture-cookie; account_mid_v2=fixture-mid",
                Assert.Single(request.Headers.GetValues("Cookie")));
            Assert.False(request.Headers.Contains("x-rpc-device_fp"));
            return Json($$$"""{"retcode":{{{retcode}}},"data":{"uid":"800000001","schedule_id":42,"floors":[{"index":12,"levels":[{"index":3,"star":3}]}]}}""");
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchAsync(passport, role, SpiralAbyssPeriod.Previous, default,
            (_, _) => throw new InvalidOperationException("The overseas request must not prompt for mainland verification."));

        Assert.Equal(expected, result.Status);
        Assert.Equal(retcode == 0 ? "42" : null, result.Snapshot?.SeasonId);
        Assert.Equal(1, requests);
        var mismatch = await source.FetchAsync(passport, Role, SpiralAbyssPeriod.Current, default);
        Assert.Equal(SpiralAbyssCollectionStatus.UnsupportedRealm, mismatch.Status);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData("fixture-device-fp", true)]
    [InlineData(null, false)]
    [InlineData("fixture; invalid", false)]
    public async Task SendsOnlyAValidFingerprintFromTheSelectedSession(string? fingerprint, bool expected)
    {
        var device = new PassportDeviceIdentity("fixture-device", fingerprint);
        var passport = new PassportAccount(Guid.NewGuid(), "1111", null, null, PassportLoginMethod.QrCode,
            new PassportCredentials(null, "fixture-ltoken", null, null, null, null, null),
            device, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(device.DeviceId, Assert.Single(request.Headers.GetValues("x-rpc-device_id")));
            Assert.Equal(expected, request.Headers.TryGetValues("x-rpc-device_fp", out var values));
            if (expected)
            {
                Assert.Equal(fingerprint, Assert.Single(values!));
            }

            return Json("""{"retcode":0,"data":{"schedule_id":42,"floors":[{"index":12,"levels":[{"index":3,"star":3}]}]}}""");
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchAsync(passport, Role, SpiralAbyssPeriod.Current, default);
        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
    }

    [Fact]
    public async Task WafCookiesFromRegistrationAreForwardedButNotReportedAsSession()
    {
        const string passportCookie = "ltuid=1111; ltoken=fixture";
        int requests = 0;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            requests++;
            switch (requests)
            {
                case 1:
                    return Json("""{"retcode":0,"data":{"role":{}}}""");
                case 2:
                    return Json("""{"retcode":1034}""");
                case 3:
                    HttpResponseMessage registration = Json(
                        """{"retcode":0,"data":{"gt":"fixture-gt","challenge":"fixture-initial"}}""");
                    registration.Headers.TryAddWithoutValidation("Set-Cookie", new[]
                    {
                        "acw_tc=fixture-waf; Path=/",
                        "aliyungf_tc=fixture-waf; Path=/",
                    });
                    return registration;
                case 4:
                    string cookie = Assert.Single(request.Headers.GetValues("Cookie"));
                    Assert.Contains("ltuid=1111", cookie);
                    Assert.Contains("ltoken=fixture", cookie);
                    Assert.Contains("acw_tc=fixture-waf", cookie);
                    Assert.Contains("aliyungf_tc=fixture-waf", cookie);
                    return Json("""{"retcode":0,"data":{"challenge":"fixture-proof"}}""");
                case 5:
                    Assert.Equal(passportCookie, Assert.Single(request.Headers.GetValues("Cookie")));
                    return Json("""{"retcode":0,"data":{"schedule_id":7,"floors":[]}}""");
                default:
                    throw new InvalidOperationException("Unexpected verification request.");
            }
        }));
        using var source = new HoYoGameRecordSpiralAbyssSource(TimeProvider.System, client);

        var result = await source.FetchAsync(
            Passport("fixture"), Role, SpiralAbyssPeriod.Current, default,
            (_, _) => Task.FromResult<PassportGeetestResult?>(
                new("fixture-human-answer", "fixture-validation", "fixture-sdk-seccode")));

        Assert.Equal(SpiralAbyssCollectionStatus.Empty, result.Status);
        Assert.Equal(GameRecordVerificationStage.Retry, result.VerificationStage);
        Assert.False(result.VerificationSessionCookieSent);
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static PassportAccount Passport(string? lToken) =>
        new(Guid.NewGuid(), "1111", null, null, PassportLoginMethod.QrCode,
            new PassportCredentials("fixture", lToken, null, null, null, null, null),
            PassportDeviceIdentity.Create(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}

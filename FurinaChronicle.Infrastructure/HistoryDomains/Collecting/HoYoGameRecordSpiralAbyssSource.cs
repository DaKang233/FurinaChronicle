// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

public sealed class HoYoGameRecordSpiralAbyssSource : ISpiralAbyssSource, IDisposable
{
    private const string Host = "https://api-takumi-record.mihoyo.com";
    private const string OverseaHost = "https://bbs-api-os.hoyolab.com";
    private const string OverseaAppVersion = "2.54.0";
    private const string OverseaGameRecordSalt = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";
    private const string AbyssPath = "/game_record/app/genshin/api/spiralAbyss";
    private const string IndexPath = "/game_record/app/genshin/api/index";

    /// <summary>
    /// Account-level risk code. It is not a captcha gate: the service exposes
    /// the captcha on the profile index endpoint and rejects a verification
    /// submitted for the abyss path with 10306.
    /// </summary>
    private const int AccountRiskProviderCode = 5003;
    private const string VerificationPath = "/game_record/app/card/wapi/";
    private static readonly Uri VerificationSubmissionUri = new(Host + VerificationPath + "verifyVerification");
    private const int VerificationSubmissionRejected = 10306;

    // The index endpoint answers a full profile document; the abyss payload is
    // smaller, but both are bounded so a runaway response fails predictably.
    private const int MaxVerificationResponseBytes = 4 * 1024 * 1024;
    private const string AppVersion = "2.115.0";
    private const string GameRecordSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    /// <summary>
    /// Cookies set by infrastructure in front of the API (for example the
    /// Aliyun WAF) are echoed on every response and are not a verification
    /// session. Counting them would report that a session was carried when the
    /// service never issued one.
    /// </summary>
    private static readonly HashSet<string> NonSessionCookieNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "acw_tc", "aliyungf_tc",
    };

    private static readonly JsonSerializerOptions VerificationJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly MiYouSheSpiralAbyssResponseReader reader;

    public HoYoGameRecordSpiralAbyssSource(TimeProvider timeProvider, HttpClient? httpClient = null)
    {
        ownsClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });
        reader = new(timeProvider);
    }

    public async Task<SpiralAbyssSourceResult> FetchAsync(
        PassportAccount passport, GameRoleNaturalIdentity role,
        SpiralAbyssPeriod period, CancellationToken cancellationToken,
        GameRecordVerificationPrompt? verificationPrompt = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool isOversea = passport.Realm == PassportRealm.Oversea;
        bool supported = isOversea
            ? role.GameBiz == GenshinGameRoleIdentity.GlobalGameBiz &&
                role.Server is "os_usa" or "os_euro" or "os_asia" or "os_cht"
            : role.GameBiz == GenshinGameRoleIdentity.MainlandGameBiz &&
                role.Server is "cn_gf01" or "cn_qd01";
        if (!supported)
        {
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }

        if (passport.Credentials.LToken is not { } token ||
            !IsHeaderToken(token) || !passport.Aid.All(char.IsAsciiDigit))
        {
            return new(SpiralAbyssCollectionStatus.AuthenticationRequired);
        }

        if (period is not (SpiralAbyssPeriod.Current or SpiralAbyssPeriod.Previous))
        {
            throw new ArgumentOutOfRangeException(nameof(period));
        }

        string query = string.Create(CultureInfo.InvariantCulture,
            $"role_id={role.Uid}&schedule_type={(int)period}&server={role.Server}");
        string indexQuery = string.Create(CultureInfo.InvariantCulture,
            $"role_id={role.Uid}&server={role.Server}");
        string suffix = isOversea ? "_v2" : string.Empty;
        string cookie = $"ltuid{suffix}={passport.Aid}; ltoken{suffix}={token}";
        if (passport.Credentials.CookieToken is { } cookieToken && IsHeaderToken(cookieToken))
        {
            cookie += $"; account_id{suffix}={passport.Aid}; cookie_token{suffix}={cookieToken}";
        }
        if (isOversea && passport.Mid is { } mid && IsHeaderToken(mid))
        {
            cookie += $"; account_mid_v2={mid}";
        }
        GameRecordVerificationStage? stage = null;
        try
        {
            if (!isOversea && verificationPrompt is not null)
            {
                // Mirror the reference client: the profile index endpoint is
                // requested first and carries the captcha gate for a risky
                // session. Requesting the abyss first makes the service answer
                // 5003 and removes the captcha opportunity.
                VerificationExchange indexProbe = await ReadIndexAsync(null);
                if (indexProbe.ProviderCode == 1034)
                {
                    return await VerifyIndexAndReadAsync();
                }
            }

            var first = await ReadRecordAsync(null);

            // 5003 is account-level risk, not a captcha gate; the index probe
            // above already reported whether a challenge was available.
            if (first.Status == SpiralAbyssCollectionStatus.AccessDenied &&
                first.ProviderCode == AccountRiskProviderCode)
            {
                return first;
            }

            if (first.Status != SpiralAbyssCollectionStatus.VerificationRequired ||
                verificationPrompt is null || isOversea)
            {
                return first;
            }

            stage = GameRecordVerificationStage.Registration;
            using var registration = CreateRequest(HttpMethod.Get, VerificationPath + "createVerification",
                "is_high=true", passport, cookie);
            AddVerificationScope(registration, Host + AbyssPath);
            var verificationCookies = new CookieContainer();
            var registrationResult = await ReadVerificationAsync(
                registration, cancellationToken, verificationCookies);            if (registrationResult.Data is not { Gt: { Length: > 0 } gt, Challenge: { Length: > 0 } initial })
            {
                return VerificationFailure(registrationResult, GameRecordVerificationStage.Registration);
            }

            PassportGeetestResult? answer = await verificationPrompt(
                new PassportGeetestChallenge(string.Empty, gt, initial, IsOversea: false), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (answer is null)
            {
                return first with { Status = SpiralAbyssCollectionStatus.VerificationCanceled };
            }

            stage = GameRecordVerificationStage.Submission;
            if (string.IsNullOrWhiteSpace(answer.Challenge) ||
                string.IsNullOrWhiteSpace(answer.Validate) ||
                string.IsNullOrWhiteSpace(answer.Seccode))
            {
                return new(SpiralAbyssCollectionStatus.VerificationFailed, VerificationStage: stage);
            }

            string body = JsonSerializer.Serialize(new
            {
                geetest_challenge = answer.Challenge,
                geetest_validate = answer.Validate,
                geetest_seccode = answer.Seccode,
            });
            // The registration response can rotate session cookies. The
            // reference client shares one cookie container across registration
            // and submission, so a freshly issued value has to take precedence
            // over the stored passport cookie; otherwise the submission runs on
            // a stale session and the service rejects it.
            (string submissionCookies, bool sentSessionCookie) =
                MergeVerificationCookies(cookie, verificationCookies);
            using var submission = CreateRequest(HttpMethod.Post, VerificationPath + "verifyVerification",
                "", passport, submissionCookies, body);
            AddVerificationScope(submission, Host + AbyssPath);
            var submissionResult = await ReadVerificationAsync(submission, cancellationToken);            if (submissionResult.Data?.Challenge is not { } verified || !IsHeaderToken(verified))
            {
                var failure = VerificationFailure(submissionResult, GameRecordVerificationStage.Submission)
                    with { VerificationSessionCookieSent = sentSessionCookie };
                if (submissionResult.ProviderCode != VerificationSubmissionRejected)
                {
                    return failure;
                }

                // An account can already have been verified in another client.
                // Only an ordinary request accepted by the service establishes access.
                try
                {
                    var checkedRecord = await ReadRecordAsync(null);
                    return checkedRecord.Status is SpiralAbyssCollectionStatus.Received or
                        SpiralAbyssCollectionStatus.Empty or SpiralAbyssCollectionStatus.NotUnlocked
                        ? checkedRecord
                        : failure;
                }
                catch (HttpRequestException)
                {
                    return failure;
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return failure;
                }
            }

            stage = GameRecordVerificationStage.Retry;
            var retried = await ReadRecordAsync(verified);
            return retried with { VerificationStage = stage, VerificationSessionCookieSent = sentSessionCookie };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable, VerificationStage: stage);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable, VerificationStage: stage);
        }

        async Task<SpiralAbyssSourceResult> VerifyIndexAndReadAsync()
        {
            stage = GameRecordVerificationStage.Registration;
            using var indexRegistration = CreateRequest(
                HttpMethod.Get, VerificationPath + "createVerification",
                "is_high=true", passport, cookie);
            AddVerificationScope(indexRegistration, Host + IndexPath);
            var indexVerificationCookies = new CookieContainer();
            var indexRegistrationResult = await ReadVerificationAsync(
                indexRegistration, cancellationToken, indexVerificationCookies);            if (indexRegistrationResult.Data is not
                { Gt: { Length: > 0 } indexGt, Challenge: { Length: > 0 } indexInitial })
            {
                return VerificationFailure(
                    indexRegistrationResult, GameRecordVerificationStage.Registration);
            }

            PassportGeetestResult? indexAnswer = await verificationPrompt!(
                new PassportGeetestChallenge(string.Empty, indexGt, indexInitial, IsOversea: false),
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (indexAnswer is null)
            {
                return new(
                    SpiralAbyssCollectionStatus.VerificationCanceled,
                    VerificationStage: stage);
            }

            stage = GameRecordVerificationStage.Submission;
            if (string.IsNullOrWhiteSpace(indexAnswer.Challenge) ||
                string.IsNullOrWhiteSpace(indexAnswer.Validate) ||
                string.IsNullOrWhiteSpace(indexAnswer.Seccode))
            {
                return new(SpiralAbyssCollectionStatus.VerificationFailed, VerificationStage: stage);
            }

            string indexBody = JsonSerializer.Serialize(new
            {
                geetest_challenge = indexAnswer.Challenge,
                geetest_validate = indexAnswer.Validate,
                geetest_seccode = indexAnswer.Seccode,
            });
            (string indexSubmissionCookies, bool indexSessionCookieSent) =
                MergeVerificationCookies(cookie, indexVerificationCookies);
            using var indexSubmission = CreateRequest(
                HttpMethod.Post, VerificationPath + "verifyVerification",
                "", passport, indexSubmissionCookies, indexBody);
            AddVerificationScope(indexSubmission, Host + IndexPath);
            var indexSubmissionResult = await ReadVerificationAsync(
                indexSubmission, cancellationToken);            if (indexSubmissionResult.Data?.Challenge is not { } indexProof ||
                !IsHeaderToken(indexProof))
            {
                return VerificationFailure(
                        indexSubmissionResult, GameRecordVerificationStage.Submission)
                    with { VerificationSessionCookieSent = indexSessionCookieSent };
            }

            stage = GameRecordVerificationStage.Retry;
            VerificationExchange retriedIndex = await ReadIndexAsync(indexProof);
            if (retriedIndex.ProviderCode != 0)
            {
                return new(
                    SpiralAbyssCollectionStatus.VerificationFailed,
                    HttpStatus: retriedIndex.HttpStatus,
                    ProviderCode: retriedIndex.ProviderCode,
                    VerificationStage: stage,
                    VerificationSessionCookieSent: indexSessionCookieSent);
            }

            SpiralAbyssSourceResult afterVerification = await ReadRecordAsync(null);
            return afterVerification with
            {
                VerificationStage = stage,
                VerificationSessionCookieSent = indexSessionCookieSent,
            };
        }

        async Task<SpiralAbyssSourceResult> ReadRecordAsync(string? proof)
        {
            using var request = CreateRequest(HttpMethod.Get, AbyssPath, query, passport, cookie);
            if (proof is not null)
            {
                request.Headers.Add("x-rpc-challenge", proof);
            }

            using var timeout = NetworkTimeout(cancellationToken);
            using var response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            SpiralAbyssSourceResult result = await reader.ReadAsync(response, role, timeout.Token);
            return result;
        }

        async Task<VerificationExchange> ReadIndexAsync(string? proof)
        {
            using var request = CreateRequest(HttpMethod.Get, IndexPath, indexQuery, passport, cookie);
            if (proof is not null)
            {
                request.Headers.Add("x-rpc-challenge", proof);
            }

            VerificationExchange exchange = await ReadVerificationAsync(request, cancellationToken);
            return exchange;
        }
    }

    private static SpiralAbyssSourceResult VerificationFailure(
        VerificationExchange exchange, GameRecordVerificationStage stage) =>
        new(SpiralAbyssCollectionStatus.VerificationFailed,
            HttpStatus: exchange.HttpStatus,
            ProviderCode: exchange.ProviderCode,
            VerificationStage: stage);

    private async Task<VerificationExchange> ReadVerificationAsync(
        HttpRequestMessage request, CancellationToken cancellationToken,
        CookieContainer? registrationCookies = null)
    {
        using var timeout = NetworkTimeout(cancellationToken);
        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (registrationCookies is not null &&
            response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            foreach (string header in cookieHeaders)
            {
                try
                {
                    registrationCookies.SetCookies(request.RequestUri!, header);
                }
                catch (CookieException)
                {
                    // Ignore malformed response cookies.
                }
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            return new((int)response.StatusCode);
        }

        try
        {
            // The index endpoint doubles as the captcha gate and answers a full
            // profile document, which is far larger than a verification body.
            await response.Content.LoadIntoBufferAsync(
                MaxVerificationResponseBytes, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var envelope = await JsonSerializer.DeserializeAsync<VerificationEnvelope>(
                stream, VerificationJson, timeout.Token);
            return new((int)response.StatusCode, envelope?.Retcode,
                envelope?.Retcode == 0 ? envelope.Data : null);
        }
        catch (JsonException)
        {
            return new((int)response.StatusCode);
        }
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method, string path, string query, PassportAccount passport,
        string cookie, string body = "")
    {
        bool isOversea = passport.Realm == PassportRealm.Oversea;
        var request = new HttpRequestMessage(method, (isOversea ? OverseaHost : Host) + path +
            (query.Length > 0 ? "?" + query : ""));
        if (body.Length > 0)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        request.Headers.Accept.ParseAdd("application/json");
        string version = isOversea ? OverseaAppVersion : AppVersion;
        string clientName = isOversea ? "miHoYoBBSOversea" : "miHoYoBBS";
        if (isOversea)
        {
            request.Headers.Add("x-rpc-language", "zh-cn");
        }
        else
        {
            request.Headers.Referrer = new Uri("https://webstatic.mihoyo.com/");
        }
        request.Headers.Add("x-rpc-client_type", "5");
        request.Headers.Add("x-rpc-app_version", version);
        request.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) {clientName}/{version}");
        request.Headers.Add("x-rpc-device_id", passport.Device.DeviceId);
        if (passport.Device.DeviceFingerprint is { } fingerprint && IsHeaderToken(fingerprint))
        {
            request.Headers.Add("x-rpc-device_fp", fingerprint);
        }
        request.Headers.Add("DS",
            MiHoYoRequestSigning.CreateDsGen2(
                isOversea ? OverseaGameRecordSalt : GameRecordSalt, body, query, numericNonce: true));
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static (string Cookies, bool ContainsSessionCookie) MergeVerificationCookies(
        string accountCookies, CookieContainer verificationCookies)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string pair in accountCookies.Split(
            ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separator = pair.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0)
            {
                merged[pair[..separator]] = pair[(separator + 1)..];
            }
        }

        bool containsSessionCookie = false;
        foreach (Cookie issued in verificationCookies.GetCookies(VerificationSubmissionUri).Cast<Cookie>())
        {
            if (string.IsNullOrEmpty(issued.Name))
            {
                continue;
            }

            // The registration response may rotate a passport cookie. The
            // reference client shares one cookie container across registration
            // and submission, so the freshly issued value wins.
            bool differs = !merged.TryGetValue(issued.Name, out string? previous) ||
                !string.Equals(previous, issued.Value, StringComparison.Ordinal);
            merged[issued.Name] = issued.Value;
            if (differs && !NonSessionCookieNames.Contains(issued.Name))
            {
                containsSessionCookie = true;
            }
        }

        return (string.Join("; ", merged.Select(pair => $"{pair.Key}={pair.Value}")),
            containsSessionCookie);
    }

    private static void AddVerificationScope(
        HttpRequestMessage request, string challengePath)
    {
        request.Headers.Add("x-rpc-challenge_game", "2");
        request.Headers.Add("x-rpc-challenge_path", challengePath);
    }

    private static CancellationTokenSource NetworkTimeout(CancellationToken cancellationToken)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        return timeout;
    }

    private static bool IsHeaderToken(string value) =>
        value.Length > 0 && value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'));

    private sealed record VerificationExchange(
        int HttpStatus, int? ProviderCode = null, VerificationData? Data = null);


    private sealed record VerificationEnvelope(int? Retcode, VerificationData? Data);
    private sealed record VerificationData(string? Gt, string? Challenge);

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }
}

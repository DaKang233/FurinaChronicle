// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class MiHoYoPassportClient : IDisposable
{
    public const string CreateQrUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/createQRLogin";
    public const string QueryQrUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/queryQRLoginStatus";
    public const string CreateCaptchaUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-verifier/verifier/createLoginCaptcha";
    public const string MobileLoginUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/loginByMobileCaptcha";
    public const string OverseaPasswordLoginUrl =
        "https://sg-public-api.hoyoverse.com/account/ma-passport/api/appLoginByPassword";
    public const string OverseaGetActionTicketInfoUrl =
        "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/getActionTicketInfo";
    public const string OverseaVerifyActionTicketUrl =
        "https://sg-public-api.hoyoverse.com/account/ma-verifier/api/verifyActionTicketPartly";
    public const string OverseaGetLTokenUrl =
        "https://api-account-os.hoyoverse.com/account/auth/api/getLTokenBySToken";
    public const string OverseaGetCookieTokenUrl =
        "https://api-account-os.hoyoverse.com/account/auth/api/getCookieAccountInfoBySToken";
    public const string GetLTokenUrl =
        "https://passport-api.mihoyo.com/account/auth/api/getLTokenBySToken";
    public const string GetCookieTokenUrl =
        "https://passport-api.mihoyo.com/account/auth/api/getCookieAccountInfoBySToken";
    public const string ExchangeTokenUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-session/app/exchange";
    public const string VerifySessionUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-session/app/verify";

    private const string PassportAppId = "bll8iq97cem8";
    private const string SessionAppId = "ddxf5dufpuyo";
    private const string AppVersion = "2.119.0";
    private const string OverseaAppVersion = "4.20.0";
    private const string PassportSdkVersion = "2.44.0";
    private const string PassportAndroidVersion = "12";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string overseaVerifierDeviceId = Guid.NewGuid().ToString("D");
    // The passport endpoints bind a request sequence to one process lifecycle.
    private readonly string passportLifecycleId = Guid.NewGuid().ToString("D");

    public MiHoYoPassportClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                // Every authenticated request constructs its Cookie header
                // explicitly. AndroidMessageHandler otherwise replaces that
                // header with cookies captured from earlier login responses.
                UseCookies = false
            })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
    }

    public async Task<PassportQrSession> CreateQrSessionAsync(
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendPassportJsonAsync(
            CreateQrUrl,
            new { },
            device,
            aigis: null,
            useSessionApp: true,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(response, cancellationToken);
        JsonElement data = document.RootElement.GetProperty("data");
        return new PassportQrSession(
            RequiredString(data, "ticket"),
            RequiredString(data, "url"),
            device.DeviceId);
    }

    public async Task<PassportQrPollResult> PollQrSessionAsync(
        PassportQrSession session,
        CancellationToken cancellationToken = default)
    {
        var device = new PassportDeviceIdentity(
            session.DeviceId,
            DeviceFingerprint: null);
        using HttpResponseMessage response = await SendPassportJsonAsync(
            QueryQrUrl,
            new { ticket = session.Ticket },
            device,
            aigis: null,
            useSessionApp: true,
            cancellationToken);
        using JsonDocument document = await ReadResponseAsync(response, cancellationToken);
        JsonElement root = document.RootElement;
        int retcode = root.GetProperty("retcode").GetInt32();
        if (retcode is -3501 or -106)
        {
            return new PassportQrPollResult(PassportQrStatus.Expired);
        }

        EnsureSuccess(root);
        JsonElement data = root.GetProperty("data");
        string status = RequiredString(data, "status");
        if (status.Equals("scanned", StringComparison.OrdinalIgnoreCase))
        {
            return new PassportQrPollResult(PassportQrStatus.Scanned);
        }

        if (!status.Equals("confirmed", StringComparison.OrdinalIgnoreCase))
        {
            return new PassportQrPollResult(PassportQrStatus.Pending);
        }

        JsonElement userInfo = data.GetProperty("user_info");
        string? sToken = null;
        if (data.TryGetProperty("tokens", out JsonElement tokens) &&
            tokens.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement token in tokens.EnumerateArray())
            {
                if (token.TryGetProperty("token_type", out JsonElement type) &&
                    type.GetInt32() == 1)
                {
                    sToken = RequiredString(token, "token");
                    break;
                }
            }
        }

        return new PassportQrPollResult(
            PassportQrStatus.Confirmed,
            new PassportLoginTokens(
                RequiredString(userInfo, "aid"),
                RequiredString(userInfo, "mid"),
                sToken,
                DisplayName: OptionalString(userInfo, "account_name"),
                DeviceFingerprint: ReadLoginFingerprint(response, QueryQrUrl)));
    }

    public async Task<MobileCaptchaChallenge> SendMobileCaptchaAsync(
        string mobile,
        PassportDeviceIdentity device,
        string? aigis = null,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendPassportJsonAsync(
            CreateCaptchaUrl,
            new Dictionary<string, string>
            {
                ["area_code"] = EncryptCn("+86"),
                ["mobile"] = EncryptCn(mobile)
            },
            device,
            aigis,
            useSessionApp: false,
            cancellationToken,
            referer: "https://user.miyoushe.com/");
        using JsonDocument document = await ReadSuccessAsync(response, cancellationToken);
        string? returnedAigis = response.Headers.TryGetValues(
            "X-Rpc-Aigis",
            out IEnumerable<string>? values)
            ? values.SingleOrDefault()
            : null;
        JsonElement data = document.RootElement.GetProperty("data");
        return new MobileCaptchaChallenge(
            RequiredString(data, "action_type"),
            returnedAigis ?? aigis,
            device.DeviceId);
    }

    public async Task<PassportLoginTokens> LoginWithMobileCaptchaAsync(
        string mobile,
        string captcha,
        MobileCaptchaChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendPassportJsonAsync(
            MobileLoginUrl,
            new Dictionary<string, string>
            {
                ["area_code"] = EncryptCn("+86"),
                ["action_type"] = challenge.ActionType,
                ["captcha"] = captcha,
                ["mobile"] = EncryptCn(mobile)
            },
            device,
            challenge.Aigis,
            useSessionApp: false,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(response, cancellationToken);
        JsonElement data = document.RootElement.GetProperty("data");
        JsonElement token = data.GetProperty("token");
        JsonElement userInfo = data.GetProperty("user_info");
        return new PassportLoginTokens(
            RequiredString(userInfo, "aid"),
            RequiredString(userInfo, "mid"),
            RequiredString(token, "token"),
            DisplayName: OptionalString(userInfo, "account_name"),
            DeviceFingerprint: ReadLoginFingerprint(response, MobileLoginUrl));
    }

    public async Task<PassportLoginTokens> LoginWithOverseaPasswordAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        OverseaPasswordLoginAttempt attempt =
            await AttemptOverseaPasswordLoginAsync(
                account,
                password,
                device,
                cancellationToken: cancellationToken);
        if (attempt.Tokens is { } tokens)
        {
            return tokens;
        }

        bool requiresSecurityVerification =
            attempt.GeetestChallenge is not null ||
            attempt.AccountVerificationChallenge is not null;
        throw new InvalidOperationException(requiresSecurityVerification
            ? "HoYoLAB 要求额外的安全验证，请完成 GeeTest/账号验证码后重试。"
            : $"HoYoLAB 登录失败 ({attempt.Retcode}): {attempt.Message ?? "Unknown error"}");
    }

    public async Task<OverseaPasswordLoginAttempt> AttemptOverseaPasswordLoginAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        string? aigis = null,
        string? verify = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(device);
        using HttpResponseMessage response = await SendOverseaPassportJsonAsync(
            OverseaPasswordLoginUrl,
            new
            {
                account = EncryptOversea(account),
                password = EncryptOversea(password),
                token_type = 2
            },
            device,
            aigis,
            verify,
            cancellationToken);
        using JsonDocument document = await ReadResponseAsync(
            response,
            cancellationToken);
        JsonElement root = document.RootElement;
        int retcode = root.GetProperty("retcode").GetInt32();
        string? message = OptionalString(root, "message");
        string? rawAigis = HeaderValue(response, "X-Rpc-Aigis");
        string? rawVerify = HeaderValue(response, "X-Rpc-Verify");
        if (retcode != 0)
        {
            PassportGeetestChallenge? geetestChallenge =
                string.IsNullOrWhiteSpace(rawAigis)
                    ? null
                    : ParseGeetestChallenge(rawAigis);
            PassportAccountVerificationChallenge? accountChallenge =
                string.IsNullOrWhiteSpace(rawVerify)
                    ? null
                    : ParseAccountVerificationChallenge(rawVerify);
            return new OverseaPasswordLoginAttempt(
                Tokens: null,
                geetestChallenge,
                accountChallenge,
                retcode,
                message);
        }

        JsonElement data = root.GetProperty("data");
        JsonElement token = data.GetProperty("token");
        JsonElement userInfo = data.GetProperty("user_info");
        return new OverseaPasswordLoginAttempt(
            new PassportLoginTokens(
                RequiredString(userInfo, "aid"),
                RequiredString(userInfo, "mid"),
                RequiredString(token, "token"),
                DisplayName: OptionalString(userInfo, "account_name")));
    }

    public Task<OverseaPasswordLoginAttempt> AttemptPasswordLoginAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        string? aigis = null,
        string? verify = null,
        CancellationToken cancellationToken = default)
    {
        return AttemptOverseaPasswordLoginAsync(
            account,
            password,
            device,
            aigis,
            verify,
            cancellationToken);
    }

    public string CompleteGeetestChallenge(
        PassportGeetestChallenge challenge,
        PassportGeetestResult result)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.Validate);
        string sessionId = ParseSessionId(challenge.State);
        string proof = JsonSerializer.Serialize(new
        {
            geetest_challenge = result.Challenge,
            geetest_validate = result.Validate,
            geetest_seccode = result.Seccode ?? $"{result.Validate}|jordan"
        });
        return $"{sessionId};{Convert.ToBase64String(Encoding.UTF8.GetBytes(proof))}";
    }

    private static string ParseSessionId(string state)
    {
        using JsonDocument session = JsonDocument.Parse(state);
        return RequiredString(session.RootElement, "session_id");
    }

    public async Task<PassportAccountVerificationChallenge> PrepareAccountVerificationAsync(
        PassportAccountVerificationChallenge challenge,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        using HttpResponseMessage response = await SendOverseaVerifierJsonAsync(
            OverseaGetActionTicketInfoUrl,
            CreateActionTicketRequest(challenge.Ticket),
            aigis: null,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(
            response,
            cancellationToken,
            allowJsonOnHttpError: true);
        JsonElement data = document.RootElement.GetProperty("data");
        string? destination = null;
        if (data.TryGetProperty("user_info", out JsonElement userInfo))
        {
            destination = challenge.Method == HoYoLabVerificationMethod.Mobile
                ? OptionalString(userInfo, "safe_mobile") ??
                    OptionalString(userInfo, "mobile")
                : OptionalString(userInfo, "email");
        }

        return challenge with { Destination = destination };
    }

    public async Task VerifyAccountAsync(
        PassportAccountVerificationChallenge challenge,
        string captcha,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        ArgumentException.ThrowIfNullOrWhiteSpace(captcha);
        using HttpResponseMessage response = await SendOverseaVerifierJsonAsync(
            OverseaVerifyActionTicketUrl,
            CreateActionTicketRequest(
                challenge.Ticket,
                captcha.Trim(),
                challenge.Method),
            aigis: null,
            cancellationToken);
        using JsonDocument _ = await ReadSuccessAsync(response, cancellationToken);

        using HttpResponseMessage confirmResponse =
            await SendOverseaVerifierJsonAsync(
                OverseaGetActionTicketInfoUrl,
                CreateActionTicketRequest(challenge.Ticket),
                aigis: null,
                cancellationToken);
        using JsonDocument confirmDocument = await ReadSuccessAsync(
            confirmResponse,
            cancellationToken,
            allowJsonOnHttpError: true);
        JsonElement data = confirmDocument.RootElement.GetProperty("data");
        string? status = data.TryGetProperty("verify_info", out JsonElement info)
            ? OptionalString(info, "status")
            : null;
        if (!string.Equals(status, "StatusVerified", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("账号安全验证码未通过验证。");
        }
    }

    public string CompleteAccountVerificationChallenge(
        PassportAccountVerificationChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        JsonNode root = JsonNode.Parse(challenge.State)
            ?? throw new FormatException("X-Rpc-Verify 内容为空。");
        root["verify_str"] = null;
        return root.ToJsonString();
    }

    public async Task<PassportDerivedTokens> GetDerivedTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        string sToken = account.Credentials.SToken
            ?? throw new InvalidOperationException("Passport account does not contain SToken.");
        string mid = account.Mid
            ?? throw new InvalidOperationException("Passport account does not contain mid.");
        if (account.Realm == PassportRealm.Oversea)
        {
            string? overseaLToken = await GetOverseaTokenAsync(
                OverseaGetLTokenUrl,
                account,
                "ltoken",
                cancellationToken);
            string? overseaCookieToken = await GetOverseaTokenAsync(
                OverseaGetCookieTokenUrl,
                account,
                "cookie_token",
                cancellationToken);
            return new PassportDerivedTokens(
                overseaLToken,
                overseaCookieToken);
        }

        // Passport v2 first: the current session service issues derived tokens
        // through the exchange endpoint and reports a refused session as -100.
        PassportDerivedTokens? exchanged = await TryExchangeTokensAsync(
            account,
            cancellationToken);
        if (exchanged is { } derived)
        {
            return derived;
        }

        // Legacy fallback: the v1 endpoints still answer for a session whose
        // cookie is already accepted, and they are the only path left if the
        // exchange refuses this process profile.
        string cookie = $"mid={mid};stoken={sToken};stuid={account.Aid}";

        string? lToken = await GetTokenAsync(
            GetLTokenUrl,
            cookie,
            "ltoken",
            account.Device,
            cancellationToken);
        string? cookieToken = await GetTokenAsync(
            GetCookieTokenUrl,
            cookie,
            "cookie_token",
            account.Device,
            cancellationToken);
        return new PassportDerivedTokens(lToken, cookieToken);
    }

    /// <summary>
    /// Exchanges the root SToken for one derived token. The signed body must be
    /// byte-identical to the body that is sent, so a single serialized string
    /// feeds both the request and the signature.
    /// </summary>
    private async Task<string?> ExchangeTokenAsync(
        PassportAccount account,
        int destinationTokenType,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new
        {
            dst_token_type = destinationTokenType,
            src_token = new
            {
                token = account.Credentials.SToken,
                token_type = 1,
            },
            mid = account.Mid,
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, ExchangeTokenUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Mozilla/5.0 (Linux; Android {PassportAndroidVersion}) Mobile miHoYoBBS/{AppVersion}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", PassportAppId);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "2");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-device_id",
            account.Device.DeviceId);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-sdk_version",
            PassportSdkVersion);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-account_version",
            PassportSdkVersion);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-sys_version",
            PassportAndroidVersion);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-lifecycle_id",
            passportLifecycleId);
        if (!string.IsNullOrWhiteSpace(account.Device.DeviceFingerprint))
        {
            request.Headers.TryAddWithoutValidation(
                "x-rpc-device_fp",
                Uri.EscapeDataString(account.Device.DeviceFingerprint));
        }

        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDsGen2(
                MiHoYoRequestSigning.PassportSalt,
                body));
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadResponseAsync(
            response,
            cancellationToken);
        JsonElement root = document.RootElement;
        int retcode = root.TryGetProperty("retcode", out JsonElement code) &&
            code.ValueKind == JsonValueKind.Number
            ? code.GetInt32()
            : -1;
        if (retcode != 0)
        {
            throw new PassportTokenExchangeException("derived token", retcode);
        }

        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (data.TryGetProperty("token", out JsonElement tokenElement) &&
            tokenElement.ValueKind == JsonValueKind.Object)
        {
            return OptionalString(tokenElement, "token");
        }

        return OptionalString(
            data,
            destinationTokenType == 2 ? "ltoken" : "cookie_token");
    }

    private async Task<PassportDerivedTokens?> TryExchangeTokensAsync(
        PassportAccount account,
        CancellationToken cancellationToken)
    {
        try
        {
            string? lToken = await ExchangeTokenAsync(
                account,
                destinationTokenType: 2,
                cancellationToken);
            string? cookieToken = await ExchangeTokenAsync(
                account,
                destinationTokenType: 4,
                cancellationToken);
            return lToken is null && cookieToken is null
                ? null
                : new PassportDerivedTokens(lToken, cookieToken);
        }
        catch (PassportTokenExchangeException)
        {
            // The legacy path below still gets a chance; the caller records the
            // failure through the typed exception only when both paths fail.
            return null;
        }
    }

    private async Task<string?> GetOverseaTokenAsync(
        string url,
        PassportAccount account,
        string propertyName,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(new
        {
            stoken = account.Credentials.SToken,
            uid = account.Aid
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"stoken={account.Credentials.SToken}; mid={account.Mid}; stuid={account.Aid}");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBSOversea/2.54.0");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.54.0");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-language", "zh-cn");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", account.Device.DeviceId);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(
            response,
            cancellationToken);
        return OptionalString(document.RootElement.GetProperty("data"), propertyName);
    }

    public async Task<PassportSessionVerification> VerifySessionAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        string sToken = account.Credentials.SToken
            ?? throw new InvalidOperationException("Passport account does not contain SToken.");
        string mid = account.Mid
            ?? throw new InvalidOperationException("Passport account does not contain mid.");
        var payload = new
        {
            token = new { token_type = 1, token = sToken },
            refresh = true,
            mid
        };
        string body = JsonSerializer.Serialize(payload);
        var request = new HttpRequestMessage(HttpMethod.Post, VerifySessionUrl)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", SessionAppId);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "3");
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "hyp_cn");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", account.Device.DeviceId);
        if (!string.IsNullOrWhiteSpace(account.Device.DeviceFingerprint))
        {
            request.Headers.TryAddWithoutValidation(
                "x-rpc-device_fp",
                account.Device.DeviceFingerprint);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(response, cancellationToken);
        JsonElement data = document.RootElement.GetProperty("data");
        string? refreshed = data.TryGetProperty("new_token", out JsonElement newToken) &&
            newToken.ValueKind == JsonValueKind.Object
            ? OptionalString(newToken, "token")
            : null;
        return new PassportSessionVerification(refreshed);
    }

    private async Task<string?> GetTokenAsync(
        string url,
        string cookie,
        string propertyName,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyPassportHeaders(request, body: string.Empty, device, aigis: null);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(response, cancellationToken);
        return OptionalString(
            document.RootElement.GetProperty("data"),
            propertyName);
    }

    private async Task<HttpResponseMessage> SendPassportJsonAsync(
        string url,
        object payload,
        PassportDeviceIdentity device,
        string? aigis,
        bool useSessionApp,
        CancellationToken cancellationToken,
        string? referer = null)
    {
        string body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyPassportHeaders(request, body, device, aigis, useSessionApp, referer);
        try
        {
            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static void ApplyPassportHeaders(
        HttpRequestMessage request,
        string body,
        PassportDeviceIdentity device,
        string? aigis,
        bool useSessionApp = false,
        string? referer = null)
    {
        // The QR endpoints belong to the HoyoPlay session app; everything else
        // speaks the passport app, which now reports its own SDK version and a
        // mobile profile.
        bool passportApp = !useSessionApp;
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            passportApp
                ? $"Mozilla/5.0 (Linux; Android {PassportAndroidVersion}) Mobile miHoYoBBS/{AppVersion}"
                : $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-cn");
        request.Headers.TryAddWithoutValidation("x-rpc-aigis", aigis ?? string.Empty);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-app_id",
            useSessionApp ? SessionAppId : PassportAppId);
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation(
            "x-rpc-client_type",
            useSessionApp ? "3" : "2");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", device.DeviceId);
        request.Headers.TryAddWithoutValidation("x-rpc-device_name", string.Empty);
        request.Headers.TryAddWithoutValidation("x-rpc-game_biz", "bbs_cn");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-sdk_version",
            passportApp ? PassportSdkVersion : "2.16.0");
        if (passportApp)
        {
            request.Headers.TryAddWithoutValidation(
                "x-rpc-account_version",
                PassportSdkVersion);
            request.Headers.TryAddWithoutValidation(
                "x-rpc-sys_version",
                PassportAndroidVersion);
        }

        if (referer is not null)
        {
            request.Headers.Referrer = new Uri(referer);
        }

        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDsGen2(
                MiHoYoRequestSigning.PassportSalt,
                body));
    }

    private static async Task<JsonDocument> ReadSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool allowJsonOnHttpError = false)
    {
        JsonDocument document = await ReadResponseAsync(
            response,
            cancellationToken,
            allowJsonOnHttpError);
        try
        {
            EnsureSuccess(document.RootElement);
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private static async Task<JsonDocument> ReadResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool allowJsonOnHttpError = false)
    {
        if (!allowJsonOnHttpError)
        {
            response.EnsureSuccessStatusCode();
        }

        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
        catch (JsonException) when (!response.IsSuccessStatusCode)
        {
            response.EnsureSuccessStatusCode();
            throw;
        }
    }

    private static void EnsureSuccess(JsonElement root)
    {
        int retcode = root.GetProperty("retcode").GetInt32();
        if (retcode == 0)
        {
            return;
        }

        string message = OptionalString(root, "message") ?? "Unknown error";
        throw new InvalidOperationException(
            $"MiHoYo passport API failed ({retcode}): {message}");
    }

    private static string RequiredString(
        JsonElement element,
        string propertyName)
    {
        return OptionalString(element, propertyName)
            ?? throw new InvalidDataException(
                $"MiHoYo passport response did not contain {propertyName}.");
    }

    private static string? OptionalString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        string? value = property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<HttpResponseMessage> SendOverseaPassportJsonAsync(
        string url,
        object payload,
        PassportDeviceIdentity device,
        string? aigis,
        string? verify,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "HYPContainer/1.1.4.133");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-app_id", "ddxf6vlr1reo");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "3");
        request.Headers.TryAddWithoutValidation("x-rpc-device_id", device.DeviceId);
        if (!string.IsNullOrWhiteSpace(aigis))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-aigis", aigis);
        }

        if (!string.IsNullOrWhiteSpace(verify))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-verify", verify);
        }

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendOverseaVerifierJsonAsync(
        string url,
        object payload,
        string? aigis,
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBSOversea/{OverseaAppVersion}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-app_version",
            OverseaAppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-language", "zh-cn");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-device_id",
            overseaVerifierDeviceId);
        if (!string.IsNullOrWhiteSpace(aigis))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-aigis", aigis);
        }

        return await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
    }

    private static object CreateActionTicketRequest(
        string ticket,
        string? captcha = null,
        HoYoLabVerificationMethod? method = null)
    {
        var request = new Dictionary<string, object?>
        {
            ["action_type"] = "verify_for_component",
            ["action_ticket"] = ticket
        };

        if (captcha is not null && method is not null)
        {
            request["verify_method"] = (int)method.Value;
            request[method == HoYoLabVerificationMethod.Mobile
                ? "mobile_captcha"
                : "email_captcha"] = captcha;
        }

        return request;
    }

    private static PassportGeetestChallenge ParseGeetestChallenge(string state)
    {
        try
        {
            using JsonDocument sessionDocument = JsonDocument.Parse(state);
            JsonElement session = sessionDocument.RootElement;
            string sessionId = RequiredString(session, "session_id");
            string data = RequiredString(session, "data");
            using JsonDocument verificationDocument = JsonDocument.Parse(data);
            JsonElement verification = verificationDocument.RootElement;
            return new PassportGeetestChallenge(
                state,
                RequiredString(verification, "gt"),
                RequiredString(verification, "challenge"));
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "HoYoLAB 返回了无法解析的 GeeTest 挑战。",
                exception);
        }
    }

    private static PassportAccountVerificationChallenge
        ParseAccountVerificationChallenge(string state)
    {
        try
        {
            using JsonDocument riskDocument = JsonDocument.Parse(state);
            string verifyString = RequiredString(
                riskDocument.RootElement,
                "verify_str");
            using JsonDocument verificationDocument = JsonDocument.Parse(
                verifyString);
            JsonElement verification = verificationDocument.RootElement;
            return new PassportAccountVerificationChallenge(
                state,
                RequiredString(verification, "ticket"),
                Method: ParseVerificationMethod(
                    RequiredString(verification, "verify_type")));
        }
        catch (JsonException exception)
        {
            throw new FormatException(
                "HoYoLAB 返回了无法解析的账号安全验证挑战。",
                exception);
        }
    }

    private static HoYoLabVerificationMethod ParseVerificationMethod(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "mobile" or "safe_mobile" or "phone" or "1" =>
                HoYoLabVerificationMethod.Mobile,
            "email" or "2" => HoYoLabVerificationMethod.Email,
            _ => throw new FormatException(
                $"HoYoLAB 返回了不支持的账号验证方式：{value}。")
        };
    }

    private static string? ReadLoginFingerprint(HttpResponseMessage response, string loginUrl)
    {
        if (response.RequestMessage?.RequestUri?.AbsoluteUri != loginUrl ||
            !response.Headers.TryGetValues("x-rpc-device_fp", out var values))
        {
            return null;
        }

        using var headers = values.GetEnumerator();
        if (!headers.MoveNext())
        {
            return null;
        }

        string value = headers.Current;
        return !headers.MoveNext() && value.Length > 0 && value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'))
            ? value
            : null;
    }

    private static string? HeaderValue(
        HttpResponseMessage response,
        string name)
    {
        return response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.SingleOrDefault()
            : null;
    }

    private static string EncryptCn(string value)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(
            """
            -----BEGIN PUBLIC KEY-----
            MIGfMA0GCSqGSIb3DQEBAQUAA4GNADCBiQKBgQDDvekdPMHN3AYhm/vktJT+YJr7
            cI5DcsNKqdsx5DZX0gDuWFuIjzdwButrIYPNmRJ1G8ybDIF7oDW2eEpm5sMbL9zs
            9ExXCdvqrn51qELbqj0XxtMTIpaCHFSI50PfPpTFV9Xt/hmyVwokoOXFlAEgCn+Q
            CgGs52bFoYMtyi+xEQIDAQAB
            -----END PUBLIC KEY-----
            """);
        return Convert.ToBase64String(
            rsa.Encrypt(
                Encoding.UTF8.GetBytes(value),
                RSAEncryptionPadding.Pkcs1));
    }

    private static string EncryptOversea(string value)
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(
            """
            -----BEGIN PUBLIC KEY-----
            MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA4PMS2JVMwBsOIrYWRluY
            wEiFZL7Aphtm9z5Eu/anzJ09nB00uhW+ScrDWFECPwpQto/GlOJYCUwVM/raQpAj
            /xvcjK5tNVzzK94mhk+j9RiQ+aWHaTXmOgurhxSp3YbwlRDvOgcq5yPiTz0+kSeK
            ZJcGeJ95bvJ+hJ/UMP0Zx2qB5PElZmiKvfiNqVUk8A8oxLJdBB5eCpqWV6CUqDKQ
            KSQP4sM0mZvQ1Sr4UcACVcYgYnCbTZMWhJTWkrNXqI8TMomekgny3y+d6NX/cFa6
            6jozFIF4HCX5aW8bp8C8vq2tFvFbleQ/Q3CU56EWWKMrOcpmFtRmC18s9biZBVR/
            8QIDAQAB
            -----END PUBLIC KEY-----
            """);
        return Convert.ToBase64String(
            rsa.Encrypt(
                Encoding.UTF8.GetBytes(value),
                RSAEncryptionPadding.Pkcs1));
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }

}

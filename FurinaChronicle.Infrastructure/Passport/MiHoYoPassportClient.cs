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
    public const string VerifySessionUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-session/app/verify";

    private const string PassportAppId = "bll8iq97cem8";
    private const string SessionAppId = "ddxf5dufpuyo";
    private const string AppVersion = "2.95.1";
    private const string OverseaAppVersion = "2.54.0";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly string overseaVerifierDeviceId = Guid.NewGuid().ToString("D");

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
                DisplayName: OptionalString(userInfo, "account_name")));
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
            cancellationToken);
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
            DisplayName: OptionalString(userInfo, "account_name"));
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
        using JsonDocument state = JsonDocument.Parse(challenge.State);
        string sessionId = RequiredString(state.RootElement, "session_id");
        byte[] proof = JsonSerializer.SerializeToUtf8Bytes(new
        {
            geetest_challenge = result.Challenge,
            geetest_validate = result.Validate,
            geetest_seccode = $"{result.Validate}|jordan"
        });
        return $"{sessionId};{Convert.ToBase64String(proof)}";
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
        CancellationToken cancellationToken)
    {
        string body = JsonSerializer.Serialize(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        ApplyPassportHeaders(request, body, device, aigis, useSessionApp);
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
        bool useSessionApp = false)
    {
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");
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
        request.Headers.TryAddWithoutValidation("x-rpc-sdk_version", "2.16.0");
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

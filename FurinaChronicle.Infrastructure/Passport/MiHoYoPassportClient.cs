using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class MiHoYoPassportClient : IMiHoYoPassportClient, IDisposable
{
    public const string CreateQrUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/createQRLogin";
    public const string QueryQrUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/queryQRLoginStatus";
    public const string CreateCaptchaUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-verifier/verifier/createLoginCaptcha";
    public const string MobileLoginUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-passport/app/loginByMobileCaptcha";
    public const string GetMultiTokenByLoginTicketUrl =
        "https://api-takumi.mihoyo.com/auth/api/getMultiTokenByLoginTicket";
    public const string UpgradeLegacySTokenUrl =
        "https://passport-api.mihoyo.com/account/ma-cn-session/app/getTokenBySToken";
    public const string OverseaPasswordLoginUrl =
        "https://sg-public-api.hoyoverse.com/account/ma-passport/api/appLoginByPassword";
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
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public MiHoYoPassportClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient
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

    public async Task<PassportLoginTokens> CompleteWebLoginAsync(
        string authenticatedCookie,
        string? loginResponseJson,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedCookie);
        ArgumentNullException.ThrowIfNull(device);

        Dictionary<string, string> cookieValues = ParseCookieValues(
            authenticatedCookie);
        WebLoginData? webLogin = ParseWebLoginResponse(loginResponseJson);
        string? aid = webLogin?.Tokens.Aid ?? First(
            cookieValues,
            "account_id_v2",
            "account_id",
            "login_uid",
            "stuid",
            "ltuid_v2",
            "ltuid");
        string? mid = webLogin?.Tokens.Mid ?? First(
            cookieValues,
            "mid",
            "account_mid_v2",
            "account_mid");
        string? sToken = webLogin?.Tokens.SToken ?? First(
            cookieValues,
            "stoken_v2",
            "stoken");
        string? lToken = webLogin?.Tokens.LToken ?? First(
            cookieValues,
            "ltoken_v2",
            "ltoken");
        string? cookieToken = webLogin?.Tokens.CookieToken ?? First(
            cookieValues,
            "cookie_token_v2",
            "cookie_token");
        string? loginTicket = webLogin?.LoginTicket ?? First(
            cookieValues,
            "login_ticket");

        if (!string.IsNullOrWhiteSpace(loginTicket) &&
            !string.IsNullOrWhiteSpace(aid) &&
            string.IsNullOrWhiteSpace(sToken))
        {
            LoginTicketTokens ticketTokens =
                await GetTokensByLoginTicketAsync(
                    loginTicket,
                    aid,
                    cancellationToken);
            sToken = ticketTokens.SToken;
            lToken ??= ticketTokens.LToken;
        }

        if (!string.IsNullOrWhiteSpace(sToken) &&
            !string.IsNullOrWhiteSpace(aid))
        {
            try
            {
                PassportLoginTokens upgraded = await UpgradeLegacySTokenAsync(
                    aid,
                    sToken,
                    device,
                    cancellationToken);
                return upgraded with
                {
                    LToken = lToken ?? upgraded.LToken,
                    CookieToken = cookieToken ?? upgraded.CookieToken,
                    DeviceFingerprint = First(cookieValues, "DEVICEFP")
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch when (!string.IsNullOrWhiteSpace(mid))
            {
                // Some web login variants already return a current SToken.
                // Keep it when the legacy-to-current exchange does not apply.
            }
        }

        if (string.IsNullOrWhiteSpace(aid) ||
            string.IsNullOrWhiteSpace(mid) ||
            string.IsNullOrWhiteSpace(sToken))
        {
            throw new FormatException(
                "米哈游登录页面尚未返回完整的 AID、MID 和 SToken。");
        }

        return new PassportLoginTokens(
            aid,
            mid,
            sToken,
            lToken,
            cookieToken,
            webLogin?.Tokens.DisplayName,
            First(cookieValues, "DEVICEFP"));
    }

    public async Task<PassportLoginTokens> LoginWithOverseaPasswordAsync(
        string account,
        string password,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentNullException.ThrowIfNull(device);
        string body = JsonSerializer.Serialize(new
        {
            account = EncryptOversea(account),
            password = EncryptOversea(password),
            token_type = 2
        });
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            OverseaPasswordLoginUrl)
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

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadResponseAsync(
            response,
            cancellationToken);
        JsonElement root = document.RootElement;
        int retcode = root.GetProperty("retcode").GetInt32();
        if (retcode != 0)
        {
            string message = OptionalString(root, "message") ?? "Unknown error";
            bool requiresSecurityVerification =
                response.Headers.Contains("X-Rpc-Aigis") ||
                response.Headers.Contains("X-Rpc-Verify");
            throw new InvalidOperationException(requiresSecurityVerification
                ? "HoYoLAB 要求额外的安全验证。请稍后重试，或先在官方 HoYoLAB 完成登录验证。"
                : $"HoYoLAB 登录失败 ({retcode}): {message}");
        }

        JsonElement data = root.GetProperty("data");
        JsonElement token = data.GetProperty("token");
        JsonElement userInfo = data.GetProperty("user_info");
        return new PassportLoginTokens(
            RequiredString(userInfo, "aid"),
            RequiredString(userInfo, "mid"),
            RequiredString(token, "token"),
            DisplayName: OptionalString(userInfo, "account_name"));
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
        if (account.LoginMethod == PassportLoginMethod.Password)
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
        using var request = new HttpRequestMessage(HttpMethod.Post, VerifySessionUrl)
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

    private async Task<LoginTicketTokens> GetTokensByLoginTicketAsync(
        string loginTicket,
        string aid,
        CancellationToken cancellationToken)
    {
        string url = GetMultiTokenByLoginTicketUrl +
            $"?login_ticket={Uri.EscapeDataString(loginTicket)}" +
            $"&uid={Uri.EscapeDataString(aid)}&token_types=3";
        using HttpResponseMessage response = await httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(
            response,
            cancellationToken);
        JsonElement data = document.RootElement.GetProperty("data");
        string? sToken = null;
        string? lToken = null;
        if (data.TryGetProperty("list", out JsonElement list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in list.EnumerateArray())
            {
                string? name = OptionalString(item, "name");
                string? token = OptionalString(item, "token");
                if (string.Equals(name, "stoken", StringComparison.OrdinalIgnoreCase))
                {
                    sToken = token;
                }
                else if (string.Equals(name, "ltoken", StringComparison.OrdinalIgnoreCase))
                {
                    lToken = token;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(sToken))
        {
            throw new InvalidDataException(
                "Login-ticket exchange did not return SToken.");
        }

        return new LoginTicketTokens(sToken, lToken);
    }

    private async Task<PassportLoginTokens> UpgradeLegacySTokenAsync(
        string aid,
        string sToken,
        PassportDeviceIdentity device,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            UpgradeLegacySTokenUrl);
        ApplyPassportHeaders(request, "{}", device, aigis: null);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"stuid={aid};stoken={sToken}");
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        using JsonDocument document = await ReadSuccessAsync(
            response,
            cancellationToken);
        JsonElement data = document.RootElement.GetProperty("data");
        JsonElement token = data.GetProperty("token");
        JsonElement userInfo = data.GetProperty("user_info");
        return new PassportLoginTokens(
            RequiredString(userInfo, "aid"),
            RequiredString(userInfo, "mid"),
            RequiredString(token, "token"),
            DisplayName: OptionalString(userInfo, "account_name"));
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
        var request = new HttpRequestMessage(HttpMethod.Post, url)
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
        CancellationToken cancellationToken)
    {
        JsonDocument document = await ReadResponseAsync(response, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        return await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
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

    private static WebLoginData? ParseWebLoginResponse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("retcode", out JsonElement retcode) ||
                retcode.GetInt32() != 0 ||
                !root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("user_info", out JsonElement userInfo) ||
                userInfo.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? aid = OptionalString(userInfo, "aid");
            if (string.IsNullOrWhiteSpace(aid))
            {
                return null;
            }

            string? sToken = null;
            string? lToken = null;
            string? cookieToken = null;
            if (data.TryGetProperty("token", out JsonElement token) &&
                token.ValueKind == JsonValueKind.Object)
            {
                string? value = OptionalString(token, "token");
                int type = token.TryGetProperty("token_type", out JsonElement tokenType) &&
                    tokenType.TryGetInt32(out int parsedType)
                    ? parsedType
                    : 0;
                switch (type)
                {
                    case 1:
                        sToken = value;
                        break;
                    case 2:
                        lToken = value;
                        break;
                    case 4:
                        cookieToken = value;
                        break;
                }
            }

            return new WebLoginData(
                new PassportLoginTokens(
                    aid,
                    OptionalString(userInfo, "mid"),
                    sToken,
                    lToken,
                    cookieToken,
                    OptionalString(userInfo, "account_name")),
                OptionalString(data, "login_ticket"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ParseCookieValues(string cookie)
    {
        return cookie
            .Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries)
            .Select(part => part.Split(
                '=',
                count: 2,
                StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length == 2 && parts[0].Length > 0)
            .GroupBy(parts => parts[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last()[1],
                StringComparer.OrdinalIgnoreCase);
    }

    private static string? First(
        IReadOnlyDictionary<string, string> values,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (values.TryGetValue(key, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
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

    private sealed record WebLoginData(
        PassportLoginTokens Tokens,
        string? LoginTicket);

    private sealed record LoginTicketTokens(
        string SToken,
        string? LToken);
}

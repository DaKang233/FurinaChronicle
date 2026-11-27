// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class MiHoYoAccountProfileClient :
    IMiHoYoAccountProfileClient,
    IDisposable
{
    /// <summary>The provider code for a session it does not recognise.</summary>
    public const int ProviderCodeAuthenticationRequired = -100;

    /// <summary>
    /// Current-account profile. It authenticates with the root SToken as well,
    /// so the display metadata never depends on the derived-token exchange.
    /// The account id is appended as a query parameter.
    /// </summary>
    public const string UserProfileUrl =
        "https://bbs-api.miyoushe.com/user/api/getUserFullInfo";

    /// <summary>
    /// Current-account role list. It authenticates with the root SToken only,
    /// so it keeps working when the derived-token exchange is refused and the
    /// cookie-token variant would answer -100.
    /// </summary>
    public const string GameRolesUrl =
        "https://api-takumi.miyoushe.com/binding/api/getUserGameRolesByStoken";

    /// <summary>
    /// Cookie-token role list. Kept as the fallback for sessions whose derived
    /// tokens are already known to be accepted.
    /// </summary>
    public const string CookieGameRolesUrl =
        "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";

    public const string OverseaUserProfileUrl =
        "https://bbs-api-os.hoyolab.com/community/painter/wapi/user/full";
    public const string OverseaGameRolesUrl =
        "https://api-os-takumi.hoyoverse.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_global";

    private const string AppVersion = "2.119.0";
    private const string OverseaAppVersion = "4.20.0";
    private const string AppReferrer = "https://app.mihoyo.com";
    private const string BbsReferrer = "https://bbs.mihoyo.com/";
    private const string BbsAndroidVersion = "15";
    private const string WebReferrer = "https://act.mihoyo.com/";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public MiHoYoAccountProfileClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate,
                // Authentication cookies come from the selected persisted
                // account and must not be replaced by handler state.
                UseCookies = false
            })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        ownsHttpClient = httpClient is null;
    }

    public async Task<PassportAccountProfile> GetAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        // Roles are the product-visible half of this call. Display metadata is
        // cosmetic and must never hide an otherwise usable role list, so a
        // refused profile read degrades instead of failing the sign-in.
        IReadOnlyList<PassportGameRole> roles =
            await GetGameRolesAsync(account, cancellationToken);
        ProfileMetadata metadata = await TryGetProfileMetadataAsync(
            account,
            cancellationToken);
        string displayName = metadata.DisplayName ??
            account.DisplayName ??
            (account.Realm == PassportRealm.Oversea
                ? $"HoYoLAB 用户 {account.Aid}"
                : $"米游社用户 {account.Aid}");
        return new PassportAccountProfile(
            displayName,
            metadata.AvatarUrl,
            roles);
    }

    public async Task<IReadOnlyList<PassportGameRole>> GetGameRolesAsync(
        PassportAccount account,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Realm == PassportRealm.Oversea)
        {
            using JsonDocument document = await SendAsync(
                CreateOverseaRequest(
                    OverseaGameRolesUrl,
                    PassportCookieComposer.ForSession(account),
                    account),
                cancellationToken);
            JsonElement overseaRoot = document.RootElement;
            EnsureSuccess(overseaRoot, "game roles");
            return ParseGameRoles(RequireRoleData(overseaRoot), isOversea: true);
        }

        // The root session is the credential that always exists right after a
        // login. Ask with it first so a refused derived-token exchange cannot
        // turn a successful sign-in into a -100.
        PassportProfileRequestException? primary = null;
        if (!string.IsNullOrWhiteSpace(account.Credentials.SToken))
        {
            try
            {
                using JsonDocument document = await SendAsync(
                    CreateMainlandSTokenRequest(GameRolesUrl, account),
                    cancellationToken);
                JsonElement root = document.RootElement;
                EnsureSuccess(root, "game roles");
                return ParseGameRoles(RequireRoleData(root), isOversea: false);
            }
            catch (PassportProfileRequestException exception)
            {
                primary = exception;
            }
        }

        if (account.Credentials.LToken is { } lToken &&
            PassportCookieComposer.IsHeaderToken(lToken))
        {
            try
            {
                using JsonDocument document = await SendAsync(
                    CreateMainlandCookieRequest(CookieGameRolesUrl, account),
                    cancellationToken);
                JsonElement root = document.RootElement;
                EnsureSuccess(root, "game roles");
                return ParseGameRoles(RequireRoleData(root), isOversea: false);
            }
            catch (PassportProfileRequestException exception)
            {
                throw primary ?? exception;
            }
        }

        throw primary ?? new PassportProfileRequestException(
            "game roles",
            ProviderCodeAuthenticationRequired);
    }

    private async Task<ProfileMetadata> TryGetProfileMetadataAsync(
        PassportAccount account,
        CancellationToken cancellationToken)
    {
        try
        {
            HttpRequestMessage request = account.Realm == PassportRealm.Oversea
                ? CreateOverseaRequest(
                    OverseaUserProfileUrl,
                    PassportCookieComposer.ForSession(account),
                    account)
                : CreateMainlandProfileRequest(account);
            using JsonDocument document = await SendAsync(
                request,
                cancellationToken);
            JsonElement root = document.RootElement;
            EnsureSuccess(root, "profile");
            if (!root.TryGetProperty("data", out JsonElement data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("user_info", out JsonElement userInfo) ||
                userInfo.ValueKind != JsonValueKind.Object)
            {
                return default;
            }

            Uri? avatarUrl = Uri.TryCreate(
                OptionalString(userInfo, "avatar_url"),
                UriKind.Absolute,
                out Uri? parsedAvatar)
                ? parsedAvatar
                : null;
            return new ProfileMetadata(
                OptionalString(userInfo, "nickname"),
                avatarUrl);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Best effort: the caller falls back to the stored or AID display
            // name. A refused or malformed profile must not fail the sign-in.
            return default;
        }
    }

    private HttpRequestMessage CreateMainlandSTokenRequest(
        string url,
        PassportAccount account)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            PassportCookieComposer.ForSTokenSession(account));
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "2");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");
        request.Headers.Referrer = new Uri(AppReferrer);
        ApplyDeviceHeaders(request, account);
        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDs(MiHoYoRequestSigning.Cnk2Salt));
        return request;
    }

    private HttpRequestMessage CreateMainlandProfileRequest(
        PassportAccount account)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{UserProfileUrl}?uid={Uri.EscapeDataString(account.Aid)}");
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            PassportCookieComposer.ForSTokenSession(account));
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Mozilla/5.0 (Linux; Android {BbsAndroidVersion}) Mobile miHoYoBBS/{AppVersion}");
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "2");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.Referrer = new Uri(BbsReferrer);
        ApplyDeviceHeaders(request, account);
        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDs(MiHoYoRequestSigning.Cnk2Salt));
        return request;
    }

    private HttpRequestMessage CreateMainlandCookieRequest(
        string url,
        PassportAccount account)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            PassportCookieComposer.ForSession(account));
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");
        request.Headers.Referrer = new Uri(WebReferrer);
        request.Headers.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");
        ApplyDeviceHeaders(request, account);
        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDs(
                MiHoYoRequestSigning.Cnx4Salt,
                numericNonce: true));
        return request;
    }

    private HttpRequestMessage CreateOverseaRequest(
        string url,
        string cookie,
        PassportAccount account)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-app_version",
            OverseaAppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-language", "zh-cn");
        request.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBSOversea/{OverseaAppVersion}");
        ApplyDeviceHeaders(request, account);
        return request;
    }

    private static void ApplyDeviceHeaders(
        HttpRequestMessage request,
        PassportAccount account)
    {
        request.Headers.TryAddWithoutValidation(
            "x-rpc-device_id",
            account.Device.DeviceId.Replace(
                "-",
                string.Empty,
                StringComparison.Ordinal));
        if (PassportCookieComposer.IsHeaderToken(
            account.Device.DeviceFingerprint))
        {
            request.Headers.TryAddWithoutValidation(
                "x-rpc-device_fp",
                account.Device.DeviceFingerprint);
        }
    }

    private async Task<JsonDocument> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using (request)
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(
                cancellationToken);
            return await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
        }
    }

    private static JsonElement RequireRoleData(JsonElement root)
    {
        if (!root.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("list", out JsonElement list) ||
            list.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("Missing game role list.");
        }

        return data;
    }

    private static IReadOnlyList<PassportGameRole> ParseGameRoles(
        JsonElement rolesData,
        bool isOversea)
    {
        var roles = new List<PassportGameRole>();
        if (rolesData.TryGetProperty("list", out JsonElement list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement role in list.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.Object)
                    throw new JsonException("Invalid game role.");
                string? uid = OptionalString(role, "game_uid");
                if (string.IsNullOrWhiteSpace(uid))
                {
                    continue;
                }

                roles.Add(new PassportGameRole(
                    OptionalString(role, "game_biz") ??
                        (isOversea ? "hk4e_global" : "hk4e_cn"),
                    OptionalString(role, "region") ?? string.Empty,
                    uid,
                    OptionalString(role, "nickname") ?? uid,
                    OptionalInt(role, "level"),
                    OptionalString(role, "region_name") ?? string.Empty));
            }
        }

        return roles;
    }

    private static void EnsureSuccess(JsonElement root, string operation)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("retcode", out JsonElement code) ||
            code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int retcode))
            throw new JsonException("Missing provider status.");
        if (retcode != 0)
        {
            throw new PassportProfileRequestException(operation, retcode);
        }
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

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : property.ToString();
    }

    private static int OptionalInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return 0;
        }

        return property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int value)
            ? value
            : int.TryParse(property.ToString(), out value) ? value : 0;
    }

    private readonly record struct ProfileMetadata(
        string? DisplayName,
        Uri? AvatarUrl);

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}

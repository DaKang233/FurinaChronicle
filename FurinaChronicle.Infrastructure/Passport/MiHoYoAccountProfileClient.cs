using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.Passport;

public sealed class MiHoYoAccountProfileClient :
    IMiHoYoAccountProfileClient,
    IDisposable
{
    public const string UserProfileUrl =
        "https://bbs-api.miyoushe.com/user/wapi/getUserFullInfo";
    public const string GameRolesUrl =
        "https://api-takumi.mihoyo.com/binding/api/getUserGameRolesByCookie?game_biz=hk4e_cn";

    private const string DsSalt = "xV8v4Qu54lUKrEYFZkJhB8cuoh9NXmz9";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public MiHoYoAccountProfileClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip | DecompressionMethods.Deflate
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
        string cookie = BuildCookie(account);
        Task<JsonDocument> profileTask = GetDocumentAsync(
            UserProfileUrl,
            cookie,
            account,
            cancellationToken);
        Task<JsonDocument> rolesTask = GetDocumentAsync(
            GameRolesUrl,
            cookie,
            account,
            cancellationToken);

        await Task.WhenAll(profileTask, rolesTask);
        using JsonDocument profileDocument = await profileTask;
        using JsonDocument rolesDocument = await rolesTask;
        JsonElement profileRoot = profileDocument.RootElement;
        JsonElement rolesRoot = rolesDocument.RootElement;
        EnsureSuccess(profileRoot, "profile");
        EnsureSuccess(rolesRoot, "game roles");

        JsonElement userInfo = profileRoot.GetProperty("data")
            .GetProperty("user_info");
        string displayName = OptionalString(userInfo, "nickname")
            ?? account.DisplayName
            ?? $"米游社用户 {account.Aid}";
        Uri? avatarUrl = Uri.TryCreate(
            OptionalString(userInfo, "avatar_url"),
            UriKind.Absolute,
            out Uri? parsedAvatar)
            ? parsedAvatar
            : null;

        var roles = new List<PassportGameRole>();
        JsonElement rolesData = rolesRoot.GetProperty("data");
        if (rolesData.TryGetProperty("list", out JsonElement list) &&
            list.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement role in list.EnumerateArray())
            {
                string? uid = OptionalString(role, "game_uid");
                if (string.IsNullOrWhiteSpace(uid))
                {
                    continue;
                }

                roles.Add(new PassportGameRole(
                    OptionalString(role, "game_biz") ?? "hk4e_cn",
                    OptionalString(role, "region") ?? string.Empty,
                    uid,
                    OptionalString(role, "nickname") ?? uid,
                    OptionalInt(role, "level"),
                    OptionalString(role, "region_name") ?? string.Empty));
            }
        }

        return new PassportAccountProfile(displayName, avatarUrl, roles);
    }

    private async Task<JsonDocument> GetDocumentAsync(
        string url,
        string cookie,
        PassportAccount account,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Cookie", cookie);
        request.Headers.TryAddWithoutValidation("DS", CreateDs());
        request.Headers.TryAddWithoutValidation(
            "x-rpc-device_id",
            account.Device.DeviceId.Replace("-", string.Empty, StringComparison.Ordinal));
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", "2.95.1");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/2.95.1");
        request.Headers.Referrer = new Uri("https://act.mihoyo.com/");
        request.Headers.TryAddWithoutValidation("Origin", "https://act.mihoyo.com");

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

    private static string BuildCookie(PassportAccount account)
    {
        var pairs = new List<string>
        {
            $"account_id={account.Aid}",
            $"ltuid={account.Aid}",
            $"stuid={account.Aid}"
        };
        Add(pairs, "mid", account.Mid);
        Add(pairs, "stoken", account.Credentials.SToken);
        Add(pairs, "ltoken", account.Credentials.LToken);
        Add(pairs, "cookie_token", account.Credentials.CookieToken);
        return string.Join(';', pairs);
    }

    private static void Add(
        ICollection<string> pairs,
        string name,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            pairs.Add($"{name}={value}");
        }
    }

    private static string CreateDs()
    {
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int random = Random.Shared.Next(100000, 200000);
        string source = $"salt={DsSalt}&t={timestamp}&r={random}";
        string checksum = Convert.ToHexString(
            MD5.HashData(Encoding.UTF8.GetBytes(source)))
            .ToLowerInvariant();
        return $"{timestamp},{random},{checksum}";
    }

    private static void EnsureSuccess(JsonElement root, string operation)
    {
        int retcode = root.GetProperty("retcode").GetInt32();
        if (retcode != 0)
        {
            string message = OptionalString(root, "message") ?? "Unknown error";
            throw new InvalidOperationException(
                $"MiHoYo {operation} request failed ({retcode}): {message}");
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

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}

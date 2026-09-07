using System.Net.Http.Json;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.Gacha.Refreshing;

namespace FurinaChronicle.Infrastructure.Gacha.Refreshing;

public sealed class MiHoYoSTokenGachaUrlProvider : ISTokenGachaUrlProvider, IDisposable
{
    public const string GenerateAuthKeyUrl =
        "https://api-takumi.mihoyo.com/binding/api/genAuthKey";

    private const string AppVersion = "2.95.1";
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;

    public MiHoYoSTokenGachaUrlProvider(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient(
            new HttpClientHandler
            {
                // The account cookie is supplied explicitly for every request.
                // AndroidMessageHandler otherwise retains Set-Cookie values from
                // genAuthKey and can replace the persisted SToken on later calls.
                UseCookies = false
            })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        ownsHttpClient = httpClient is null;
    }

    public async Task<Uri> CreateAsync(
        PassportAccount passportAccount,
        GameAccount gameAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passportAccount);
        ArgumentNullException.ThrowIfNull(gameAccount);
        if (passportAccount.Realm != PassportRealm.MainlandChina)
        {
            throw new NotSupportedException(
                "SToken gacha refresh currently supports mainland China passport accounts only.");
        }

        string sToken = passportAccount.Credentials.SToken
            ?? throw new InvalidOperationException("Passport account does not contain SToken.");
        string mid = passportAccount.Mid
            ?? throw new InvalidOperationException("Passport account does not contain mid.");
        string region = GetChineseRegion(gameAccount.ServerRegion);
        if (!long.TryParse(gameAccount.Uid, out long gameUid))
        {
            throw new ArgumentException(
                "Game account UID must be numeric.",
                nameof(gameAccount));
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            GenerateAuthKeyUrl)
        {
            Content = JsonContent.Create(new
            {
                auth_appid = "webview_gacha",
                game_biz = "hk4e_cn",
                game_uid = gameUid,
                region
            })
        };
        request.Headers.TryAddWithoutValidation(
            "Cookie",
            $"stuid={passportAccount.Aid};stoken={sToken};mid={mid}");
        request.Headers.TryAddWithoutValidation(
            "DS",
            MiHoYoRequestSigning.CreateDs(MiHoYoRequestSigning.Lk2Salt));
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation(
            "x-rpc-device_id",
            passportAccount.Device.DeviceId.Replace("-", string.Empty, StringComparison.Ordinal));
        request.Headers.Referrer = new Uri("https://app.mihoyo.com");
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");

        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        int retcode = root.GetProperty("retcode").GetInt32();
        if (retcode != 0)
        {
            string message = root.TryGetProperty("message", out JsonElement messageElement)
                ? messageElement.GetString() ?? "Unknown error"
                : "Unknown error";
            throw new InvalidOperationException(
                $"MiHoYo genAuthKey failed ({retcode}): {message}");
        }

        JsonElement data = root.GetProperty("data");
        string authKey = data.GetProperty("authkey")
            .GetString()
            ?? throw new InvalidDataException("MiHoYo genAuthKey response did not contain authkey.");
        int authKeyVersion = GetOptionalInt(data, "authkey_ver", 1);
        int signType = GetOptionalInt(data, "sign_type", 2);

        return new Uri(
            "https://public-operation-hk4e.mihoyo.com/gacha_info/api/getGachaLog" +
            $"?auth_appid=webview_gacha&authkey={Uri.EscapeDataString(authKey)}" +
            $"&authkey_ver={authKeyVersion}&sign_type={signType}" +
            $"&game_biz=hk4e_cn&region={region}&lang=zh-cn");
    }

    private static string GetChineseRegion(GameServerRegion region)
    {
        return region switch
        {
            GameServerRegion.ChinaOfficial => "cn_gf01",
            GameServerRegion.ChinaBilibili => "cn_qd01",
            _ => throw new NotSupportedException(
                "SToken gacha refresh currently supports mainland China Genshin accounts only.")
        };
    }

    private static int GetOptionalInt(
        JsonElement element,
        string propertyName,
        int fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement property))
        {
            return fallback;
        }

        if (property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out int numeric))
        {
            return numeric;
        }

        return int.TryParse(property.ToString(), out int parsed)
            ? parsed
            : fallback;
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient.Dispose();
        }
    }
}

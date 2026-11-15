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

/// <summary>
/// Live character and equipped-weapon transport over the Game Record service.
/// It is deliberately single-attempt: a duplicate risky request is worse than
/// reporting the first failure, and the caller may retry explicitly.
/// </summary>
public sealed class HoYoGameRecordCharacterWeaponSource
    : ICharacterWeaponSource, IDisposable
{
    private const string Host = "https://api-takumi-record.mihoyo.com";
    private const string OverseaHost = "https://bbs-api-os.hoyolab.com";
    private const string CharacterPath = "/game_record/app/genshin/api/character/list";
    private const string OverseaAppVersion = "2.54.0";
    private const string AppVersion = "2.115.0";
    private const string GameRecordSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string OverseaGameRecordSalt = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly MiYouSheCharacterWeaponResponseReader reader;

    public HoYoGameRecordCharacterWeaponSource(
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        ownsClient = httpClient is null;
        this.httpClient = httpClient ?? new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate,
        });
        reader = new MiYouSheCharacterWeaponResponseReader(timeProvider);
    }

    public async Task<CharacterWeaponSourceResult> FetchAsync(
        PassportAccount passport,
        GameRoleNaturalIdentity role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(passport);
        cancellationToken.ThrowIfCancellationRequested();
        bool isOversea = passport.Realm == PassportRealm.Oversea;
        bool supported = isOversea
            ? role.GameBiz == GenshinGameRoleIdentity.GlobalGameBiz &&
                role.Server is "os_usa" or "os_euro" or "os_asia" or "os_cht"
            : role.GameBiz == GenshinGameRoleIdentity.MainlandGameBiz &&
                role.Server is "cn_gf01" or "cn_qd01";
        if (!supported)
        {
            return new(CharacterWeaponCollectionStatus.UnsupportedRealm);
        }

        if (passport.Credentials.LToken is not { } token ||
            !IsHeaderToken(token) || !passport.Aid.All(char.IsAsciiDigit))
        {
            return new(CharacterWeaponCollectionStatus.AuthenticationRequired);
        }

        string body = JsonSerializer.Serialize(new
        {
            role_id = role.Uid,
            server = role.Server,
            sort_type = 1,
        });
        string cookie = BuildCookie(passport, token, isOversea);
        using var request = CreateRequest(
            body, passport, cookie, isOversea);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            return await reader.ReadAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(CharacterWeaponCollectionStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(CharacterWeaponCollectionStatus.Unavailable);
        }
    }

    private static string BuildCookie(
        PassportAccount passport,
        string token,
        bool isOversea)
    {
        string suffix = isOversea ? "_v2" : string.Empty;
        string cookie = $"ltuid{suffix}={passport.Aid}; ltoken{suffix}={token}";
        if (passport.Credentials.CookieToken is { } cookieToken &&
            IsHeaderToken(cookieToken))
        {
            cookie += $"; account_id{suffix}={passport.Aid}; cookie_token{suffix}={cookieToken}";
        }
        if (isOversea && passport.Mid is { } mid && IsHeaderToken(mid))
        {
            cookie += $"; account_mid_v2={mid}";
        }
        return cookie;
    }

    private static HttpRequestMessage CreateRequest(
        string body,
        PassportAccount passport,
        string cookie,
        bool isOversea)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            (isOversea ? OverseaHost : Host) + CharacterPath)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
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
        if (passport.Device.DeviceFingerprint is { } fingerprint &&
            IsHeaderToken(fingerprint))
        {
            request.Headers.Add("x-rpc-device_fp", fingerprint);
        }

        // The reference client signs the request URI only for this POST; the
        // JSON body is not part of the Gen 2 checksum.
        request.Headers.Add(
            "DS",
            MiHoYoRequestSigning.CreateDsGen2(
                isOversea ? OverseaGameRecordSalt : GameRecordSalt,
                body: string.Empty,
                query: string.Empty,
                numericNonce: true));
        request.Headers.Add("Cookie", cookie);
        return request;
    }

    private static bool IsHeaderToken(string value) =>
        value.Length > 0 && value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'));

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }
}

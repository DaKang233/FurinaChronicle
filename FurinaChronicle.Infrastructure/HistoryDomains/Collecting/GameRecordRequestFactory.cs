// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Shared request shape for the Game Record endpoints. Only endpoints that
/// authenticate with a passport cookie and sign their query are accepted here.
/// </summary>
internal static class GameRecordRequestFactory
{
    internal const string Host = "https://api-takumi-record.mihoyo.com";
    internal const string OverseaHost = "https://bbs-api-os.hoyolab.com";
    internal const string AppVersion = "2.115.0";
    internal const string OverseaAppVersion = "2.54.0";
    internal const string GameRecordSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    internal const string OverseaGameRecordSalt =
        "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";

    public static HttpRequestMessage Create(
        HttpMethod method,
        bool isOversea,
        string path,
        string query,
        string? body,
        PassportAccount passport)
    {
        ArgumentNullException.ThrowIfNull(passport);
        var request = new HttpRequestMessage(
            method,
            (isOversea ? OverseaHost : Host) + path +
            (query.Length > 0 ? "?" + query : string.Empty));
        if (body is not null)
        {
            request.Content = new StringContent(
                body, Encoding.UTF8, "application/json");
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
        if (passport.Device.DeviceFingerprint is { } fingerprint &&
            IsHeaderToken(fingerprint))
        {
            request.Headers.Add("x-rpc-device_fp", fingerprint);
        }

        request.Headers.Add(
            "DS",
            MiHoYoRequestSigning.CreateDsGen2(
                isOversea ? OverseaGameRecordSalt : GameRecordSalt,
                body: string.Empty,
                query: query,
                numericNonce: true));
        request.Headers.Add("Cookie", BuildCookie(passport, isOversea));
        return request;
    }

    public static string RoleQuery(string roleId, string server) =>
        $"role_id={roleId}&server={server}";

    public static bool IsHeaderToken(string value) =>
        value.Length > 0 && value.All(character =>
            character is >= '!' and <= '~' and not ('"' or ',' or ';' or '\\'));

    private static string BuildCookie(
        PassportAccount passport,
        bool isOversea)
    {
        if (passport.Credentials.LToken is not { } token ||
            !IsHeaderToken(token))
        {
            throw new ArgumentException(
                "The passport has no usable LToken.", nameof(passport));
        }

        string suffix = isOversea ? "_v2" : string.Empty;
        string cookie = $"ltuid{suffix}={passport.Aid}; ltoken{suffix}={token}";
        if (passport.Credentials.CookieToken is { } cookieToken &&
            IsHeaderToken(cookieToken))
        {
            cookie +=
                $"; account_id{suffix}={passport.Aid}; cookie_token{suffix}={cookieToken}";
        }
        if (isOversea && passport.Mid is { } mid && IsHeaderToken(mid))
        {
            cookie += $"; account_mid_v2={mid}";
        }
        return cookie;
    }
}

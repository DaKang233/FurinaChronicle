// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Infrastructure.MiHoYo;
using FurinaChronicle.Services.HistoryDomains.Collecting;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

public sealed class MiYouSheSpiralAbyssSource : ISpiralAbyssSource, IDisposable
{
    private const string Endpoint =
        "https://api-takumi-record.mihoyo.com/game_record/app/genshin/api/spiralAbyss";
    private const string AppVersion = "2.95.1";
    private const string GameRecordSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly MiYouSheSpiralAbyssResponseReader reader;

    public MiYouSheSpiralAbyssSource(TimeProvider timeProvider, HttpClient? httpClient = null)
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

    public async Task<SpiralAbyssSourceResult> FetchCurrentAsync(
        PassportAccount passport, GameRoleNaturalIdentity role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (passport.Realm != PassportRealm.MainlandChina ||
            role.GameBiz != GenshinGameRoleIdentity.MainlandGameBiz ||
            role.Server is not ("cn_gf01" or "cn_qd01"))
        {
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }

        if (passport.Credentials.LToken is not { } token ||
            !IsCookieValue(token) || !passport.Aid.All(char.IsAsciiDigit))
        {
            return new(SpiralAbyssCollectionStatus.AuthenticationRequired);
        }

        string query = $"role_id={role.Uid}&schedule_type=1&server={role.Server}";
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}?{query}");
        request.Headers.Referrer = new Uri("https://webstatic.mihoyo.com/");
        request.Headers.TryAddWithoutValidation("x-rpc-client_type", "5");
        request.Headers.TryAddWithoutValidation("x-rpc-app_version", AppVersion);
        request.Headers.UserAgent.ParseAdd(
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) miHoYoBBS/{AppVersion}");
        request.Headers.Add("x-rpc-device_id", passport.Device.DeviceId);
        if (passport.Device.DeviceFingerprint is { } fingerprint && IsCookieValue(fingerprint))
        {
            request.Headers.TryAddWithoutValidation("x-rpc-device_fp", fingerprint);
        }
        request.Headers.TryAddWithoutValidation("DS",
            MiHoYoRequestSigning.CreateDsGen2(GameRecordSalt, query: query, numericNonce: true));
        string cookie = $"ltuid={passport.Aid}; ltoken={token}";
        if (passport.Credentials.CookieToken is { } cookieToken && IsCookieValue(cookieToken))
        {
            cookie += $"; account_id={passport.Aid}; cookie_token={cookieToken}";
        }
        request.Headers.Add("Cookie", cookie);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return await reader.ReadAsync(response, role, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable);
        }
    }

    private static bool IsCookieValue(string value) =>
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

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Inventory;
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
    private const string DetailPath = "/game_record/app/genshin/api/character/detail";
    private const int DetailBatchSize = 32;
    private const string OverseaAppVersion = "2.54.0";
    private const string AppVersion = "2.115.0";
    private const string GameRecordSalt = "xV8v4Qu54lUKrEYFZkJhB8cuOh9Asafs";
    private const string OverseaGameRecordSalt = "h4c1d6ywfq5bsbnbhm1bzq7bxzzv6srt";
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly MiYouSheCharacterWeaponResponseReader reader;
    private readonly MiYouSheCharacterDetailResponseReader detailReader = new();
    private readonly TimeProvider timeProvider;

    public HoYoGameRecordCharacterWeaponSource(
        TimeProvider timeProvider,
        HttpClient? httpClient = null)
    {
        this.timeProvider = timeProvider;
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
            CharacterPath, body, passport, cookie, isOversea);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            CharacterWeaponSourceResult rosterResult = await reader.ReadAsync(response, timeout.Token);
            if (isOversea || rosterResult.Status != CharacterWeaponCollectionStatus.Received)
                return rosterResult;

            return await FetchDetailsAsync(passport, role, cookie, rosterResult, cancellationToken);
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

    private async Task<CharacterWeaponSourceResult> FetchDetailsAsync(
        PassportAccount passport, GameRoleNaturalIdentity role, string cookie,
        CharacterWeaponSourceResult rosterResult, CancellationToken cancellationToken)
    {
        if (rosterResult.Snapshot is not { } snapshot || rosterResult.Roster is not { Count: > 0 and <= 400 } roster)
            return new(CharacterWeaponCollectionStatus.InvalidResponse);

        var characters = new List<InventoryStateEntry>(roster.Count);
        CharacterResponseMetadata? metadata = null;
        foreach (int[] batch in roster.Keys.Chunk(DetailBatchSize))
        {
            string body = JsonSerializer.Serialize(new
            {
                role_id = role.Uid,
                server = role.Server,
                sort_type = 1,
                character_ids = batch,
            });
            using var request = CreateRequest(DetailPath, body, passport, cookie, isOversea: false);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            using HttpResponseMessage response = await httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            var result = await detailReader.ReadAsync(response, deadline.Token);
            if (result.Status != CharacterWeaponCollectionStatus.Received)
                return new(result.Status, RetryAfter: result.RetryAfter,
                    HttpStatus: result.HttpStatus, ProviderCode: result.ProviderCode);

            if (result.Entries is not { } entries || !batch.ToHashSet().SetEquals(entries.Keys) ||
                !TryMergeMetadata(ref metadata, result.Metadata))
                return new(CharacterWeaponCollectionStatus.InvalidResponse, HttpStatus: result.HttpStatus);

            foreach (int id in batch)
            {
                var entry = entries[id];
                characters.Add(new(InventoryItemKind.Character, id.ToString(CultureInfo.InvariantCulture),
                    instanceId: null, quantity: 1, entry.Level, entry.ConstellationCount,
                    InventoryFieldCoverage.Level | InventoryFieldCoverage.Progression,
                    FurinaChronicle.Core.Records.DataCompleteness.Complete,
                    entry.Detail with { Roster = roster[id], Ascension = entry.Detail.Ascension ?? roster[id].Ascension }));
            }
        }

        return rosterResult with
        {
            Roster = null,
            Snapshot = snapshot with
            {
                Characters = characters,
                CharacterMetadata = metadata,
                FetchedAt = timeProvider.GetUtcNow(),
            },
        };
    }

    private static bool TryMergeMetadata(ref CharacterResponseMetadata? current, CharacterResponseMetadata? incoming)
    {
        if (incoming is null) return true;
        if (current is null)
        {
            current = incoming;
            return true;
        }
        if (!TryMerge(current.Properties, incoming.Properties, out var properties) ||
            !TryMerge(current.ArtifactWiki, incoming.ArtifactWiki, out var artifacts) ||
            !TryMerge(current.WeaponWiki, incoming.WeaponWiki, out var weapons) ||
            !TryMerge(current.AvatarWiki, incoming.AvatarWiki, out var avatars) ||
            current.ArtifactPropertyOptions is { } existing && incoming.ArtifactPropertyOptions is { } options &&
                JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(options))
            return false;

        current = new(properties, current.ArtifactPropertyOptions ?? incoming.ArtifactPropertyOptions,
            artifacts, weapons, avatars);
        return true;
    }

    private static bool TryMerge<T>(IReadOnlyDictionary<string, T>? first, IReadOnlyDictionary<string, T>? second,
        out IReadOnlyDictionary<string, T>? result)
    {
        result = first ?? second;
        if (first is null || second is null) return true;
        var merged = new SortedDictionary<string, T>(StringComparer.Ordinal);
        foreach (var pair in first) merged.Add(pair.Key, pair.Value);
        foreach (var pair in second)
        {
            if (merged.TryGetValue(pair.Key, out var existing) &&
                !EqualityComparer<T>.Default.Equals(existing, pair.Value))
                return false;
            merged[pair.Key] = pair.Value;
        }
        result = merged;
        return true;
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
        string path,
        string body,
        PassportAccount passport,
        string cookie,
        bool isOversea)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            (isOversea ? OverseaHost : Host) + path)
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

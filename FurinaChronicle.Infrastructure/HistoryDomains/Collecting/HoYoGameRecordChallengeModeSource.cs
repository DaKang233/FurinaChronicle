// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Infrastructure.HistoryDomains.Collecting;

/// <summary>
/// Live transport for the theater and the Stygian onslaught. Both use the same
/// Game Record host and cookie but a different path, query and reader.
/// </summary>
public sealed class HoYoGameRecordChallengeModeSource
    : IChallengeModeSource, IDisposable
{
    private const string RoleCombatPath =
        "/game_record/app/genshin/api/role_combat";
    private const string HardChallengePath =
        "/game_record/app/genshin/api/hard_challenge";
    private readonly HttpClient httpClient;
    private readonly bool ownsClient;
    private readonly MiYouSheRoleCombatResponseReader roleCombat;
    private readonly MiYouSheHardChallengeResponseReader hardChallenge;

    public HoYoGameRecordChallengeModeSource(
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
        roleCombat = new MiYouSheRoleCombatResponseReader(timeProvider);
        hardChallenge = new MiYouSheHardChallengeResponseReader(timeProvider);
    }

    public async Task<ChallengeModeSourceResult> FetchAsync(
        ChallengeMode mode,
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
            return new(SpiralAbyssCollectionStatus.UnsupportedRealm);
        }
        if (passport.Credentials.LToken is not { } token ||
            !GameRecordRequestFactory.IsHeaderToken(token))
        {
            return new(SpiralAbyssCollectionStatus.AuthenticationRequired);
        }

        (string path, string query) = mode switch
        {
            ChallengeMode.ImaginariumTheater => (
                RoleCombatPath,
                GameRecordRequestFactory.RoleQuery(role.Uid, role.Server) +
                "&need_detail=true&active=1"),
            ChallengeMode.StygianOnslaught => (
                HardChallengePath,
                GameRecordRequestFactory.RoleQuery(role.Uid, role.Server) +
                "&need_detail=true"),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        using var request = GameRecordRequestFactory.Create(
            HttpMethod.Get, isOversea, path, query, body: null, passport);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            return mode == ChallengeMode.ImaginariumTheater
                ? await roleCombat.ReadAsync(response, timeout.Token)
                : await hardChallenge.ReadAsync(response, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable);
        }
        catch (HttpRequestException)
        {
            return new(SpiralAbyssCollectionStatus.Unavailable);
        }
        catch (ArgumentException)
        {
            return new(SpiralAbyssCollectionStatus.AuthenticationRequired);
        }
    }

    public void Dispose()
    {
        if (ownsClient)
        {
            httpClient.Dispose();
        }
    }
}

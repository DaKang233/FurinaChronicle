// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class HoYoGameRecordCharacterWeaponSourceTests
{
    private const string RosterBody = """
        {
          "retcode": 0,
          "data": {
            "list": [
              {
                "id": 10000093,
                "level": 90,
                "actived_constellation_num": 0,
                "weapon": { "id": 11501, "level": 90, "affix_level": 5 }
              },
              {
                "id": 10000103,
                "level": 80,
                "actived_constellation_num": 2,
                "weapon": { "id": 11501, "level": 80, "affix_level": 1 }
              }
            ]
          }
        }
        """;

    private static readonly GameRoleNaturalIdentity Role =
        new("hk4e_cn", "cn_gf01", "100000001");

    [Fact]
    public async Task PostsRosterAndMapsItIntoTwoScopes()
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("api-takumi-record.mihoyo.com", request.RequestUri!.Host);
            Assert.Equal(
                "/game_record/app/genshin/api/character/list",
                request.RequestUri.AbsolutePath);
            Assert.Equal(string.Empty, request.RequestUri.Query);
            Assert.Equal("5", Assert.Single(request.Headers.GetValues("x-rpc-client_type")));
            Assert.Equal("2.115.0", Assert.Single(request.Headers.GetValues("x-rpc-app_version")));
            Assert.Equal(
                "fixture-device",
                Assert.Single(request.Headers.GetValues("x-rpc-device_id")));
            Assert.Equal(
                "fixture-fingerprint",
                Assert.Single(request.Headers.GetValues("x-rpc-device_fp")));
            Assert.Equal(
                "ltuid=1111; ltoken=fixture; account_id=1111; cookie_token=fixture-cookie",
                Assert.Single(request.Headers.GetValues("Cookie")));
            string[] signature = Assert.Single(request.Headers.GetValues("DS")).Split(',');
            Assert.Equal(3, signature.Length);
            Assert.Matches("^[0-9]{6}$", signature[1]);
            Assert.Equal(
                "application/json",
                request.Content!.Headers.ContentType!.MediaType);
            string body = request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Assert.Contains("\"role_id\":\"100000001\"", body);
            Assert.Contains("\"server\":\"cn_gf01\"", body);
            Assert.Contains("\"sort_type\":1", body);
            return Json(RosterBody);
        }));
        using var source = new HoYoGameRecordCharacterWeaponSource(
            TimeProvider.System, client);

        CharacterWeaponSourceResult result = await source.FetchAsync(
            Passport(), Role);

        Assert.Equal(CharacterWeaponCollectionStatus.Received, result.Status);
        Assert.Equal(200, result.HttpStatus);
        CharacterWeaponSnapshot snapshot = Assert.IsType<CharacterWeaponSnapshot>(
            result.Snapshot);
        Assert.Equal(2, snapshot.Characters.Count);
        Assert.Equal(DataCompleteness.Complete, snapshot.CharacterCompleteness);
        Assert.Equal(DataCompleteness.Partial, snapshot.WeaponCompleteness);
        InventoryStateEntry duplicated = Assert.Single(
            snapshot.Weapons,
            entry => entry.TypeId == "11501");
        Assert.Equal(2, duplicated.Quantity);
        Assert.Null(duplicated.Level);
        Assert.Null(duplicated.Progression);
        Assert.Equal(InventoryFieldCoverage.Quantity, duplicated.FieldCoverage);
    }

    [Fact]
    public async Task OverseasUsesItsOwnHostVersionAndCookieSuffix()
    {
        var role = new GameRoleNaturalIdentity("hk4e_global", "os_asia", "800000001");
        var passport = new PassportAccount(
            Guid.NewGuid(), "1111", "fixture-mid", null,
            PassportLoginMethod.OverseaPassword,
            new PassportCredentials(
                null, "fixture", "fixture-cookie", null, null, null, null),
            new PassportDeviceIdentity("fixture-device", null),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            PassportRealm.Oversea);
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal("bbs-api-os.hoyolab.com", request.RequestUri!.Host);
            Assert.Equal(
                "/game_record/app/genshin/api/character/list",
                request.RequestUri.AbsolutePath);
            Assert.Equal("2.54.0", Assert.Single(request.Headers.GetValues("x-rpc-app_version")));
            Assert.Equal("zh-cn", Assert.Single(request.Headers.GetValues("x-rpc-language")));
            Assert.False(request.Headers.Contains("x-rpc-device_fp"));
            string cookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.Contains("ltuid_v2=1111", cookie);
            Assert.Contains("ltoken_v2=fixture", cookie);
            Assert.Contains("account_id_v2=1111", cookie);
            Assert.Contains("cookie_token_v2=fixture-cookie", cookie);
            Assert.Contains("account_mid_v2=fixture-mid", cookie);
            return Json(RosterBody);
        }));
        using var source = new HoYoGameRecordCharacterWeaponSource(
            TimeProvider.System, client);

        CharacterWeaponSourceResult result = await source.FetchAsync(
            passport, role);

        Assert.Equal(CharacterWeaponCollectionStatus.Received, result.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fixture; other=fixture")]
    public async Task RejectsMissingOrInvalidLTokenBeforeSending(string? lToken)
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new InvalidOperationException("Unexpected network request.")));
        using var source = new HoYoGameRecordCharacterWeaponSource(
            TimeProvider.System, client);

        Assert.Equal(
            CharacterWeaponCollectionStatus.AuthenticationRequired,
            (await source.FetchAsync(Passport(lToken), Role)).Status);
    }

    [Theory]
    [InlineData(5003, CharacterWeaponCollectionStatus.AccessDenied)]
    [InlineData(10102, CharacterWeaponCollectionStatus.AccessDenied)]
    [InlineData(1034, CharacterWeaponCollectionStatus.VerificationRequired)]
    [InlineData(-100, CharacterWeaponCollectionStatus.AuthenticationRequired)]
    [InlineData(10101, CharacterWeaponCollectionStatus.RateLimited)]
    [InlineData(1, CharacterWeaponCollectionStatus.SourceRejected)]
    public async Task MapsBusinessResult(
        int retcode,
        CharacterWeaponCollectionStatus expected)
    {
        using var client = new HttpClient(new RecordingHandler(_ =>
            Json($$"""{"retcode":{{retcode}}}""")));
        using var source = new HoYoGameRecordCharacterWeaponSource(
            TimeProvider.System, client);

        CharacterWeaponSourceResult result = await source.FetchAsync(
            Passport(), Role);

        Assert.Equal(expected, result.Status);
        Assert.Equal(retcode, result.ProviderCode);
        Assert.Equal(200, result.HttpStatus);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task TransportFailureIsReportedAsUnavailable()
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new HttpRequestException("fixture")));
        using var source = new HoYoGameRecordCharacterWeaponSource(
            TimeProvider.System, client);

        Assert.Equal(
            CharacterWeaponCollectionStatus.Unavailable,
            (await source.FetchAsync(Passport(), Role)).Status);
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content) };

    private static PassportAccount Passport(string? lToken = "fixture") =>
        new(
            Guid.NewGuid(), "1111", null, null, PassportLoginMethod.ManualCookie,
            new PassportCredentials(
                null, lToken, "fixture-cookie", null, null, null, null),
            new PassportDeviceIdentity("fixture-device", "fixture-fingerprint"),
            DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }
}

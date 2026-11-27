// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using System.Text.Json.Nodes;
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
            if (request.RequestUri!.AbsolutePath.EndsWith("/character/detail", StringComparison.Ordinal))
            {
                string payload = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                var ids = JsonNode.Parse(payload)!["character_ids"]!.AsArray();
                Assert.Equal(new[] { 10000093, 10000103 }, ids.Select(value => value!.GetValue<int>()));
                return Json(BuildDetails().ToJsonString());
            }
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
            string rosterCookie = Assert.Single(request.Headers.GetValues("Cookie"));
            Assert.Contains("ltuid=1111", rosterCookie);
            Assert.Contains("ltoken=fixture", rosterCookie);
            Assert.Contains("account_id=1111", rosterCookie);
            Assert.Contains("cookie_token=fixture-cookie", rosterCookie);
            Assert.Contains("ltoken_v2=fixture", rosterCookie);
            Assert.Contains("cookie_token_v2=fixture-cookie", rosterCookie);
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
        var detail = Assert.IsType<CharacterDetail>(snapshot.Characters[0].CharacterDetail);
        Assert.Equal("Fixture", detail.Name);
        Assert.Equal(10, detail.Friendship);
        Assert.Equal(6, detail.Ascension);
        Assert.False(Assert.Single(detail.Constellations!).Unlocked);
        Assert.Equal("124.5%", Assert.Single(Assert.Single(detail.Talents!).Attributes!).Value);
        var enhancedTalent = Assert.Single(detail.Talents!);
        Assert.Equal(new CharacterEnhancement(true, false, "<b>Enhanced</b>"), Assert.Single(detail.Constellations!).Enhancement);
        Assert.Equal(new CharacterEnhancement(true, true, "Enhanced skill"), enhancedTalent.Enhancement);
        Assert.Empty(enhancedTalent.BeforeEnhancementIndexes!);
        Assert.Equal(0, Assert.Single(enhancedTalent.AfterEnhancementIndexes!));
        Assert.Equal("1016", Assert.Single(detail.BaseStats!).Base);
        Assert.Equal(4, Assert.Single(Assert.Single(detail.Artifacts!).SubStats!).Hits);
        Assert.Equal(5, detail.Weapon?.Refinement);
        Assert.Equal("攻击力", snapshot.CharacterMetadata?.Properties?["2001"].Name);
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

    [Theory]
    [InlineData("unknown")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("missing stat type")]
    [InlineData("missing artifact type")]
    [InlineData("mismatched property key")]
    [InlineData("negative hit count")]
    [InlineData("invalid weapon ascension")]
    public async Task InvalidDetailsDoNotExposeARosterAsSuccessfulCollection(string defect)
    {
        var details = BuildDetails();
        var list = details["data"]!["list"]!.AsArray();
        switch (defect)
        {
            case "unknown": list[0]!["relics"]![0]!["sub_property_list"]![0]!["future_field"] = 1; break;
            case "missing": list.RemoveAt(1); break;
            case "duplicate": list.Add(list[0]!.DeepClone()); break;
            case "missing stat type": list[0]!["base_properties"]![0]!.AsObject().Remove("property_type"); break;
            case "missing artifact type": list[0]!["relics"]![0]!["sub_property_list"]![0]!.AsObject().Remove("property_type"); break;
            case "mismatched property key": details["data"]!["property_map"]!["2001"]!["property_type"] = 2002; break;
            case "negative hit count": list[0]!["relics"]![0]!["sub_property_list"]![0]!["times"] = -1; break;
            case "invalid weapon ascension": list[0]!["base"]!["weapon"]!["promote_level"] = 7; break;
        }
        using var client = new HttpClient(new RecordingHandler(request =>
            Json(request.RequestUri!.AbsolutePath.EndsWith("/detail", StringComparison.Ordinal) ?
                details.ToJsonString() : RosterBody)));
        using var source = new HoYoGameRecordCharacterWeaponSource(TimeProvider.System, client);
        var result = await source.FetchAsync(Passport(), Role);

        Assert.Equal(CharacterWeaponCollectionStatus.InvalidResponse, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public async Task ArtifactHitCountPreservesUnavailableAndExplicitZero(int? hits)
    {
        var details = BuildDetails();
        details["data"]!["list"]![0]!["relics"]![0]!["sub_property_list"]![0]!["times"] = hits;
        using var client = new HttpClient(new RecordingHandler(request =>
            Json(request.RequestUri!.AbsolutePath.EndsWith("/detail", StringComparison.Ordinal) ?
                details.ToJsonString() : RosterBody)));
        using var source = new HoYoGameRecordCharacterWeaponSource(TimeProvider.System, client);
        var result = await source.FetchAsync(Passport(), Role);

        Assert.Equal(CharacterWeaponCollectionStatus.Received, result.Status);
        var detail = Assert.IsType<CharacterDetail>(result.Snapshot?.Characters[0].CharacterDetail);
        Assert.Equal(hits, Assert.Single(Assert.Single(detail.Artifacts!).SubStats!).Hits);
    }

    [Fact]
    public async Task MissingCharacterAscensionUsesValidatedOfficialCalculatorEntry()
    {
        var details = BuildDetails();
        details["data"]!["list"]![0]!["base"]!["promote_level"] = null;
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/character/detail", StringComparison.Ordinal)) return Json(details.ToJsonString());
            if (path.EndsWith("/character/list", StringComparison.Ordinal)) return Json(RosterBody);
            Assert.Equal("api-takumi.mihoyo.com", request.RequestUri.Host);
            Assert.EndsWith("/sync/avatar/list", path, StringComparison.Ordinal);
            return Json("""{"retcode":0,"data":{"total":1,"list":[{"id":10000093,"promote_level":6,"level_current":90,"max_level":90}]}}""");
        }));
        using var source = new HoYoGameRecordCharacterWeaponSource(TimeProvider.System, client);
        var result = await source.FetchAsync(Passport(), Role);

        var detail = Assert.IsType<CharacterDetail>(result.Snapshot?.Characters[0].CharacterDetail);
        Assert.Equal(6, detail.Ascension);
        Assert.Equal(90, detail.AscensionEvidence?.CurrentLevel);
    }

    private static JsonNode BuildDetails()
    {
        var data = JsonNode.Parse(RosterBody)!;
        var list = data["data"]!["list"]!.AsArray();
        var entries = list.Select(value => new JsonObject { ["base"] = value!.DeepClone() }).ToArray();
        list.Clear();
        entries[0]["base"]!["name"] = "Fixture";
        entries[0]["base"]!["fetter"] = 10;
        entries[0]["base"]!["promote_level"] = 6;
        entries[1]["base"]!["promote_level"] = 5;
        var sample = JsonNode.Parse("""
            {
              "constellations": [{"id": 1, "pos": 1, "is_actived": false, "name": "Fixture constellation", "effect": "<b>Effect</b>", "can_enhanced": true, "is_enhanced": false, "enhanced_effect": "<b>Enhanced</b>"}],
              "skills": [{"skill_id": 1, "level": 10, "can_enhanced": true, "is_enhanced": true, "enhanced_desc": "Enhanced skill", "before_enhanced_skill_attr_index": [], "after_enhanced_skill_attr_index": [0], "skill_affix_list": [{"name": "Damage", "value": "124.5%"}]}],
              "base_properties": [{"property_type": 2001, "base": "1016", "add": "2542", "final": "3558"}],
              "relics": [{"id": 15001, "pos": 1, "level": 20, "sub_property_list": [{"property_type": 20, "value": "15.5%", "times": 4}]}]
            }
            """)!.AsObject();
        foreach (var property in sample) entries[0][property.Key] = property.Value!.DeepClone();
        foreach (var entry in entries) list.Add(entry);
        data["data"]!["property_map"] = JsonNode.Parse("""{"2001":{"property_type":2001,"name":"攻击力"}}""");
        return data;
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

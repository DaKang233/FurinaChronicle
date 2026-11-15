// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Services.HistoryDomains.Collecting;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.Tests.Infrastructure.HistoryDomains;

public sealed class HoYoGameRecordChallengeModeSourceTests
{
    private const string TheaterBody = """
        {
          "retcode": 0,
          "data": {
            "data": [
              {
                "schedule": {
                  "schedule_id": 37,
                  "start_time": 1794000000,
                  "end_time": 1795000000
                },
                "stat": { "difficulty_id": 4, "medal_num": 9 },
                "detail": {
                  "rounds_data": [
                    {
                      "round_id": 1,
                      "is_get_medal": true,
                      "finish_time": 1794100000,
                      "avatars": [
                        {
                          "avatar_id": 10000093,
                          "name": "闲云",
                          "image": "https://example.invalid/avatar.png",
                          "level": 90,
                          "rarity": 5
                        }
                      ],
                      "choice_cards": [ { "name": "元素战技强化" } ],
                      "buffs": [ { "name": "元素爆发强化" } ],
                      "enemies": [
                        {
                          "name": "深渊法师",
                          "icon": "https://example.invalid/enemy.png",
                          "level": 90
                        }
                      ]
                    },
                    { "round_id": 2, "is_get_medal": false, "finish_time": 1794200000 }
                  ]
                },
                "has_data": true
              }
            ],
            "is_unlock": true
          }
        }
        """;

    private const string StygianBody = """
        {
          "retcode": 0,
          "data": {
            "data": [
              {
                "schedule": {
                  "schedule_id": 5,
                  "start_time": 1794000000,
                  "end_time": 1795000000
                },
                "single": {
                  "best": { "difficulty": 6, "second": 120 },
                  "challenge": [
                    {
                      "name": "怪物A",
                      "second": 60,
                      "teams": [
                        {
                          "avatar_id": 10000093,
                          "side_icon": "https://example.invalid/side.png",
                          "level": 90,
                          "rank": 6
                        }
                      ],
                      "monster": {
                        "name": "怪物A",
                        "level": 90,
                        "icon": "https://example.invalid/monster.png"
                      }
                    },
                    { "name": "怪物B", "second": 90 }
                  ],
                  "has_data": true
                },
                "mp": {}
              }
            ],
            "is_unlock": true
          }
        }
        """;

    private static readonly GameRoleNaturalIdentity Role =
        new("hk4e_cn", "cn_gf01", "100000001");

    [Fact]
    public async Task TheaterUsesRoleCombatAndMapsRounds()
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "/game_record/app/genshin/api/role_combat",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "?role_id=100000001&server=cn_gf01&need_detail=true&active=1",
                request.RequestUri.Query);
            Assert.Equal("5", Assert.Single(request.Headers.GetValues("x-rpc-client_type")));
            Assert.Equal(
                "ltuid=1111; ltoken=fixture; account_id=1111; cookie_token=fixture-cookie",
                Assert.Single(request.Headers.GetValues("Cookie")));
            Assert.Equal(3, Assert.Single(request.Headers.GetValues("DS")).Split(',').Length);
            return Json(TheaterBody);
        }));
        using var source = new HoYoGameRecordChallengeModeSource(
            TimeProvider.System, client);

        ChallengeModeSourceResult result = await source.FetchAsync(
            ChallengeMode.ImaginariumTheater, Passport(), Role);

        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        Assert.Equal(200, result.HttpStatus);
        ChallengeModeSnapshot snapshot = Assert.IsType<ChallengeModeSnapshot>(
            result.Snapshot);
        Assert.Equal(ChallengeMode.ImaginariumTheater, snapshot.Mode);
        Assert.Equal("37", snapshot.SeasonId);
        Assert.Equal(4, snapshot.Difficulty);
        Assert.Equal(9, snapshot.MedalCount);
        Assert.Equal(2, snapshot.Stages.Count);
        ChallengeStageResult first = snapshot.Stages[0];
        Assert.Equal("1", first.StageId);
        Assert.Equal(1, first.Score);
        Assert.Equal(
            DateTimeOffset.FromUnixTimeSeconds(1794100000),
            first.CompletedAt.Start);
        Assert.Equal(TimePrecision.Second, first.CompletedAt.Precision);
        Assert.Equal(0, snapshot.Stages[1].Score);
        ChallengeStageResult detailed = snapshot.Stages[0];
        ChallengeParticipant participant = Assert.Single(detailed.Participants);
        Assert.Equal("10000093", participant.Id);
        Assert.Equal("闲云", participant.Name);
        Assert.Equal(90, participant.Level);
        Assert.Equal(5, participant.Rarity);
        Assert.Equal(["元素战技强化", "元素爆发强化"], detailed.Buffs);
        Assert.Equal("深渊法师", Assert.Single(detailed.Enemies).Name);
    }

    [Fact]
    public async Task StygianUsesHardChallengeAndMapsMonsterSeconds()
    {
        using var client = new HttpClient(new RecordingHandler(request =>
        {
            Assert.Equal(
                "/game_record/app/genshin/api/hard_challenge",
                request.RequestUri!.AbsolutePath);
            Assert.Equal(
                "?role_id=100000001&server=cn_gf01&need_detail=true",
                request.RequestUri.Query);
            return Json(StygianBody);
        }));
        using var source = new HoYoGameRecordChallengeModeSource(
            TimeProvider.System, client);

        ChallengeModeSourceResult result = await source.FetchAsync(
            ChallengeMode.StygianOnslaught, Passport(), Role);

        Assert.Equal(SpiralAbyssCollectionStatus.Received, result.Status);
        ChallengeModeSnapshot snapshot = Assert.IsType<ChallengeModeSnapshot>(
            result.Snapshot);
        Assert.Equal(ChallengeMode.StygianOnslaught, snapshot.Mode);
        Assert.Equal("5", snapshot.SeasonId);
        Assert.Equal(6, snapshot.Difficulty);
        Assert.Null(snapshot.MedalCount);
        Assert.Equal(2, snapshot.Stages.Count);
        Assert.Equal("怪物A", snapshot.Stages[0].StageId);
        Assert.Equal(60, snapshot.Stages[0].Score);
        Assert.Equal(OccurrenceTime.Unknown, snapshot.Stages[0].CompletedAt);
        Assert.Equal("10000093", Assert.Single(snapshot.Stages[0].Participants).Id);
        Assert.Equal("怪物A", Assert.Single(snapshot.Stages[0].Enemies).Name);
        Assert.Empty(snapshot.Stages[0].Buffs);
    }

    [Theory]
    [InlineData(ChallengeMode.ImaginariumTheater)]
    [InlineData(ChallengeMode.StygianOnslaught)]
    public async Task MissingLTokenIsRejectedBeforeSending(ChallengeMode mode)
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new InvalidOperationException("Unexpected network request.")));
        using var source = new HoYoGameRecordChallengeModeSource(
            TimeProvider.System, client);

        Assert.Equal(
            SpiralAbyssCollectionStatus.AuthenticationRequired,
            (await source.FetchAsync(mode, Passport(null), Role)).Status);
    }

    [Theory]
    [InlineData(0, SpiralAbyssCollectionStatus.Empty)]
    [InlineData(5003, SpiralAbyssCollectionStatus.AccessDenied)]
    [InlineData(1034, SpiralAbyssCollectionStatus.VerificationRequired)]
    [InlineData(10101, SpiralAbyssCollectionStatus.RateLimited)]
    [InlineData(1, SpiralAbyssCollectionStatus.SourceRejected)]
    public async Task MapsBusinessResult(
        int retcode,
        SpiralAbyssCollectionStatus expected)
    {
        string body = "{\"retcode\":" + retcode +
            ",\"data\":{\"data\":[],\"is_unlock\":true}}";
        using var client = new HttpClient(new RecordingHandler(_ => Json(body)));
        using var source = new HoYoGameRecordChallengeModeSource(
            TimeProvider.System, client);

        ChallengeModeSourceResult result = await source.FetchAsync(
            ChallengeMode.ImaginariumTheater, Passport(), Role);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task TransportFailureIsReportedAsUnavailable()
    {
        using var client = new HttpClient(new RecordingHandler(
            _ => throw new HttpRequestException("fixture")));
        using var source = new HoYoGameRecordChallengeModeSource(
            TimeProvider.System, client);

        Assert.Equal(
            SpiralAbyssCollectionStatus.Unavailable,
            (await source.FetchAsync(
                ChallengeMode.StygianOnslaught, Passport(), Role)).Status);
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

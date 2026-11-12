// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.History;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class HistoryObservationViewTests
{
    private static readonly Guid AccountId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    private static readonly DateTimeOffset Time =
        new(2026, 10, 1, 12, 0, 0, TimeSpan.FromHours(8));

    [Fact]
    public async Task CharacterObservation_KeepsTimeAndMissingFieldsExplicit()
    {
        var observation = new ItemStateObservation(
            new ItemObservationId(Guid.NewGuid()),
            AccountId,
            InventoryItemKind.Character,
            DataCompleteness.Partial,
            [
                new InventoryStateEntry(
                    InventoryItemKind.Character,
                    "10000002",
                    null,
                    null,
                    level: null,
                    progression: null,
                    InventoryFieldCoverage.None,
                    DataCompleteness.Unknown),
            ],
            new RecordProvenance(
                DataOrigin.UserEntered,
                new RecordTimestamps(ObservedAt: Time)));

        CharacterWeaponObservationView view =
            await HistoryObservationViewBuilder.CharacterWeaponAsync(
                observation,
                NullDomainMetadataService.Instance,
                "UID 800000001");

        Assert.Contains("角色", view.Title);
        Assert.Contains("观察于", view.ObservedText);
        Assert.Contains("不适用", view.OccurredText);
        Assert.Contains("部分", view.CompletenessText);
        Assert.Contains("用户录入", view.SourceText);
        Assert.Contains("缺少等级", view.MissingFieldsNote);
        Assert.Contains("10000002", view.Detail);
        Assert.Equal("角色", view.KindLabel);
        Assert.Contains("UID 800000001", view.DetailText);
    }

    [Fact]
    public async Task AbyssObservation_UsesLastChamberOccurrenceAndStarCount()
    {
        ChallengeObservation observation = Abyss(
            "12",
            Time,
            stars3: (3, 3, 3));

        ChallengeObservationView view =
            await HistoryObservationViewBuilder.ChallengeAsync(
                observation,
                NullDomainMetadataService.Instance,
                "UID 800000001");

        Assert.StartsWith("发生于", view.OccurredText, StringComparison.Ordinal);
        Assert.Equal("星数：9", view.StarsText);
        Assert.Equal("难度：未知", view.DifficultyText);
        Assert.Equal("奖章：未知", view.MedalText);
        Assert.Equal("秒数成绩：不适用", view.SecondsText);
        Assert.Contains("第 12 层", view.Detail);
        Assert.Contains("深境螺旋", view.Title);
    }

    [Fact]
    public async Task TheaterObservation_ShowsStageLineupBuffsAndEnemies()
    {
        ChallengeObservation observation = new(
            ChallengeObservationId.New(),
            AccountId,
            ChallengeMode.ImaginariumTheater,
            "31",
            null,
            null,
            DataCompleteness.Partial,
            [],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: Time)),
            [
                new ChallengeStageResult(
                    "第一幕",
                    score: 1,
                    OccurrenceTime.At(Time, TimePrecision.Second),
                    participants:
                    [
                        new ChallengeParticipant("10000002", null, null, 90, 5),
                    ],
                    buffs: ["攻击提升"],
                    enemies:
                    [
                        new ChallengeEnemy(null, 95, null),
                    ]),
            ],
            difficulty: 4,
            medalCount: 9);

        ChallengeObservationView view =
            await HistoryObservationViewBuilder.ChallengeAsync(
                observation,
                NullDomainMetadataService.Instance,
                "UID 800000001");

        Assert.StartsWith("发生于", view.OccurredText, StringComparison.Ordinal);
        Assert.Equal("难度：4", view.DifficultyText);
        Assert.Equal("奖章：9", view.MedalText);
        Assert.Contains("10000002", view.Detail);
        Assert.Contains("攻击提升", view.Detail);
        Assert.Contains("未知", view.Detail);
    }

    [Fact]
    public async Task StygianObservation_HasNoOccurrenceAndReportsBestSeconds()
    {
        ChallengeObservation observation = new(
            ChallengeObservationId.New(),
            AccountId,
            ChallengeMode.StygianOnslaught,
            "5269013",
            null,
            null,
            DataCompleteness.Partial,
            [],
            new RecordProvenance(
                DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: Time, FetchedAt: Time)),
            [
                new ChallengeStageResult(
                    "怪物A",
                    score: 60,
                    OccurrenceTime.Unknown),
            ],
            difficulty: 6,
            medalCount: null);

        ChallengeObservationView view =
            await HistoryObservationViewBuilder.ChallengeAsync(
                observation,
                NullDomainMetadataService.Instance,
                "UID 800000001");

        Assert.Contains("未知（只以观察/采集时间排序）", view.OccurredText);
        Assert.Contains("最快 60 秒", view.SecondsText);
        Assert.Contains("观察于", view.ObservedText);
        Assert.Contains("幽境危战", view.Title);
        Assert.Equal("难度：6", view.DifficultyText);
        Assert.Equal("奖章：未知", view.MedalText);
    }

    private static ChallengeObservation Abyss(
        string seasonId,
        DateTimeOffset completedAt,
        (int Chamber, int Stars, int StarsB) stars3) => new(
        ChallengeObservationId.New(),
        AccountId,
        ChallengeMode.SpiralAbyss,
        seasonId,
        null,
        null,
        DataCompleteness.Partial,
        [
            new SpiralAbyssFloorResult(
                12,
                DataCompleteness.Partial,
                [
                    new SpiralAbyssChamberResult(
                        1,
                        stars3.Stars,
                        OccurrenceTime.At(completedAt, TimePrecision.Second)),
                    new SpiralAbyssChamberResult(
                        2,
                        stars3.StarsB,
                        OccurrenceTime.At(
                            completedAt.AddMinutes(1),
                            TimePrecision.Second)),
                    new SpiralAbyssChamberResult(
                        3,
                        stars3.Chamber,
                        OccurrenceTime.At(
                            completedAt.AddMinutes(2),
                            TimePrecision.Second)),
                ]),
        ],
        new RecordProvenance(
            DataOrigin.OfficialApi,
            new RecordTimestamps(ObservedAt: completedAt.AddMinutes(5))));
}

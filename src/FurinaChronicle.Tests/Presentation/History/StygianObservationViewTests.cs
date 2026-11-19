// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class StygianObservationViewTests
{
    [Fact]
    public async Task ShowsSeparateOfficialTotalsConstellationsEnemyMechanicsAndPerformanceTypes()
    {
        var snapshot = (await StygianDetailFixture.ReadAsync()).Snapshot!;
        var observation = StygianDetailFixture.Observation(Guid.NewGuid(), snapshot);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        Assert.Contains("单人总成绩：180 秒", view.SecondsText);
        Assert.Contains("联机总成绩：240 秒", view.SecondsText);
        Assert.Contains("绝境（6）", view.DifficultyText);
        Assert.Contains("无畏（5）", view.DifficultyText);
        Assert.Contains("未知", view.OccurredText);
        var single = Assert.Single(view.DetailSections, section => section.Title == "单人 · 合成关卡");
        var multiplayer = Assert.Single(view.DetailSections, section => section.Title == "联机 · 合成关卡");
        Assert.Equal("单人挑战", single.Run);
        Assert.Equal("联机挑战", multiplayer.Run);
        Assert.Equal(2, single.Metrics.Count);
        Assert.All(single.Metrics, metric => Assert.False(metric.Portrait!.UseRarityBackground));
        Assert.All(single.Battles.SelectMany(battle => battle.Groups).Where(group => group.Title == "阵容").SelectMany(group => group.Portraits), portrait => Assert.True(portrait.UseRarityBackground));
        Assert.Contains(single.Lines, line => line == "敌人说明：合成敌人说明\n合成机制第二行");
        Assert.Contains("敌人标签：合成元素抗性", single.Lines);
        Assert.Contains("敌人标签：合成多人标签", multiplayer.Lines);
        var portraits = single.Battles.SelectMany(battle => battle.Groups).SelectMany(group => group.Portraits).ToArray();
        Assert.Contains(portraits, portrait => portrait.Label.Contains("6 命", StringComparison.Ordinal));
        Assert.Contains(portraits, portrait => portrait.Label.Contains("0 命", StringComparison.Ordinal));
        Assert.Contains(portraits, portrait => portrait.Label.Contains("最强一击：3000000000", StringComparison.Ordinal));
        Assert.Contains(portraits, portrait => portrait.Label.Contains("最高总伤害：6000000000", StringComparison.Ordinal));
        var blings = Assert.Single(view.DetailSections, section => section.Title == "增辉角色");
        Assert.Contains(blings.Battles[0].Groups[0].Portraits, portrait => portrait.Label.Contains("上榜状态未知", StringComparison.Ordinal));
        Assert.DoesNotContain("DPS", view.Detail);
        Assert.All(portraits.Where(portrait => portrait.IsCharacter), portrait => Assert.Equal(5, portrait.Rarity));
        Assert.Contains(portraits, portrait => !portrait.IsCharacter && portrait.Rarity is null);
    }

    [Fact]
    public async Task EnemyDescriptionsAndTagsUseReadableProjectionWithoutRewritingSavedContent()
    {
        string response = StygianDetailFixture.Response
            .Replace("合成元素抗性", "<b>抗性</b> &amp; 元素<br/>下一行")
            .Replace("合成敌人说明", "<size=20>敌人说明</size> &amp; 机制");
        var snapshot = (await StygianDetailFixture.ReadAsync(response)).Snapshot!;
        var observation = StygianDetailFixture.Observation(Guid.NewGuid(), snapshot);
        string before = JsonSerializer.Serialize(observation);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成");
        var single = Assert.Single(view.DetailSections, section => section.Title == "单人 · 合成关卡");
        Assert.Contains("敌人说明：敌人说明 & 机制\n合成机制第二行", single.Lines);
        Assert.Contains("敌人标签：抗性 & 元素\n下一行", single.Lines);
        Assert.Contains(single.RichLines, line => line.Contains("<size=20>"));
        Assert.Equal(before, JsonSerializer.Serialize(observation));
        Assert.Contains("<size=20>", observation.Stages[0].StygianDetails!.MonsterDescriptions[0]);
        Assert.Contains("未知", view.OccurredText);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("https://act-webstatic.mihoyo.com/single-difficulty.png", true)]
    [InlineData("https://untrusted.invalid/difficulty.png", false)]
    [InlineData("https://act-webstatic.mihoyo.com/difficulty.png?credential=fixture", false)]
    public async Task DifficultyArtworkUsesOnlyEachRunStoredTrustedSource(string? singleIcon, bool visible)
    {
        const string multiplayerIcon = "https://act-webstatic.mihoyo.com/multiplayer-difficulty.png";
        var statistics = new StygianStatistics(new(true, 6, 180, singleIcon), new(true, 5, 240, multiplayerIcon));
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            ChallengeMode.StygianOnslaught, "900010", null, null,
            DataCompleteness.Partial, [],
            RecordProvenance.Unknown, difficulty: 6, stygianStatistics: statistics);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        var single = Assert.Single(view.DetailSections, section => section.Title == "单人挑战");
        var multiplayer = Assert.Single(view.DetailSections, section => section.Title == "联机挑战");
        Assert.Equal(visible, single.HasHeaderArtwork);
        if (visible)
        {
            Assert.Equal(singleIcon, single.HeaderArtwork!.Icon!.AbsoluteUri);
            Assert.Equal("单人挑战 · 绝境（6）", single.HeaderArtwork.Label);
        }
        Assert.Equal(multiplayerIcon, multiplayer.HeaderArtwork!.Icon!.AbsoluteUri);
        Assert.Equal("联机挑战 · 无畏（5）", multiplayer.HeaderArtwork.Label);
        Assert.Contains("总成绩：180 秒", single.Lines);
        Assert.Contains("总成绩：240 秒", multiplayer.Lines);
        Assert.Equal(singleIcon, statistics.SinglePlayer!.IconUrl);
        Assert.Contains("未知", view.OccurredText);
    }

    [Fact]
    public async Task NoDataAndMissingIconsDoNotInventDifficultyArtwork()
    {
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            ChallengeMode.StygianOnslaught, "900010", null, null,
            DataCompleteness.Partial, [],
            RecordProvenance.Unknown,
            stygianStatistics: new StygianStatistics(new(false), new(true, 5, 240)));
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        Assert.All(view.DetailSections, section => Assert.False(section.HasHeaderArtwork));
        Assert.Contains("暂无官方记录。", view.DetailSections[0].Lines);
    }

    [Theory]
    [InlineData("UI_LeyLineChallenge_Medal_0", "UI_LeyLineChallenge_Medal_0")]
    [InlineData("UI_LeyLineChallenge_Medal_1", "UI_LeyLineChallenge_Medal_1")]
    [InlineData("UI_LeyLineChallenge_Medal_2", "UI_LeyLineChallenge_Medal_2")]
    [InlineData("UI_LeyLineChallenge_Medal_3", "UI_LeyLineChallenge_Medal_3")]
    [InlineData("UI_LeyLineChallenge_Medal_4", "UI_LeyLineChallenge_Medal_4")]
    [InlineData("UI_LeyLineChallenge_Medal_5", "UI_LeyLineChallenge_Medal_5")]
    [InlineData("UI_LeyLineChallenge_Medal_6", "UI_LeyLineChallenge_Medal_6")]
    [InlineData("UI_LeyLineChallenge_Medal_7", "UI_LeyLineChallenge_Medal_7")]
    [InlineData("UI_LeyLineChallenge_Medal_4, UI_LeyLineChallenge_Medal_5", "UI_LeyLineChallenge_Medal_5")]
    public void ExactKnownSourceNamesResolveToBundledAssets(string source, string expected)
    {
        var picture = StygianDifficultyArtwork.FromSource(source, "源提供的难度");
        Assert.NotNull(picture);
        Assert.Equal(expected, picture.ResourceKey);
        Assert.Null(picture.Icon);
        Assert.False(picture.IsCharacter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("UI_LeyLineChallenge_Medal_8")]
    [InlineData("UI_LeyLineChallenge_Medal_00")]
    [InlineData("../UI_LeyLineChallenge_Medal_4")]
    [InlineData("UI_LeyLineChallenge_Medal_4.png")]
    [InlineData("unknown")]
    [InlineData("file:///UI_LeyLineChallenge_Medal_4")]
    public void UnknownResourceNamesDoNotBecomeLocalFilePaths(string source) =>
        Assert.Null(StygianDifficultyArtwork.FromSource(source, "未知"));

    [Fact]
    public async Task ResourceNameIsNotReplacedByTheNumericDifficultyAndSingleAndMultiplayerStaySeparate()
    {
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            ChallengeMode.StygianOnslaught, "900010", null, null, DataCompleteness.Partial, [], RecordProvenance.Unknown,
            difficulty: 6, stygianStatistics: new StygianStatistics(
                new(true, 6, 180, "UI_LeyLineChallenge_Medal_4"),
                new(true, 5, 240, "UI_LeyLineChallenge_Medal_3,UI_LeyLineChallenge_Medal_5")));
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        Assert.Equal("UI_LeyLineChallenge_Medal_4",
            Assert.Single(view.DetailSections, section => section.Title == "单人挑战").HeaderArtwork!.ResourceKey);
        Assert.Equal("UI_LeyLineChallenge_Medal_5",
            Assert.Single(view.DetailSections, section => section.Title == "联机挑战").HeaderArtwork!.ResourceKey);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UnknownOccurrenceListShowsTheAvailableObservedOrFetchedTimeWithItsActualBasis(bool observed)
    {
        var stamp = new DateTimeOffset(2026, 11, 19, 12, 34, 56, TimeSpan.FromHours(8));
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            ChallengeMode.StygianOnslaught, "900010", null, null, DataCompleteness.Partial, [],
            new RecordProvenance(DataOrigin.OfficialApi,
                new RecordTimestamps(ObservedAt: observed ? stamp : null, FetchedAt: stamp)));
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        Assert.StartsWith(observed ? "观察于" : "采集于", view.ListTimeText, StringComparison.Ordinal);
        Assert.Contains(stamp.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), view.ListTimeText);
        Assert.Contains("发生时间未知", view.ListTimeText);
        Assert.DoesNotContain("发生于", view.ListTimeText);
        Assert.Contains("未知", view.OccurredText);
        Assert.Empty(observation.Stages);
        Assert.Equal(stamp, observation.Provenance.Timestamps.FetchedAt);
    }

    private sealed class Metadata : IDomainMetadataService
    {
        public ValueTask<DomainItemMetadata?> ResolveItemAsync(InventoryItemKind kind, string typeId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<DomainItemMetadata?>(new(kind, typeId, "合成指标角色", 5, null, "fixture"));
        public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(ChallengeMode mode, string seasonId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ChallengeScheduleMetadata?>(null);
    }
}

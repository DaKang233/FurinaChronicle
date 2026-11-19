// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class TheaterBuffViewTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShowsCategoryEnhancementLevelEffectsAndSafeArtworkWithoutMergingNames(bool untrusted)
    {
        string response = TheaterBuffFixture.Response;
        if (untrusted) response = response.Replace("https://act-webstatic.mihoyo.com/fixture-choice.png",
            "https://untrusted.invalid/card.png?credential=fixture", StringComparison.Ordinal);
        var observation = TheaterBuffFixture.Observation(Guid.NewGuid(), (await TheaterBuffFixture.ReadAsync(response)).Snapshot!);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        var stage = Assert.Single(view.DetailSections, section => section.Title == "第 10 幕");
        Assert.Equal(3, stage.Buffs.Count);
        Assert.Equal("同名合成卡牌 · 已强化", stage.Buffs[0].Title);
        Assert.Equal("同名合成卡牌 · 未强化", stage.Buffs[1].Title);
        Assert.Equal("合成辉彩祝福 · Lv.3", stage.Buffs[2].Title);
        Assert.Contains("选择卡牌", stage.Buffs[0].Detail);
        Assert.Contains("辉彩祝福", stage.Buffs[2].Detail);
        Assert.Contains("效果甲\n后续", stage.Buffs[2].Detail);
        Assert.Equal(untrusted, stage.Buffs[0].Artwork.Icon is null);
        Assert.NotNull(stage.Buffs[1].Artwork.Icon);
        Assert.False(stage.Buffs[0].Artwork.IsCharacter);
        Assert.Contains("祝福总览：合成祝福总览", stage.Lines);
        Assert.DoesNotContain(stage.Lines, line => line.StartsWith("同名合成卡牌", StringComparison.Ordinal));
    }

    [Fact]
    public void StylingIsRemovedOnlyInDisplayWithoutLosingContentOrUnknownSyntax()
    {
        const string source = "<color=#ff0000><b>伤害</b></color> +20%\\n<i>效果</i><br/>A &amp; B <未知标记>";
        Assert.Equal("伤害 +20%\n效果\nA & B <未知标记>", HoYoDescriptionText.Plain(source));
        Assert.Contains("<color=#ff0000>", source);
    }

    [Fact]
    public async Task SummaryOnlyStructuredPayloadDoesNotRequireLegacyStringsOrCardEntries()
    {
        const string summary = "<color=#ff0000>只提供总览</color>";
        var stage = new ChallengeStageResult("1", null, OccurrenceTime.Unknown,
            theaterRound: new(false, null, buffSummary: summary));
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            ChallengeMode.ImaginariumTheater, "37", null, null,
            DataCompleteness.Partial, [],
            RecordProvenance.Unknown, [stage]);
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, new Metadata(), "合成账号");
        Assert.Contains("祝福总览：只提供总览", Assert.Single(view.DetailSections).Lines);
        Assert.Contains("只提供总览", view.Detail);
        Assert.Equal(summary, stage.TheaterRound!.BuffSummary);
        Assert.Empty(stage.Buffs);
    }

    private sealed class Metadata : IDomainMetadataService
    {
        public ValueTask<DomainItemMetadata?> ResolveItemAsync(InventoryItemKind kind, string typeId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<DomainItemMetadata?>(null);
        public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(ChallengeMode mode, string seasonId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<ChallengeScheduleMetadata?>(null);
    }
}

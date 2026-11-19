// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Net;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Metadata;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.HistoryDomains.Collecting;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Tests.TestDoubles;

namespace FurinaChronicle.Tests.Presentation.History;

public sealed class TheaterObservationViewTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ShowsScopedMetadataRostersAndNumericRoundOrderAndHonorsStatVisibility(bool showStats)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(TheaterDetailFixture.Response.Replace(
                "\"is_show_battle_stats\":true", $"\"is_show_battle_stats\":{showStats.ToString().ToLowerInvariant()}", StringComparison.Ordinal)),
        };
        var snapshot = (await new MiYouSheRoleCombatResponseReader(TimeProvider.System).ReadAsync(response)).Snapshot!;
        var observation = new ChallengeObservation(ChallengeObservationId.New(), Guid.NewGuid(),
            snapshot.Mode, snapshot.SeasonId, snapshot.PeriodStart, snapshot.PeriodEnd, snapshot.Completeness,
            [], RecordProvenance.Unknown, snapshot.Stages, snapshot.Difficulty, snapshot.MedalCount,
            theaterStatistics: snapshot.TheaterStatistics);
        var service = new Metadata();
        var view = await HistoryObservationViewBuilder.ChallengeAsync(observation, service, "合成账号");
        Assert.Equal(new[] { "第 1 幕", "第 2 幕", "第 10 幕", "塔罗挑战 1" },
            view.DetailSections.Select(section => section.Title).Where(title => title.StartsWith("第 ", StringComparison.Ordinal) || title.StartsWith("塔罗挑战", StringComparison.Ordinal)));
        var summary = Assert.Single(view.DetailSections, section => section.Title == "演出总览");
        Assert.Contains("演出总时长：90761 秒（25:12:41）", summary.Lines);
        Assert.Contains("准入元素：火、风", view.DetailSections[0].Lines);
        var backup = Assert.Single(view.DetailSections, section => section.Title == "候补角色");
        var people = backup.Battles[0].Groups[0].Portraits;
        Assert.Contains(people, person => person.Label.Contains("试用", StringComparison.Ordinal));
        Assert.Contains(people, person => person.Label.Contains("助演", StringComparison.Ordinal));
        var initial = Assert.Single(view.DetailSections, section => section.Title == "初始角色（期次配置）");
        Assert.DoesNotContain("自有", initial.Battles[0].Groups[0].Portraits[0].Label);
        Assert.Equal(showStats, view.DetailSections.Any(section => section.Title == "最强一击"));
        Assert.All(view.DetailSections.Where(section => section.Kind == ChallengeSectionKind.Metric).SelectMany(section => section.Metrics), metric => Assert.False(metric.Portrait!.UseRarityBackground));
        Assert.All(observation.Stages, stage => Assert.Equal(TimePrecision.Second, stage.CompletedAt.Precision));
    }

    private sealed class Metadata : IDomainMetadataService
    {
        public ValueTask<DomainItemMetadata?> ResolveItemAsync(
            FurinaChronicle.Core.Inventory.InventoryItemKind kind, string typeId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<DomainItemMetadata?>(null);

        public ValueTask<ChallengeScheduleMetadata?> ResolveScheduleAsync(
            ChallengeMode mode, string seasonId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ChallengeScheduleMetadata?>(new(mode, seasonId, null, null, null, "fixture",
                Theater: new TheaterScheduleMetadata([1,7], ["10000016"], ["10000089"])));
    }
}

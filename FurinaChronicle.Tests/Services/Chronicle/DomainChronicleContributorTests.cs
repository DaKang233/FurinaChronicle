// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Chronicle;

namespace FurinaChronicle.Tests.Services.Chronicle;

public sealed class DomainChronicleContributorTests
{
    private static readonly Guid AccountId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void PartialInventoryAbsence_DoesNotCreateRemoval()
    {
        ItemStateObservation before = Observation(
            1, DataCompleteness.Complete,
            [Item("character-a", 1, InventoryFieldCoverage.Level)]);
        ItemStateObservation after = Observation(
            2, DataCompleteness.Partial, []);

        IReadOnlyList<ChronicleEntry> result =
            new InventoryChronicleContributor().Project(AccountId, [before, after]);

        Assert.DoesNotContain(result, item => item.Kind == ChronicleEntryKind.StateChange);
    }

    [Fact]
    public void CoveredFieldDifference_CreatesUncertainIntervalChange()
    {
        ItemStateObservation before = Observation(
            1, DataCompleteness.Partial,
            [Item("character-a", 1, InventoryFieldCoverage.Level)]);
        ItemStateObservation after = Observation(
            2, DataCompleteness.Partial,
            [Item("character-a", 2, InventoryFieldCoverage.Level)]);

        ChronicleEntry change = new InventoryChronicleContributor()
            .Project(AccountId, [before, after])
            .Single(item => item.Kind == ChronicleEntryKind.StateChange);

        Assert.Equal(OccurrenceTimeKind.UncertainInterval, change.Time.Occurred!.Kind);
        Assert.False(ChronicleProfileRules.IsVisible(change,
            new ChronicleProfileRequest(ChronicleTimeline.Character)));
        Assert.True(ChronicleProfileRules.IsVisible(change,
            new ChronicleProfileRequest(ChronicleTimeline.Character,
                ChronicleGranularity.Detailed, IncludeStateChanges: true)));
    }

    [Fact]
    public void SpiralAbyss_TargetPresenceCreatesOneStableCompletionPerSeason()
    {
        ChallengeObservation laterEvidence = Challenge(2, "season-1", 3, 20);
        ChallengeObservation earlierEvidence = Challenge(1, "season-1", 2, 10);

        IReadOnlyList<ChronicleEntry> result =
            new ChallengeChronicleContributor().Project(
                AccountId, [laterEvidence, earlierEvidence]);

        ChronicleEntry completion = Assert.Single(result, item =>
            item.Kind == ChronicleEntryKind.ChallengeCompleted);
        Assert.Equal("2", completion.Time.Occurred!.UtcStart!.Value.Second.ToString());
        Assert.Equal("SpiralAbyss", completion.SummaryArguments["mode"]);
        Assert.True(completion.Timelines.HasFlag(ChronicleTimeline.Main));
    }

    [Fact]
    public void CompleteObservationWithoutTarget_DoesNotInventCompletion()
    {
        ChallengeObservation value = new(
            new ChallengeObservationId(Guid.Parse("00000000-0000-4000-8000-000000000001")),
            AccountId, ChallengeMode.SpiralAbyss, "season-1", null, null,
            DataCompleteness.Complete, [], Provenance(1));

        IReadOnlyList<ChronicleEntry> result =
            new ChallengeChronicleContributor().Project(AccountId, [value]);

        Assert.DoesNotContain(result, item =>
            item.Kind == ChronicleEntryKind.ChallengeCompleted);
    }

    [Fact]
    public void OnlyCatalogMarkedAchievementsEnterMainTimeline()
    {
        var facts = new[]
        {
            new ChronicleAchievementFact(AccountId, "important", null, Provenance(1)),
            new ChronicleAchievementFact(AccountId, "ordinary", null, Provenance(2)),
        };
        var catalog = new Dictionary<string, ChronicleAchievementDescriptor>
        {
            ["important"] = new("important", true),
            ["ordinary"] = new("ordinary", false),
        };

        ChronicleEntry entry = Assert.Single(
            new AchievementChronicleContributor().Project(AccountId, facts, catalog));

        Assert.Equal(ChronicleSortBasis.Observed, entry.Time.SortBasis);
        Assert.Equal("important", entry.SourceEntityId);
    }

    private static ItemStateObservation Observation(
        int suffix,
        DataCompleteness completeness,
        IReadOnlyList<InventoryStateEntry> entries) => new(
            new ItemObservationId(Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")),
            AccountId, InventoryItemKind.Character, completeness, entries,
            Provenance(suffix));

    private static InventoryStateEntry Item(
        string id,
        int level,
        InventoryFieldCoverage coverage) => new(
            InventoryItemKind.Character, id, null, null, level, null,
            coverage, DataCompleteness.Partial);

    private static ChallengeObservation Challenge(
        int suffix,
        string season,
        int completedSecond,
        int observedSecond) => new(
            new ChallengeObservationId(Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")),
            AccountId, ChallengeMode.SpiralAbyss, season, null, null,
            DataCompleteness.Partial,
            [new SpiralAbyssFloorResult(12, DataCompleteness.Partial,
                [new SpiralAbyssChamberResult(3, 0,
                    OccurrenceTime.At(Time(completedSecond), TimePrecision.Second))])],
            Provenance(observedSecond));

    private static RecordProvenance Provenance(int second) => new(
        DataOrigin.OfficialApi,
        new RecordTimestamps(ObservedAt: Time(second)));

    private static DateTimeOffset Time(int second) =>
        new(2026, 1, 1, 0, 0, second, TimeSpan.FromHours(8));
}

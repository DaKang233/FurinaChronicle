// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Services.Chronicle;

public sealed class GachaAcquisitionChronicleContributorTests
{
    private static readonly Guid AccountId =
        Guid.Parse("11111111-1111-4111-8111-111111111111");

    [Fact]
    public void FiveStar_IsOneCandidateAcrossMainAndCharacterProfiles()
    {
        GachaRecord record = Record("1", "char-five", 5, 1, 0);
        var contributor = new GachaAcquisitionChronicleContributor();

        ChronicleEntry entry = Assert.Single(contributor.Project(
            AccountId,
            [record],
            [],
            Catalog(),
            new ChronicleProjectionEvidence()));

        Assert.True(entry.Timelines.HasFlag(ChronicleTimeline.Main));
        Assert.True(entry.Timelines.HasFlag(ChronicleTimeline.Character));
        Assert.Equal("local-first-record", entry.Milestone);
        Assert.Equal(record.Provenance.Origin, entry.BaseOrigin);
        Assert.Equal(DataOrigin.Derived, entry.AnnotationOrigin);
    }

    [Fact]
    public void FourthStarCharacter_OnlyFirstAndSeventhEnterMain()
    {
        GachaRecord[] records = Enumerable.Range(1, 7)
            .Select(index => Record(
                index.ToString(),
                "char-four",
                4,
                1,
                index))
            .ToArray();
        var contributor = new GachaAcquisitionChronicleContributor();

        IReadOnlyList<ChronicleEntry> entries = contributor.Project(
            AccountId,
            records,
            [],
            Catalog(),
            new ChronicleProjectionEvidence());

        Assert.Equal(7, entries.Count);
        ChronicleEntry[] main = entries
            .Where(item => item.Timelines.HasFlag(ChronicleTimeline.Main))
            .ToArray();
        Assert.Equal(2, main.Length);
        Assert.Contains(main, item => item.Milestone == "local-first-record");
        Assert.Contains(main, item => item.Milestone == "local-seventh-record");
    }

    [Fact]
    public void CompleteSyntheticEvidence_EnablesExactPityAndTrueMilestone()
    {
        GachaRecord[] records =
        [
            Record("1", "char-five", 5, 1, 1),
            Record("2", "char-four", 4, 1, 2),
            Record("3", "char-five", 5, 1, 3),
        ];
        var contributor = new GachaAcquisitionChronicleContributor();

        IReadOnlyList<ChronicleEntry> entries = contributor.Project(
            AccountId,
            records,
            [],
            Catalog(),
            new ChronicleProjectionEvidence(
                DeclaresCompleteGachaHistory: true,
                DeclarationReference: "synthetic-fixture"));

        ChronicleEntry secondFive = entries.Single(item =>
            item.SourceEntityId == "3");
        Assert.Equal(2, secondFive.ExactPity);
        Assert.Equal(2, secondFive.LocalRecordedPulls);
        Assert.Contains(entries, item => item.Milestone == "first-acquisition");
    }

    [Fact]
    public void SameSecondAndAggregatedRecord_DoNotCreateFalseOrdinalOrPity()
    {
        GachaRecord[] records =
        [
            Record("1", "char-four", 4, 1, 1),
            Record("2", "char-four", 4, 1, 1),
            Record("3", "char-five", 5, 2, 2),
            Record("4", "char-five", 5, 1, 3),
        ];
        var contributor = new GachaAcquisitionChronicleContributor();

        IReadOnlyList<ChronicleEntry> entries = contributor.Project(
            AccountId,
            records,
            [],
            Catalog(),
            new ChronicleProjectionEvidence(true, "synthetic"));

        Assert.DoesNotContain(entries, item =>
            item.ItemId == "char-four" && item.Milestone is not null);
        Assert.Null(entries.Single(item => item.SourceEntityId == "4").ExactPity);
    }

    [Fact]
    public void ManualAndGachaOverlap_RemainsAmbiguousAndDistinct()
    {
        DateTimeOffset time = Time(1);
        var manual = new ManualAcquisition(
            new ManualAcquisitionId(
                Guid.Parse("22222222-2222-4222-8222-222222222222")),
            AccountId,
            InventoryItemKind.Character,
            "char-four",
            OccurrenceTime.ForInterval(
                time.AddHours(-1),
                time.AddHours(1),
                TimePrecision.Day),
            ManualAcquisitionReason.Other,
            "synthetic",
            new RecordProvenance(DataOrigin.UserEntered));
        var contributor = new GachaAcquisitionChronicleContributor();

        IReadOnlyList<ChronicleEntry> entries = contributor.Project(
            AccountId,
            [Record("1", "char-four", 4, 1, 1)],
            [new ManualAcquisitionState(
                manual,
                new FurinaChronicle.Core.History.FactVersion(1),
                Guid.Parse("33333333-3333-4333-8333-333333333333"))],
            Catalog(),
            new ChronicleProjectionEvidence());

        Assert.Equal(2, entries.Count);
        Assert.All(entries, item =>
            Assert.Equal(ChronicleEvidenceStatus.Ambiguous, item.Evidence.Status));
    }

    private static GachaRecord Record(
        string externalId,
        string itemId,
        int rank,
        int count,
        int second) => new(
            AccountId,
            externalId,
            ItemName: null,
            rank,
            Time(second))
        {
            ItemId = itemId,
            UigfGachaType = "301",
            Count = count,
            Provenance = new RecordProvenance(DataOrigin.StandardImport),
        };

    private static DateTimeOffset Time(int second) =>
        new(2026, 1, 1, 0, 0, second, TimeSpan.FromHours(8));

    private static IChronicleItemCatalogSnapshot Catalog() =>
        new TestCatalog(
        [
            new ChronicleItemDescriptor(
                "char-five",
                InventoryItemKind.Character,
                5),
            new ChronicleItemDescriptor(
                "char-four",
                InventoryItemKind.Character,
                4),
        ]);

    private sealed class TestCatalog(
        IEnumerable<ChronicleItemDescriptor> items)
        : IChronicleItemCatalogSnapshot
    {
        private readonly Dictionary<string, ChronicleItemDescriptor> byId =
            items.ToDictionary(item => item.ItemId, StringComparer.Ordinal);

        public string Version => "synthetic-v1";

        public ChronicleItemDescriptor? Find(string itemId) =>
            byId.GetValueOrDefault(itemId);
    }
}

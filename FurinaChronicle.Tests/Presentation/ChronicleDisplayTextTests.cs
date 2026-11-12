// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Presentation.Chronicle;
using FurinaChronicle.Services.Chronicle;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChronicleDisplayTextTests
{
    private static readonly Guid AccountId = ChronicleTestEntries.AccountId;

    [Fact]
    public void ContributorOutputs_AllTitleMilestoneAndEvidenceKeysAreMapped()
    {
        IReadOnlyList<ChronicleEntry> entries = ContributorEntries();

        Assert.Equal(
            new[]
            {
                ChronicleKeyCatalog.GachaAcquisitionTitle,
                ChronicleKeyCatalog.ManualAcquisitionTitle,
                ChronicleKeyCatalog.ChallengeObservationTitle,
                ChronicleKeyCatalog.ChallengeCompletedTitle,
                ChronicleKeyCatalog.InventoryObservationTitle,
                ChronicleKeyCatalog.InventoryStateChangeTitle,
                ChronicleKeyCatalog.AchievementUnlockedTitle,
            }.Order(StringComparer.Ordinal),
            entries.Select(entry => entry.TitleKey)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));

        foreach (ChronicleEntry entry in entries)
        {
            Assert.True(
                ChronicleKeyCatalog.IsKnownTitleKey(entry.TitleKey),
                $"Unmapped title key: {entry.TitleKey}");
            if (entry.Milestone is not null)
            {
                Assert.True(
                    ChronicleKeyCatalog.IsKnownMilestoneKey(entry.Milestone),
                    $"Unmapped milestone: {entry.Milestone}");
            }
            Assert.True(
                ChronicleKeyCatalog.IsKnownEvidenceReasonKey(
                    entry.Evidence.ReasonKey),
                $"Unmapped evidence reason: {entry.Evidence.ReasonKey}");
        }

        var context = new ChronicleDisplayContext
        {
            ItemNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["char-four"] = "测试四星角色",
                ["char-five"] = "测试五星角色",
                ["weap-five"] = "测试五星武器",
                ["weap-four"] = "测试四星武器",
                ["1:char-a"] = "测试角色",
            },
            DisplayTimeZone = TimeZoneInfo.Utc,
        };
        foreach (ChronicleEntry entry in entries)
        {
            ChronicleEntryDisplay display =
                ChronicleEntryDisplay.FromEntry(entry, context);
            Assert.False(string.IsNullOrWhiteSpace(display.Title));
            Assert.DoesNotContain("chronicle.", display.Title, StringComparison.Ordinal);
            Assert.DoesNotContain("chronicle.", display.EvidenceText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KnownMilestones_UseProductWording()
    {
        Assert.Equal("首次获取", ChronicleDisplayText.Milestone("first-acquisition"));
        Assert.Equal("本地首次记录", ChronicleDisplayText.Milestone("local-first-record"));
        Assert.Equal("第七次获取（满命）", ChronicleDisplayText.Milestone("seventh-acquisition"));
        Assert.Equal("本地第七次记录（满命）", ChronicleDisplayText.Milestone("local-seventh-record"));
        Assert.Equal("完成节点", ChronicleDisplayText.Milestone("challenge-completed"));
        Assert.Equal("重要成就", ChronicleDisplayText.Milestone("important-achievement"));
        Assert.Null(ChronicleDisplayText.Milestone("unknown-milestone"));
    }

    [Fact]
    public void UnknownTitleKey_NeverLeaksTheRawKey()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            titleKey: "chronicle.unknown.future-entry");
        ChronicleEntryDisplay display = ChronicleEntryDisplay.FromEntry(entry);

        Assert.DoesNotContain("chronicle", display.Title, StringComparison.Ordinal);
        Assert.Contains("未能识别", display.Title);
    }

    [Fact]
    public void UnknownEvidenceReason_IsSafeAndExplicit()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            evidence: new ChronicleEvidence(
                ChronicleEvidenceStatus.Ambiguous,
                ChronicleEvidenceScope.Unknown,
                "chronicle.evidence.future-reason",
                []));
        ChronicleEntryDisplay display = ChronicleEntryDisplay.FromEntry(entry);

        Assert.DoesNotContain("chronicle", display.EvidenceText, StringComparison.Ordinal);
        Assert.Contains("依据说明不可用", display.EvidenceText);
    }

    [Fact]
    public void UnknownMilestone_IsHiddenAndDoesNotLeak()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(milestone: "future-milestone");
        ChronicleEntryDisplay display = ChronicleEntryDisplay.FromEntry(entry);

        Assert.Null(display.MilestoneText);
        Assert.DoesNotContain("future-milestone", display.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void GachaTitle_UsesItemNameAndRank()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            itemId: "10000089",
            rankType: 5,
            timelines: ChronicleTimeline.Main | ChronicleTimeline.Character,
            arguments: ChronicleTestEntries.Arguments(
                ("source_scope", "local-gacha-records")));
        ChronicleEntryDisplay display = ChronicleEntryDisplay.FromEntry(
            entry,
            new ChronicleDisplayContext
            {
                ItemNames = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["10000089"] = "芙宁娜",
                },
                DisplayTimeZone = TimeZoneInfo.Utc,
            });

        Assert.Equal("获得五星角色 · 芙宁娜", display.Title);
        Assert.Contains("五星角色", display.Title);
        Assert.Equal("芙宁娜", display.ItemText);
    }

    [Fact]
    public void GachaTitle_AppendsMilestoneWording()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            itemId: "10000089",
            rankType: 5,
            milestone: "first-acquisition",
            timelines: ChronicleTimeline.Main | ChronicleTimeline.Character);

        Assert.Equal(
            "获得五星角色 · 未知物品（首次获取）",
            ChronicleEntryDisplay.FromEntry(entry).Title);
    }

    [Fact]
    public void WeaponEntry_UsesWeaponWording()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            itemId: "11514",
            rankType: 5,
            timelines: ChronicleTimeline.Main | ChronicleTimeline.Weapon,
            arguments: ChronicleTestEntries.Arguments(
                ("source_scope", "local-gacha-records")));

        Assert.Equal(
            "获得五星武器 · 未知物品",
            ChronicleEntryDisplay.FromEntry(entry).Title);
    }

    [Fact]
    public void ChallengeTitles_UseModeAndSeason()
    {
        ChronicleEntry observation = ChronicleTestEntries.Create(
            titleKey: ChronicleKeyCatalog.ChallengeObservationTitle,
            kind: ChronicleEntryKind.ChallengeObservation,
            domain: ChronicleDomain.Challenge,
            timelines: ChronicleTimeline.Challenge,
            granularity: ChronicleGranularity.Detailed,
            arguments: ChronicleTestEntries.Arguments(
                ("mode", "ImaginariumTheater"),
                ("season_id", "31")),
            time: ChronicleTestEntries.ObservedOnly(
                ChronicleTestEntries.SampleTime));

        ChronicleEntry completed = ChronicleTestEntries.Create(
            titleKey: ChronicleKeyCatalog.ChallengeCompletedTitle,
            kind: ChronicleEntryKind.ChallengeCompleted,
            domain: ChronicleDomain.Challenge,
            timelines: ChronicleTimeline.Main | ChronicleTimeline.Challenge,
            milestone: "challenge-completed",
            arguments: ChronicleTestEntries.Arguments(
                ("mode", "SpiralAbyss"),
                ("season_id", "12")),
            seed: "2");

        Assert.Equal(
            "幻想真境剧诗 · 第 31 期 · 挑战观察",
            ChronicleEntryDisplay.FromEntry(observation).Title);
        Assert.Equal(
            "完成深境螺旋 · 第 12 期",
            ChronicleEntryDisplay.FromEntry(completed).Title);
    }

    [Fact]
    public void InventoryTitles_DescribeObservationCountAndStateChange()
    {
        ChronicleEntry observation = ChronicleTestEntries.Create(
            titleKey: ChronicleKeyCatalog.InventoryObservationTitle,
            kind: ChronicleEntryKind.StateObservation,
            domain: ChronicleDomain.Inventory,
            timelines: ChronicleTimeline.Character,
            granularity: ChronicleGranularity.Detailed,
            arguments: ChronicleTestEntries.Arguments(
                ("item_kind", "Character"),
                ("entry_count", "3")),
            time: ChronicleTestEntries.ObservedOnly(
                ChronicleTestEntries.SampleTime));

        ChronicleEntry change = ChronicleTestEntries.Create(
            titleKey: ChronicleKeyCatalog.InventoryStateChangeTitle,
            kind: ChronicleEntryKind.StateChange,
            domain: ChronicleDomain.Inventory,
            timelines: ChronicleTimeline.Character,
            granularity: ChronicleGranularity.Detailed,
            seed: "2",
            arguments: ChronicleTestEntries.Arguments(
                ("item_key", "1:char-a"),
                ("change", "added")),
            time: ChronicleTestEntries.ObservedOnly(
                ChronicleTestEntries.SampleTime));

        var context = new ChronicleDisplayContext
        {
            ItemNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1:char-a"] = "班尼特",
            },
        };
        Assert.Equal(
            "角色状态观察 · 3 条",
            ChronicleEntryDisplay.FromEntry(observation).Title);
        Assert.Equal(
            "角色状态变化 · 新增 · 班尼特",
            ChronicleEntryDisplay.FromEntry(change, context).Title);
    }

    [Fact]
    public void AchievementTitle_UsesCatalogIdWithoutFabricatingName()
    {
        ChronicleEntry entry = ChronicleTestEntries.Create(
            titleKey: ChronicleKeyCatalog.AchievementUnlockedTitle,
            kind: ChronicleEntryKind.AchievementUnlocked,
            domain: ChronicleDomain.Achievement,
            milestone: "important-achievement",
            arguments: ChronicleTestEntries.Arguments(
                ("achievement_id", "80001")),
            time: ChronicleTestEntries.Instant(ChronicleTestEntries.SampleTime));

        Assert.Equal(
            "解锁重要成就 · 编号 80001",
            ChronicleEntryDisplay.FromEntry(entry).Title);
    }

    [Fact]
    public void UnknownEnums_RenderAsUnknownInsteadOfRawNames()
    {
        Assert.Equal("未知来源", ChronicleDisplayText.Origin((DataOrigin)99));
        Assert.Equal("未知", ChronicleDisplayText.Confidence((DataConfidence)99));
        Assert.Equal("未知", ChronicleDisplayText.Completeness((DataCompleteness)99));
        Assert.Equal("未知领域", ChronicleDisplayText.Domain((ChronicleDomain)99));
        Assert.Equal("未知", ChronicleDisplayText.EntryKind((ChronicleEntryKind)99));
        Assert.Equal("未知时间线", ChronicleDisplayText.Timeline((ChronicleTimeline)99));
        Assert.Equal("无排序锚点", ChronicleDisplayText.SortBasis((ChronicleSortBasis)99));
        Assert.Equal("未知挑战模式", ChronicleDisplayText.ChallengeMode("FutureMode"));
    }

    [Fact]
    public void EveryKnownEvidenceReason_MapsToSpecificWording()
    {
        foreach (string key in ChronicleKeyCatalog.KnownEvidenceReasonKeys)
        {
            string text = ChronicleDisplayText.EvidenceReason(key);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.DoesNotContain("不可用", text, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyList<ChronicleEntry> ContributorEntries()
    {
        var entries = new List<ChronicleEntry>();
        entries.AddRange(GachaEntries(declaredComplete: true));
        entries.AddRange(GachaEntries(declaredComplete: false));
        entries.AddRange(InventoryEntries());
        entries.AddRange(ChallengeEntries());
        entries.AddRange(AchievementEntries());
        return entries;
    }

    private static IReadOnlyList<ChronicleEntry> GachaEntries(bool declaredComplete)
    {
        var records = new List<GachaRecord>();
        for (int index = 1; index <= 7; index++)
        {
            records.Add(Record($"char4-{index}", "char-four", 4, index));
        }
        records.Add(Record("char5-1", "char-five", 5, 10));
        records.Add(Record("weap5-1", "weap-five", 5, 11));
        records.Add(Record("weap4-1", "weap-four", 4, 12));
        records.Add(Record("weap3-1", "weap-three", 3, 13));

        var manual = new ManualAcquisition(
            new ManualAcquisitionId(
                Guid.Parse("22222222-2222-4222-8222-222222222222")),
            AccountId,
            InventoryItemKind.Character,
            "char-four",
            OccurrenceTime.At(Time(20), TimePrecision.Second),
            ManualAcquisitionReason.Other,
            "synthetic manual acquisition",
            new RecordProvenance(DataOrigin.UserEntered));
        return new GachaAcquisitionChronicleContributor().Project(
            AccountId,
            records,
            [new ManualAcquisitionState(
                manual,
                new FactVersion(1),
                Guid.Parse("33333333-3333-4333-8333-333333333333"))],
            Catalog(),
            new ChronicleProjectionEvidence(
                DeclaresCompleteGachaHistory: declaredComplete,
                DeclarationReference: declaredComplete ? "synthetic" : null));
    }

    private static IReadOnlyList<ChronicleEntry> InventoryEntries() =>
        new InventoryChronicleContributor().Project(
            AccountId,
            [
                Observation(
                    1,
                    [Item("char-a", 1, InventoryFieldCoverage.Level)]),
                Observation(
                    2,
                    [Item("char-a", 2, InventoryFieldCoverage.Level)]),
            ]);

    private static IReadOnlyList<ChronicleEntry> ChallengeEntries() =>
        new ChallengeChronicleContributor().Project(
            AccountId,
            [Challenge(1, "season-1", 3, 20)]);

    private static IReadOnlyList<ChronicleEntry> AchievementEntries() =>
        new AchievementChronicleContributor().Project(
            AccountId,
            [new ChronicleAchievementFact(
                AccountId,
                "important",
                OccurrenceTime.At(Time(30), TimePrecision.Second),
                Provenance(30))],
            new Dictionary<string, ChronicleAchievementDescriptor>
            {
                ["important"] = new("important", true),
            });

    private static GachaRecord Record(
        string externalId,
        string itemId,
        int rank,
        int second) => new(
        AccountId,
        externalId,
        ItemName: null,
        rank,
        Time(second))
        {
            ItemId = itemId,
            UigfGachaType = "301",
            Count = 1,
            Provenance = new RecordProvenance(DataOrigin.StandardImport),
        };

    private static ItemStateObservation Observation(
        int suffix,
        IReadOnlyList<InventoryStateEntry> entries) => new(
        new ItemObservationId(Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")),
        AccountId,
        InventoryItemKind.Character,
        DataCompleteness.Complete,
        entries,
        Provenance(suffix));

    private static InventoryStateEntry Item(
        string id,
        int level,
        InventoryFieldCoverage coverage) => new(
        InventoryItemKind.Character,
        id,
        null,
        null,
        level,
        null,
        coverage,
        DataCompleteness.Partial);

    private static ChallengeObservation Challenge(
        int suffix,
        string season,
        int completedSecond,
        int observedSecond) => new(
        new ChallengeObservationId(
            Guid.Parse($"00000000-0000-4000-8000-{suffix:D12}")),
        AccountId,
        ChallengeMode.SpiralAbyss,
        season,
        null,
        null,
        DataCompleteness.Partial,
        [
            new SpiralAbyssFloorResult(
                12,
                DataCompleteness.Partial,
                [
                    new SpiralAbyssChamberResult(
                        3,
                        0,
                        OccurrenceTime.At(
                            Time(completedSecond),
                            TimePrecision.Second)),
                ]),
        ],
        Provenance(observedSecond));

    private static RecordProvenance Provenance(int second) => new(
        DataOrigin.OfficialApi,
        new RecordTimestamps(ObservedAt: Time(second)));

    private static DateTimeOffset Time(int second) =>
        new(2026, 1, 1, 0, 0, second, TimeSpan.FromHours(8));

    private static IChronicleItemCatalogSnapshot Catalog() => new TestCatalog(
        [
            new ChronicleItemDescriptor(
                "char-four",
                InventoryItemKind.Character,
                4),
            new ChronicleItemDescriptor(
                "char-five",
                InventoryItemKind.Character,
                5),
            new ChronicleItemDescriptor(
                "weap-five",
                InventoryItemKind.Weapon,
                5),
            new ChronicleItemDescriptor(
                "weap-four",
                InventoryItemKind.Weapon,
                4),
            new ChronicleItemDescriptor(
                "weap-three",
                InventoryItemKind.Weapon,
                3),
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

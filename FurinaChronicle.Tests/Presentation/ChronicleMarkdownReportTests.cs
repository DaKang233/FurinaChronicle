// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Presentation.Chronicle;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ChronicleMarkdownReportTests
{
    [Fact]
    public void Write_GroupsByMonthInDescendingOrderAndMarksUndated()
    {
        var context = new ChronicleDisplayContext
        {
            DisplayTimeZone = TimeZoneInfo.Utc,
            ItemNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["10000089"] = "芙宁娜",
            },
        };
        ChronicleEntryDisplay february = ChronicleEntryDisplay.FromEntry(
            Entry("1", new DateTimeOffset(2026, 2, 5, 0, 0, 0, TimeSpan.FromHours(8))),
            context);
        ChronicleEntryDisplay january = ChronicleEntryDisplay.FromEntry(
            Entry("2", new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.FromHours(8))),
            context);
        ChronicleEntryDisplay undated = ChronicleEntryDisplay.FromEntry(
            ChronicleTestEntries.Create(
                seed: "3",
                time: ChronicleTestEntries.Undated()),
            context);

        string markdown = ChronicleMarkdownReport.Write(
            [january, undated, february],
            "测试档案",
            new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.FromHours(8)));

        Assert.Contains("# 游戏历程", markdown, StringComparison.Ordinal);
        Assert.Contains("- 数据范围：测试档案", markdown, StringComparison.Ordinal);
        Assert.Contains(
            "- 生成时间：2026-03-01 12:00:00 +08:00",
            markdown,
            StringComparison.Ordinal);
        Assert.Contains(ChronicleMarkdownReport.NotImportableNotice, markdown, StringComparison.Ordinal);
        Assert.Contains(ChronicleMarkdownReport.TimeSemanticsNotice, markdown, StringComparison.Ordinal);
        Assert.Contains("## 2026 年 02 月", markdown, StringComparison.Ordinal);
        Assert.Contains("## 2026 年 01 月", markdown, StringComparison.Ordinal);
        Assert.Contains("## 未注明时间", markdown, StringComparison.Ordinal);
        Assert.Contains("获得五星角色 · 芙宁娜", markdown, StringComparison.Ordinal);

        int februaryIndex = markdown.IndexOf("## 2026 年 02 月", StringComparison.Ordinal);
        int januaryIndex = markdown.IndexOf("## 2026 年 01 月", StringComparison.Ordinal);
        int undatedIndex = markdown.IndexOf("## 未注明时间", StringComparison.Ordinal);
        Assert.True(februaryIndex < januaryIndex);
        Assert.True(januaryIndex < undatedIndex);
    }

    [Fact]
    public void Write_DoesNotLeakRawLocalizationKeys()
    {
        ChronicleEntryDisplay display = ChronicleEntryDisplay.FromEntry(
            ChronicleTestEntries.Create(
                titleKey: "chronicle.future.unknown",
                evidence: new ChronicleEvidence(
                    ChronicleEvidenceStatus.Unknown,
                    ChronicleEvidenceScope.Unknown,
                    "chronicle.evidence.future-unknown",
                    [])));

        string markdown = ChronicleMarkdownReport.Write([display]);

        Assert.DoesNotContain("chronicle.", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmptyCollectionStillProducesAUsableHeader()
    {
        string markdown = ChronicleMarkdownReport.Write([]);

        Assert.Contains("# 游戏历程", markdown, StringComparison.Ordinal);
        Assert.Contains("- 数据范围：当前页面选择", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("##", markdown, StringComparison.Ordinal);
    }

    private static ChronicleEntry Entry(string seed, DateTimeOffset time) =>
        ChronicleTestEntries.Create(
            seed: seed,
            itemId: "10000089",
            rankType: 5,
            timelines: ChronicleTimeline.Main | ChronicleTimeline.Character,
            time: ChronicleTestEntries.Instant(time));
}

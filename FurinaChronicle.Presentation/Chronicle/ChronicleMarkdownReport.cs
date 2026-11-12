// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Builds a localized, human-readable Markdown report from display entries.
/// The report is intentionally not re-importable; it never replaces a domain
/// export and never changes the underlying facts.
/// </summary>
public static class ChronicleMarkdownReport
{
    public const string NotImportableNotice =
        "按当前筛选条件生成的可读报告，不能重新导入；导出不改变任何领域事实。";

    public const string TimeSemanticsNotice =
        "时间语义按条目分别标注：发生于、观察于、采集于与未注明时间互不替代。";

    public static string Write(
        IEnumerable<ChronicleEntryDisplay> entries,
        string? dataScope = null,
        DateTimeOffset? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var builder = new StringBuilder();
        builder.AppendLine("# 游戏历程");
        builder.AppendLine();
        builder.AppendLine($"- 数据范围：{dataScope ?? "当前页面选择"}");
        if (generatedAt is { } generated)
        {
            builder.AppendLine(
                $"- 生成时间：{generated.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}");
        }
        builder.AppendLine($"- 说明：{NotImportableNotice}");
        builder.AppendLine($"- 时间：{TimeSemanticsNotice}");
        builder.AppendLine();

        string? currentMonth = null;
        foreach (ChronicleEntryDisplay entry in Order(entries))
        {
            if (!string.Equals(currentMonth, entry.MonthKey, StringComparison.Ordinal))
            {
                currentMonth = entry.MonthKey;
                builder.AppendLine($"## {entry.MonthHeading}");
                builder.AppendLine();
            }

            builder.AppendLine($"- **{entry.Time.PrimaryText}** {entry.Title}");
            foreach (string detail in entry.Details)
            {
                builder.AppendLine($"  - {detail}");
            }
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static IReadOnlyList<ChronicleEntryDisplay> Order(
        IEnumerable<ChronicleEntryDisplay> entries) => entries
        .OrderBy(entry => entry.IsUndated ? 1 : 0)
        .ThenByDescending(entry => entry.SortAtUtc)
        .ThenByDescending(entry => entry.EntryId, StringComparer.Ordinal)
        .ToList();
}

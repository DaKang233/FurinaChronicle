// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.Infrastructure.Gacha.Exporting;

public sealed class GachaMarkdownExportWriter(
    IGachaItemMetadataProvider metadataProvider) : IGachaMarkdownExportWriter
{
    public async Task WriteAsync(
        Stream destination,
        GachaExportDocument document,
        GachaMarkdownExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "导出目标流不可写。",
                nameof(destination));
        }
        options.Validate();

        var resolver = new GachaExportValueResolver(metadataProvider);
        await using var writer = new StreamWriter(
            destination,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 81920,
            leaveOpen: true);

        await writer.WriteLineAsync("# FurinaChronicle 抽卡记录报告");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(
            "> 此 Markdown 仅供阅读和归档，不能重新导入；它不替代 UIGF 或完整逻辑 Archive。");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(
            $"- 档案：{Escape(document.SourceArchive?.Name ?? "未知")}");
        await writer.WriteLineAsync(
            $"- 导出时间：{FormatTime(document.ExportedAt)}");
        await writer.WriteLineAsync(
            $"- 账号数量：{document.Accounts.Count.ToString(CultureInfo.InvariantCulture)}");
        await writer.WriteLineAsync(
            $"- 记录数量：{document.RecordCount.ToString(CultureInfo.InvariantCulture)}");
        await writer.WriteLineAsync(
            $"- 完整性声明：{Escape(document.CompletenessAssertion)}（不据此推导账号全部历史完整）");
        await writer.WriteLineAsync(
            "- 时间语义：抽卡发生时间按记录中的秒级时间戳和原始 UTC 偏移显示；导出时间不会补齐未知的观察、抓取或导入时间。");

        foreach (GachaExportAccount account in document.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAccountSummaryAsync(
                writer,
                resolver,
                account,
                options.Language,
                cancellationToken);
        }

        if (options.IncludeDetails)
        {
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("# 明细");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync(
                "| UID | 记录 ID | 发生时间 | UTC 偏移 | 物品 | 物品 ID | 星级 | 类型 | 卡池 | 数量 | 来源 | 观察时间 | 抓取时间 | 导入时间 | 来源引用 |");
            await writer.WriteLineAsync(
                "|---|---|---|---|---|---|---:|---|---|---:|---|---|---|---|---|");

            foreach (GachaExportAccount account in document.Accounts)
            {
                foreach (GachaRecord record in account.Records)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string name = await resolver.GetReadableItemNameAsync(
                        record,
                        options.Language,
                        cancellationToken);
                    string itemType = await resolver.GetReadableItemTypeAsync(
                        record,
                        options.Language,
                        cancellationToken);
                    int? rank = await resolver.GetReadableRankTypeAsync(
                        record,
                        cancellationToken);
                    RecordTimestamps timestamps = record.Provenance.Timestamps;
                    await writer.WriteLineAsync(
                        $"| {Escape(account.Account.Uid)}" +
                        $" | {Escape(record.ExternalRecordId)}" +
                        $" | {Escape(record.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))}" +
                        $" | {Escape(FormatOffset(record.Time.Offset))}" +
                        $" | {Escape(name)}" +
                        $" | {Escape(ValueOrUnknown(record.ItemId))}" +
                        $" | {Escape(rank?.ToString(CultureInfo.InvariantCulture) ?? "未知")}" +
                        $" | {Escape(itemType)}" +
                        $" | {Escape(GachaExportValueResolver.GetPoolName(record, options.Language))}" +
                        $" | {record.Count.ToString(CultureInfo.InvariantCulture)}" +
                        $" | {Escape(record.Provenance.Origin.ToString())}" +
                        $" | {Escape(FormatOptionalTime(timestamps.ObservedAt))}" +
                        $" | {Escape(FormatOptionalTime(timestamps.FetchedAt))}" +
                        $" | {Escape(FormatOptionalTime(timestamps.ImportedAt))}" +
                        $" | {Escape(FormatSource(record.Provenance.Source))} |");
                }
            }
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static async Task WriteAccountSummaryAsync(
        TextWriter writer,
        GachaExportValueResolver resolver,
        GachaExportAccount account,
        string language,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(
            $"## 账号 {Escape(account.Account.Uid)}");
        await writer.WriteLineAsync();
        await writer.WriteLineAsync(
            $"- 区服：{Escape(account.Account.ServerRegion.ToString())}");
        await writer.WriteLineAsync(
            $"- 记录数：{account.Records.Count.ToString(CultureInfo.InvariantCulture)}");
        if (account.Records.Count > 0)
        {
            DateTimeOffset first = account.Records.Min(record => record.Time);
            DateTimeOffset last = account.Records.Max(record => record.Time);
            await writer.WriteLineAsync(
                $"- 时间范围：{FormatTime(first)} ～ {FormatTime(last)}");
        }
        else
        {
            await writer.WriteLineAsync("- 时间范围：无记录");
        }

        string origins = string.Join(
            "；",
            account.Records
                .GroupBy(record => record.Provenance.Origin)
                .OrderBy(group => group.Key)
                .Select(group =>
                    $"{group.Key} {group.Count().ToString(CultureInfo.InvariantCulture)}"));
        await writer.WriteLineAsync(
            $"- 来源分布：{Escape(string.IsNullOrEmpty(origins) ? "无记录" : origins)}");

        var rankCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var poolCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (GachaRecord record in account.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int? rank = await resolver.GetReadableRankTypeAsync(
                record,
                cancellationToken);
            string rankLabel = rank is int value ? $"{value} 星" : "未知星级";
            rankCounts[rankLabel] = rankCounts.GetValueOrDefault(rankLabel) + 1;
            string pool = GachaExportValueResolver.GetPoolName(record, language);
            poolCounts[pool] = poolCounts.GetValueOrDefault(pool) + 1;
        }

        await writer.WriteLineAsync(
            $"- 星级分布：{Escape(FormatCounts(rankCounts))}");
        await writer.WriteLineAsync(
            $"- 卡池分布：{Escape(FormatCounts(poolCounts))}");
    }

    private static string FormatCounts(IReadOnlyDictionary<string, int> counts) =>
        counts.Count == 0
            ? "无记录"
            : string.Join(
                "；",
                counts.Select(pair =>
                    $"{pair.Key} {pair.Value.ToString(CultureInfo.InvariantCulture)}"));

    private static string FormatSource(DataSourceReference? source)
    {
        if (source is null)
        {
            return "未知";
        }
        return string.Join(
            "；",
            new[]
            {
                source.Provider is null ? null : $"provider={source.Provider}",
                source.SourceRecordId is null
                    ? null
                    : $"record={source.SourceRecordId}",
                source.SourceSnapshotId is null
                    ? null
                    : $"snapshot={source.SourceSnapshotId}"
            }.Where(value => value is not null));
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);

    private static string FormatOptionalTime(DateTimeOffset? value) =>
        value is DateTimeOffset actual ? FormatTime(actual) : "未知";

    private static string FormatOffset(TimeSpan offset)
    {
        char sign = offset < TimeSpan.Zero ? '-' : '+';
        TimeSpan absolute = offset.Duration();
        return $"{sign}{absolute.Hours:00}:{absolute.Minutes:00}";
    }

    private static string ValueOrUnknown(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "未知" : value;

    private static string Escape(string value) =>
        value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
}

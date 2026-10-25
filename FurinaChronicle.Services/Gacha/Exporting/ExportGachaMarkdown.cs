// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Gacha.Exporting;

public sealed class ExportGachaMarkdown(
    LoadGachaExportData loadData,
    IGachaMarkdownExportWriter writer)
{
    public async Task<GachaExportResult> ExecuteAsync(
        Stream destination,
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        GachaMarkdownExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "导出目标流不可写。",
                nameof(destination));
        }

        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        GachaExportDocument document = await loadData.ExecuteAsync(
            playerArchiveId,
            gameAccountIds,
            cancellationToken);
        await writer.WriteAsync(
            destination,
            document,
            options,
            cancellationToken);
        return new GachaExportResult(
            document.Accounts.Count,
            document.RecordCount);
    }
}

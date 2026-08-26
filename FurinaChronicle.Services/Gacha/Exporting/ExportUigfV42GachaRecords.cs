namespace FurinaChronicle.Services.Gacha.Exporting;

public sealed class ExportUigfV42GachaRecords(
    LoadGachaExportData loadData,
    IUigfV42ExportWriter writer)
{
    public async Task<GachaExportResult> ExecuteAsync(
        Stream destination,
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        UigfV42ExportOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateDestination(destination);
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

    private static void ValidateDestination(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
        {
            throw new ArgumentException(
                "导出目标流不可写。",
                nameof(destination));
        }
    }
}

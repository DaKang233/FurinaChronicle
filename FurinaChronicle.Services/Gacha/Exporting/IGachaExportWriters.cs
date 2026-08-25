namespace FurinaChronicle.Services.Gacha.Exporting;

public interface IUigfV42ExportWriter
{
    Task WriteAsync(
        Stream destination,
        GachaExportDocument document,
        UigfV42ExportOptions options,
        CancellationToken cancellationToken = default);
}

public interface IGachaTableExportWriter
{
    Task WriteAsync(
        Stream destination,
        GachaExportDocument document,
        GachaTableExportOptions options,
        CancellationToken cancellationToken = default);
}

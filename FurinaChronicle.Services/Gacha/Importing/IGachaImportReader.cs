namespace FurinaChronicle.Services.Gacha.Importing;

public interface IGachaImportReader
{
    Task<GachaReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}

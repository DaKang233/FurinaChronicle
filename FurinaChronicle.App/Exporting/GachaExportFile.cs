using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.App.Exporting;

public sealed record GachaExportFile(
    string FileName,
    MemoryStream Content,
    GachaExportResult Result)
    : IDisposable
{
    public void Dispose() => Content.Dispose();
}

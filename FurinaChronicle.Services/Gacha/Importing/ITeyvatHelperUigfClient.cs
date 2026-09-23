namespace FurinaChronicle.Services.Gacha.Importing;

public sealed record TeyvatHelperUigfDownload(
    string FileName,
    byte[] Content);

public sealed record TeyvatHelperImportAvailability(
    string? SelectedRoleUid,
    bool CanAutomaticallyImport);

public interface ITeyvatHelperUigfClient
{
    Task<TeyvatHelperUigfDownload> DownloadAsync(
        string uid,
        Uri gachaUrl,
        CancellationToken cancellationToken = default);
}

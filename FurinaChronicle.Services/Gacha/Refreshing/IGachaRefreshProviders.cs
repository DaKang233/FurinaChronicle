using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Passport;

namespace FurinaChronicle.Services.Gacha.Refreshing;

public interface ISTokenGachaUrlProvider
{
    Task<Uri> CreateAsync(
        PassportAccount passportAccount,
        GameAccount gameAccount,
        CancellationToken cancellationToken = default);
}

public interface IWindowsGachaCacheUrlProvider
{
    bool IsSupported { get; }

    Task<Uri> FindAsync(
        string gameInstallationPath,
        GameServerRegion region,
        CancellationToken cancellationToken = default);
}

public interface IGachaLogClient
{
    Task<GachaRemotePage> GetPageAsync(
        Uri sourceUrl,
        string gachaType,
        string? endId,
        CancellationToken cancellationToken = default);
}

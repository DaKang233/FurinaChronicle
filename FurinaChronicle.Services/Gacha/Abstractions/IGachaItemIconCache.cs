using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaItemIconCache
{
    Task<string?> GetOrRefreshAsync(
        GachaGame game,
        string itemId,
        string? sourceUrl,
        CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}

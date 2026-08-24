using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaMetadataRefreshService
{
    Task<GachaMetadataRefreshResult> RefreshIfNeededAsync(
        GachaGame game,
        bool force = false,
        CancellationToken cancellationToken = default);
}

public sealed record GachaMetadataRefreshResult(
    bool CheckAttempted,
    bool ContentUpdated,
    bool UsedExistingCache,
    string? ContentSha256);

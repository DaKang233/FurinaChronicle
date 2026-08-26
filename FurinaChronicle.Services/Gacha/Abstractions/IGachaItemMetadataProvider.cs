using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;

namespace FurinaChronicle.Services.Gacha.Abstractions;

public interface IGachaItemMetadataProvider
{
    ValueTask<GachaItemMetadata?> FindByIdAsync(
        GachaGame game,
        string itemId,
        CancellationToken cancellationToken = default);
}

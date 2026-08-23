using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata;

public sealed class EmptyGachaItemMetadataProvider : IGachaItemMetadataProvider
{
    public ValueTask<GachaItemMetadata?> FindByIdAsync(
        GachaGame game,
        string itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<GachaItemMetadata?>(null);
    }
}

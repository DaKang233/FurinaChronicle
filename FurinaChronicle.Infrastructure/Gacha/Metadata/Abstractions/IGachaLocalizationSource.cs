using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;

public interface IGachaLocalizationSource
{
    Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> LoadAsync(
        GachaGame game,
        CancellationToken cancellationToken = default);
}

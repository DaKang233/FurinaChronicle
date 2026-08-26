using FurinaChronicle.Core.Gacha;

namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;

public interface IGachaMetadataRemoteSource
{
    Task<IReadOnlyList<GachaMetadataSourceItem>> FetchAsync(
        GachaGame game,
        CancellationToken cancellationToken = default);
}

public sealed record GachaMetadataSourceItem(
    string ItemId,
    string SourceName,
    string ItemType,
    int RankType,
    string? IconUrl);

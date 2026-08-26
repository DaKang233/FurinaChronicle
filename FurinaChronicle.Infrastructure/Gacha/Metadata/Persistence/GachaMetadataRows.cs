namespace FurinaChronicle.Infrastructure.Gacha.Metadata.Persistence;

internal sealed class GachaMetadataItemRow
{
    public int GameId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public string ItemType { get; set; } = string.Empty;

    public int? RankType { get; set; }

    public string? IconUrl { get; set; }
}

internal sealed class GachaMetadataNameRow
{
    public int GameId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public string Language { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

internal sealed class GachaMetadataStateRow
{
    public int GameId { get; set; }

    public long LastCheckUtcTicks { get; set; }

    public long LastSuccessUtcTicks { get; set; }

    public string? ContentSha256 { get; set; }
}

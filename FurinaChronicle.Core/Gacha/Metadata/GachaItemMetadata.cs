namespace FurinaChronicle.Core.Gacha.Metadata;

public sealed record GachaItemMetadata(
    GachaGame Game,
    string ItemId,
    string Name,
    string ItemType,
    int? RankType,
    string? IconUrl = null,
    IReadOnlyDictionary<string, string>? LocalizedNames = null);

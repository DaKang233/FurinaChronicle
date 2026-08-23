namespace FurinaChronicle.Services.Gacha.Importing;

public sealed record GachaImportResult(
    int TotalCount,
    int ImportedCount,
    int DuplicateCount,
    int InvalidCount,
    int IgnoredCount,
    int CreatedAccountCount);

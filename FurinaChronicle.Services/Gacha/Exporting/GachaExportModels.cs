using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;

namespace FurinaChronicle.Services.Gacha.Exporting;

public sealed record GachaExportDocument(
    DateTimeOffset ExportedAt,
    IReadOnlyList<GachaExportAccount> Accounts)
{
    public int RecordCount => Accounts.Sum(account => account.Records.Count);
}

public sealed record GachaExportAccount(
    GameAccount Account,
    int Timezone,
    IReadOnlyList<WishRecord> Records);

public sealed record GachaExportResult(
    int AccountCount,
    int RecordCount);

public static class GachaExportLanguages
{
    public const string SimplifiedChinese = "zh-cn";
    public const string TraditionalChinese = "zh-tw";
    public const string English = "en-us";

    public static IReadOnlyList<string> Supported { get; } =
        [SimplifiedChinese, TraditionalChinese, English];

    public static void Validate(string language)
    {
        if (!Supported.Contains(language, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"Unsupported export language: {language}.",
                nameof(language));
        }
    }
}

public sealed record UigfV42ExportOptions
{
    public string Language { get; init; } =
        GachaExportLanguages.SimplifiedChinese;

    public bool IncludeInfoLanguage { get; init; } = true;

    public bool IncludeAccountLanguage { get; init; }

    public bool IncludeCount { get; init; } = true;

    public bool IncludeName { get; init; } = true;

    public bool IncludeItemType { get; init; } = true;

    public bool IncludeRankType { get; init; } = true;

    public bool IncludeEmptyHk4eUgc { get; init; } = true;

    public static UigfV42ExportOptions Compatible { get; } = new();

    public static UigfV42ExportOptions Minimal { get; } = new()
    {
        IncludeInfoLanguage = false,
        IncludeCount = false,
        IncludeName = false,
        IncludeItemType = false,
        IncludeRankType = false,
        IncludeEmptyHk4eUgc = false
    };

    public void Validate()
    {
        GachaExportLanguages.Validate(Language);
    }
}

public enum GachaTableFormat
{
    Csv,
    Xlsx
}

public sealed record GachaTableExportOptions(
    GachaTableFormat Format,
    string Language)
{
    public void Validate()
    {
        GachaExportLanguages.Validate(Language);
    }
}

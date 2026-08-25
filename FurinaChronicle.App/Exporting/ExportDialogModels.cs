using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.App.Exporting;

public sealed record UigfExportDialogResult(
    IReadOnlyList<Guid> GameAccountIds,
    UigfV42ExportOptions Options);

public sealed record TableExportDialogResult(
    IReadOnlyList<Guid> GameAccountIds,
    GachaTableExportOptions Options);

internal sealed record ExportLanguageOption(
    string DisplayName,
    string Code)
{
    public override string ToString() => DisplayName;

    public static ExportLanguageOption[] All { get; } =
    [
        new("简体中文", GachaExportLanguages.SimplifiedChinese),
        new("繁體中文", GachaExportLanguages.TraditionalChinese),
        new("English", GachaExportLanguages.English)
    ];
}

internal sealed record TableFormatOption(
    string DisplayName,
    GachaTableFormat Format)
{
    public override string ToString() => DisplayName;

    public static TableFormatOption[] All { get; } =
    [
        new("Excel 工作簿（.xlsx）", GachaTableFormat.Xlsx),
        new("CSV（.csv）", GachaTableFormat.Csv)
    ];
}

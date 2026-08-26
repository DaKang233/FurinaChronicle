namespace FurinaChronicle.Services.Gacha.Importing;

public sealed record GachaSourceInfo(
    long ExportTimestamp,
    string ExportApp,
    string ExportAppVersion,
    string FormatVersion);

public sealed record GachaSourceRecord(
    string ExternalRecordId,
    string ItemId,
    string GachaType,
    string UigfGachaType,
    DateTimeOffset Time,
    string? ItemName,
    string? ItemType,
    int? RankType,
    int Count);

public sealed record GachaSourceAccount(
    string Uid,
    int Timezone,
    string? Language,
    IReadOnlyList<GachaSourceRecord> Records);

public sealed record GachaReadError(
    string? Uid,
    int? RecordIndex,
    string Code,
    string Message);

public sealed record GachaReadResult(
    GachaSourceInfo Info,
    IReadOnlyList<GachaSourceAccount> Accounts,
    IReadOnlyList<GachaReadError> Errors,
    int TotalRecordCount);

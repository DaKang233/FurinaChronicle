using System.Globalization;
using System.Text.Json;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;

public sealed class UigfV42GachaReader : IGachaImportReader
{
    private const string SupportedVersion = "v4.2";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly HashSet<string> Languages =
    [
        "de-de", "en-us", "es-es", "fr-fr", "id-id", "it-it", "ja-jp",
        "ko-kr", "pt-pt", "ru-ru", "th-th", "tr-tr", "vi-vn", "zh-cn", "zh-tw"
    ];

    private static readonly HashSet<string> GachaTypes =
        ["100", "200", "301", "302", "400", "500"];

    private static readonly HashSet<string> UigfGachaTypes =
        ["100", "200", "301", "302", "500"];

    public async Task<GachaReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException("The input stream is not readable.", nameof(source));
        }

        UigfV42DocumentDto? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<UigfV42DocumentDto>(
                source,
                cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new GachaImportFormatException("The file is not valid UIGF JSON.", exception);
        }

        GachaSourceInfo info = ReadInfo(document?.Info);
        List<GachaSourceAccount> accounts = [];
        List<GachaReadError> errors = [];
        int totalRecordCount = 0;

        foreach (UigfV42Hk4eAccountDto? accountDto in document?.Hk4e ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();

            int sourceCount = accountDto?.List?.Count ?? 0;
            totalRecordCount += sourceCount;

            if (accountDto is null)
            {
                errors.Add(new(null, null, "null_account", "An hk4e account cannot be null."));
                continue;
            }

            if (!TryReadStringOrInteger(accountDto.Uid, out string? uid) ||
                !GameUidValidation.IsValidUid(uid))
            {
                AddAccountErrors(
                    errors,
                    uid,
                    sourceCount,
                    "invalid_uid",
                    "UID must contain 9 to 10 digits and map to a known server.");
                continue;
            }

            if (accountDto.Timezone is not int timezone || timezone is < -14 or > 14)
            {
                AddAccountErrors(
                    errors,
                    uid,
                    sourceCount,
                    "invalid_timezone",
                    "timezone must be an integer from -14 to 14.");
                continue;
            }

            string? language = NormalizeOptional(accountDto.Language);
            if (language is not null && !Languages.Contains(language))
            {
                AddAccountErrors(
                    errors,
                    uid,
                    sourceCount,
                    "invalid_language",
                    $"Unsupported language code: {language}.");
                continue;
            }

            if (accountDto.List is null)
            {
                errors.Add(new(uid, null, "missing_list", "The account is missing its list array."));
                continue;
            }

            List<GachaSourceRecord> records = [];
            for (int index = 0; index < accountDto.List.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                UigfV42Hk4eRecordDto? recordDto = accountDto.List[index];
                int recordIndex = index + 1;

                if (TryReadRecord(
                    recordDto,
                    timezone,
                    uid!,
                    recordIndex,
                    out GachaSourceRecord? record,
                    out GachaReadError? error))
                {
                    records.Add(record!);
                }
                else
                {
                    errors.Add(error!);
                }
            }

            accounts.Add(new GachaSourceAccount(uid!, timezone, language, records));
        }

        return new GachaReadResult(info, accounts, errors, totalRecordCount);
    }

    private static GachaSourceInfo ReadInfo(UigfV42InfoDto? dto)
    {
        if (dto is null)
        {
            throw new GachaImportFormatException("The UIGF file is missing info.");
        }

        if (!TryReadInt64(dto.ExportTimestamp, out long exportTimestamp) ||
            exportTimestamp < 0)
        {
            throw new GachaImportFormatException("info.export_timestamp is invalid.");
        }

        string? exportApp = NormalizeOptional(dto.ExportApp);
        string? exportAppVersion = NormalizeOptional(dto.ExportAppVersion);
        string? version = NormalizeOptional(dto.Version);

        if (exportApp is null || exportAppVersion is null)
        {
            throw new GachaImportFormatException("The info object is missing exporter details.");
        }

        if (!string.Equals(version, SupportedVersion, StringComparison.Ordinal))
        {
            throw new GachaImportFormatException(
                $"Only UIGF {SupportedVersion} is supported. Actual version: {version ?? "missing"}.");
        }

        return new GachaSourceInfo(
            exportTimestamp,
            exportApp,
            exportAppVersion,
            version!);
    }

    private static bool TryReadRecord(
        UigfV42Hk4eRecordDto? dto,
        int timezone,
        string uid,
        int recordIndex,
        out GachaSourceRecord? record,
        out GachaReadError? error)
    {
        record = null;
        error = null;

        if (dto is null)
        {
            error = new(uid, recordIndex, "null_record", "A record cannot be null.");
            return false;
        }

        string? id = NormalizeOptional(dto.Id);
        if (id is null || id.Length > 19 || !id.All(char.IsAsciiDigit))
        {
            error = new(uid, recordIndex, "invalid_id", "id must contain at most 19 digits.");
            return false;
        }

        string? itemId = NormalizeOptional(dto.ItemId);
        if (itemId is null)
        {
            error = new(uid, recordIndex, "missing_item_id", "item_id is required.");
            return false;
        }

        string? gachaType = NormalizeOptional(dto.GachaType);
        if (gachaType is null || !GachaTypes.Contains(gachaType))
        {
            error = new(uid, recordIndex, "invalid_gacha_type", $"Invalid gacha_type: {gachaType}.");
            return false;
        }

        string? uigfGachaType = NormalizeOptional(dto.UigfGachaType);
        if (uigfGachaType is null || !UigfGachaTypes.Contains(uigfGachaType))
        {
            error = new(uid, recordIndex, "invalid_uigf_gacha_type", $"Invalid uigf_gacha_type: {uigfGachaType}.");
            return false;
        }

        if (!DateTime.TryParseExact(
            NormalizeOptional(dto.Time),
            TimeFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out DateTime localTime))
        {
            error = new(uid, recordIndex, "invalid_time", $"Invalid time: {dto.Time}.");
            return false;
        }

        int count = 1;
        string? countText = NormalizeOptional(dto.Count);
        if (countText is not null &&
            (!int.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out count) ||
             count <= 0))
        {
            error = new(uid, recordIndex, "invalid_count", $"Invalid count: {countText}.");
            return false;
        }

        int? rankType = null;
        string? rankText = NormalizeOptional(dto.RankType);
        if (rankText is not null)
        {
            if (!int.TryParse(rankText, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedRank) ||
                parsedRank is < 3 or > 5)
            {
                error = new(uid, recordIndex, "invalid_rank_type", $"Invalid rank_type: {rankText}.");
                return false;
            }

            rankType = parsedRank;
        }

        TimeSpan timeOffset = TimeSpan.FromHours(timezone);
        long utcTicks = localTime.Ticks - timeOffset.Ticks;
        if (utcTicks < DateTime.MinValue.Ticks ||
            utcTicks > DateTime.MaxValue.Ticks)
        {
            error = new(
                uid,
                recordIndex,
                "invalid_time",
                $"Time {dto.Time} cannot be represented with timezone {timezone}.");
            return false;
        }

        var time = new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            timeOffset);

        record = new GachaSourceRecord(
            id,
            itemId,
            gachaType,
            uigfGachaType,
            time,
            NormalizeOptional(dto.Name),
            NormalizeOptional(dto.ItemType),
            rankType,
            count);
        return true;
    }

    private static void AddAccountErrors(
        ICollection<GachaReadError> errors,
        string? uid,
        int recordCount,
        string code,
        string message)
    {
        if (recordCount == 0)
        {
            errors.Add(new(uid, null, code, message));
            return;
        }

        for (int index = 1; index <= recordCount; index++)
        {
            errors.Add(new(uid, index, code, message));
        }
    }

    private static bool TryReadStringOrInteger(JsonElement element, out string? value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(element.GetString()),
            JsonValueKind.Number => element.GetRawText(),
            _ => null
        };

        return value is not null && value.All(char.IsAsciiDigit);
    }

    private static bool TryReadInt64(JsonElement element, out long value)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.TryGetInt64(out value);
        }

        return long.TryParse(
            element.ValueKind == JsonValueKind.String ? element.GetString() : null,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

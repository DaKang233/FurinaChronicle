// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

internal static class GachaPortableRules
{
    public const string Format = "furina-gacha-portable";
    public const string CurrentVersion = "1.0";
    public const string Game = "genshin";
    public const string ManifestPath = "manifest.json";
    public const string UigfVersion = "v4.2";
    public const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private static readonly HashSet<string> GachaTypes =
        ["100", "200", "301", "302", "400", "500"];

    private static readonly HashSet<string> UigfGachaTypes =
        ["100", "200", "301", "302", "500"];

    public static string GetAccountDirectory(Guid accountReference) =>
        $"accounts/{accountReference:D}".ToLowerInvariant();

    public static string GetUigfPath(Guid accountReference) =>
        $"{GetAccountDirectory(accountReference)}/gacha.uigf.json";

    public static string GetSupplementPath(Guid accountReference) =>
        $"{GetAccountDirectory(accountReference)}/supplement.ndjson";

    public static void ValidateNaturalIdentity(
        GameRoleNaturalIdentity identity,
        string? path = null)
    {
        ArgumentNullException.ThrowIfNull(identity);

        GameServerRegion region = (identity.GameBiz, identity.Server) switch
        {
            (GenshinGameRoleIdentity.MainlandGameBiz, "cn_gf01") =>
                GameServerRegion.ChinaOfficial,
            (GenshinGameRoleIdentity.MainlandGameBiz, "cn_qd01") =>
                GameServerRegion.ChinaBilibili,
            (GenshinGameRoleIdentity.GlobalGameBiz, "os_usa") =>
                GameServerRegion.America,
            (GenshinGameRoleIdentity.GlobalGameBiz, "os_euro") =>
                GameServerRegion.Europe,
            (GenshinGameRoleIdentity.GlobalGameBiz, "os_asia") =>
                GameServerRegion.Asia,
            (GenshinGameRoleIdentity.GlobalGameBiz, "os_cht") =>
                GameServerRegion.TaiwanHongKongMacao,
            _ => throw Error(
                GachaPortableErrorCode.UnsupportedAccount,
                "The natural identity is not a supported Genshin account.",
                path),
        };

        try
        {
            GameRoleNaturalIdentity canonical =
                GenshinGameRoleIdentity.Create(identity.Uid, region);
            if (canonical != identity)
            {
                throw Error(
                    GachaPortableErrorCode.UnsupportedAccount,
                    "The natural identity is not canonical.",
                    path);
            }
        }
        catch (ArgumentException exception)
        {
            throw Error(
                GachaPortableErrorCode.UnsupportedAccount,
                "The natural identity conflicts with the UID-derived server.",
                path,
                innerException: exception);
        }
    }

    public static void ValidateRecord(
        GachaRecord record,
        Guid accountReference,
        string? path = null)
    {
        if (record.GameAccountId != accountReference)
        {
            throw Error(
                GachaPortableErrorCode.InvalidReference,
                "The record owner does not match account_ref.",
                path,
                record.ExternalRecordId);
        }

        if (string.IsNullOrWhiteSpace(record.ExternalRecordId) ||
            record.ExternalRecordId.Length > 19 ||
            !record.ExternalRecordId.All(char.IsAsciiDigit))
        {
            throw Error(
                GachaPortableErrorCode.MissingRequiredField,
                "external_record_id must contain at most 19 ASCII digits.",
                path,
                record.ExternalRecordId);
        }

        Require(record.ItemId, "item_id", record, path);
        string gachaType = Require(record.GachaType, "gacha_type", record, path);
        string uigfGachaType = Require(
            record.UigfGachaType,
            "uigf_gacha_type",
            record,
            path);
        if (!GachaTypes.Contains(gachaType) ||
            !UigfGachaTypes.Contains(uigfGachaType))
        {
            throw Error(
                GachaPortableErrorCode.MissingRequiredField,
                "The record contains an unsupported Gacha type.",
                path,
                record.ExternalRecordId);
        }

        if (record.Count <= 0 ||
            record.RankType is not null and (< 3 or > 5))
        {
            throw Error(
                GachaPortableErrorCode.InvalidPackage,
                "The record count or rank is invalid.",
                path,
                record.ExternalRecordId);
        }

        if (record.Provenance.Source is not null ||
            record.Provenance.Timestamps.ObservedAt is not null)
        {
            throw Error(
                GachaPortableErrorCode.InvalidPackage,
                "Portable v1 cannot preserve this record's source reference or observed_at.",
                path,
                record.ExternalRecordId);
        }
    }

    public static string FormatOrigin(DataOrigin origin) => origin switch
    {
        DataOrigin.Unknown => "unknown",
        DataOrigin.OfficialApi => "official_api",
        DataOrigin.StandardImport => "standard_import",
        DataOrigin.FurinaImport => "furina_import",
        DataOrigin.LocalObservation => "local_observation",
        DataOrigin.LocalCollector => "local_collector",
        DataOrigin.UserEntered => "user_entered",
        DataOrigin.Derived => "derived",
        _ => throw Error(
            GachaPortableErrorCode.UnsupportedEnum,
            $"Unsupported data origin: {origin}."),
    };

    public static DataOrigin ParseOrigin(
        string? value,
        string path,
        string? recordId) => value switch
    {
        "unknown" => DataOrigin.Unknown,
        "official_api" => DataOrigin.OfficialApi,
        "standard_import" => DataOrigin.StandardImport,
        "furina_import" => DataOrigin.FurinaImport,
        "local_observation" => DataOrigin.LocalObservation,
        "local_collector" => DataOrigin.LocalCollector,
        "user_entered" => DataOrigin.UserEntered,
        "derived" => DataOrigin.Derived,
        _ => throw Error(
            GachaPortableErrorCode.UnsupportedEnum,
            $"Unsupported data origin: {value ?? "missing"}.",
            path,
            recordId),
    };

    public static void ValidateEntryPath(string path)
    {
        if (string.IsNullOrEmpty(path) ||
            path.StartsWith("/", StringComparison.Ordinal) ||
            path.Contains('\\') ||
            path.Split('/').Any(
                segment => segment.Length == 0 || segment is "." or ".."))
        {
            throw Error(
                GachaPortableErrorCode.InvalidPath,
                $"Invalid ZIP entry path: {path}.",
                path);
        }
    }

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } &&
        value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static GachaPortableException Error(
        GachaPortableErrorCode code,
        string message,
        string? path = null,
        string? recordId = null,
        Exception? innerException = null) =>
        new(code, message, path, recordId, innerException);

    private static string Require(
        string? value,
        string field,
        GachaRecord record,
        string? path)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw Error(
            GachaPortableErrorCode.MissingRequiredField,
            $"Record {record.ExternalRecordId} is missing {field}.",
            path,
            record.ExternalRecordId);
    }
}

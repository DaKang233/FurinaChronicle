using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Infrastructure.Gacha.Metadata.Abstractions;
using FurinaChronicle.Infrastructure.Gacha.Uigf.V4_2;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Infrastructure.Gacha.Uigf.Compatibility;

/// <summary>
/// Accepts historical Genshin UIGF documents and normalizes them to UIGF v4.2
/// before delegating validation and mapping to <see cref="UigfV42GachaReader"/>.
/// </summary>
public sealed class UigfCompatibleGachaReader(
    UigfV42GachaReader v42Reader,
    IGachaLocalizationSource localizationSource,
    TimeProvider timeProvider) : IGachaImportReader
{
    private const string CurrentVersion = "v4.2";

    private static readonly HashSet<string> LegacyVersions =
        ["v2.2", "v2.3", "v2.4", "v3.0"];

    private static readonly HashSet<string> CompatibleV4Versions =
        ["v4.0", "v4.1", CurrentVersion];

    private static readonly IReadOnlyDictionary<string, string>
        UigfToEmbeddedLanguage =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["de-de"] = "de",
                ["en-us"] = "en",
                ["es-es"] = "es",
                ["fr-fr"] = "fr",
                ["id-id"] = "id",
                ["ja-jp"] = "jp",
                ["ko-kr"] = "kr",
                ["pt-pt"] = "pt",
                ["ru-ru"] = "ru",
                ["th-th"] = "th",
                ["vi-vn"] = "vi",
                ["zh-cn"] = "chs",
                ["zh-tw"] = "cht"
            };

    public async Task<GachaReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The input stream is not readable.",
                nameof(source));
        }

        JsonObject root = await ParseRootAsync(source, cancellationToken);
        JsonObject info = root["info"] as JsonObject ??
            throw new GachaImportFormatException(
                "The UIGF file is missing info.");

        string? legacyVersion = ReadScalar(info["uigf_version"]);
        string? v4Version = ReadScalar(info["version"]);

        JsonObject normalized;
        if (legacyVersion is not null)
        {
            if (!LegacyVersions.Contains(legacyVersion))
            {
                throw UnsupportedVersion(legacyVersion);
            }

            normalized = await UpgradeLegacyAsync(
                root,
                info,
                legacyVersion,
                cancellationToken);
        }
        else if (v4Version is not null)
        {
            if (!CompatibleV4Versions.Contains(v4Version))
            {
                throw UnsupportedVersion(v4Version);
            }

            normalized = (JsonObject)root.DeepClone();
            ((JsonObject)normalized["info"]!)["version"] = CurrentVersion;
        }
        else if (info.ContainsKey("srgf_version"))
        {
            throw new GachaImportFormatException(
                "SRGF is not a Genshin Impact UIGF file.");
        }
        else
        {
            throw new GachaImportFormatException(
                "The JSON file does not contain a recognized UIGF version.");
        }

        await using var normalizedStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            normalizedStream,
            normalized,
            cancellationToken: cancellationToken);
        normalizedStream.Position = 0;

        return await v42Reader.ReadAsync(
            normalizedStream,
            cancellationToken);
    }

    private async Task<JsonObject> UpgradeLegacyAsync(
        JsonObject root,
        JsonObject info,
        string version,
        CancellationToken cancellationToken)
    {
        string uid = ReadScalar(info["uid"]) ??
            throw new GachaImportFormatException(
                $"UIGF {version} info.uid is missing.");

        JsonArray sourceRecords = root["list"] as JsonArray ??
            throw new GachaImportFormatException(
                $"UIGF {version} is missing its list array.");

        string? language = NormalizeUigfLanguage(
            ReadScalar(info["lang"]));
        int timezone = ReadTimezone(info, uid);

        LegacyNameIndex? nameIndex = null;
        if (string.Equals(version, "v2.2", StringComparison.Ordinal) &&
            sourceRecords.Any(NeedsItemId))
        {
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>
                localizations = await localizationSource.LoadAsync(
                    GachaGame.GenshinImpact,
                    cancellationToken);
            nameIndex = LegacyNameIndex.Create(localizations);
        }

        var upgradedRecords = new JsonArray();
        for (int index = 0; index < sourceRecords.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            JsonNode? sourceRecord = sourceRecords[index];
            if (sourceRecord is not JsonObject record)
            {
                upgradedRecords.Add(sourceRecord?.DeepClone());
                continue;
            }

            var upgradedRecord = (JsonObject)record.DeepClone();
            string? itemId = ReadScalar(record["item_id"]);
            if (itemId is null &&
                string.Equals(version, "v2.2", StringComparison.Ordinal))
            {
                string? itemName = ReadScalar(record["name"]);
                string? recordLanguage = NormalizeUigfLanguage(
                    ReadScalar(record["lang"]));
                itemId = nameIndex?.Resolve(
                    itemName,
                    recordLanguage ?? language);

                if (itemId is null)
                {
                    throw new GachaImportFormatException(
                        $"UIGF v2.2 record {index + 1} has no item_id, " +
                        $"and item name '{itemName ?? "missing"}' could not be resolved.");
                }
            }

            if (itemId is not null)
            {
                upgradedRecord["item_id"] = itemId;
            }

            upgradedRecords.Add(upgradedRecord);
        }

        long exportTimestamp = ReadExportTimestamp(info);
        string exportApp = ReadScalar(info["export_app"]) ??
            "FurinaChronicle";
        string exportAppVersion =
            ReadScalar(info["export_app_version"]) ?? "development";

        var upgradedInfo = new JsonObject
        {
            ["export_timestamp"] = exportTimestamp,
            ["export_app"] = exportApp,
            ["export_app_version"] = exportAppVersion,
            ["version"] = CurrentVersion
        };
        if (language is not null)
        {
            upgradedInfo["lang"] = language;
        }

        var account = new JsonObject
        {
            ["uid"] = uid,
            ["timezone"] = timezone,
            ["list"] = upgradedRecords
        };
        if (language is not null)
        {
            account["lang"] = language;
        }

        return new JsonObject
        {
            ["info"] = upgradedInfo,
            ["hk4e"] = new JsonArray(account)
        };
    }

    private long ReadExportTimestamp(JsonObject info)
    {
        if (!TryReadInt64(info["export_timestamp"], out long timestamp) ||
            timestamp < 0)
        {
            return timeProvider.GetUtcNow().ToUnixTimeSeconds();
        }

        // UIGF 2.x exporters sometimes emitted JavaScript milliseconds.
        return timestamp >= 100_000_000_000L
            ? timestamp / 1000
            : timestamp;
    }

    private static int ReadTimezone(JsonObject info, string uid)
    {
        if (TryReadInt64(info["region_time_zone"], out long timezone) &&
            timezone is >= -14 and <= 14)
        {
            return (int)timezone;
        }

        return uid.StartsWith('6')
            ? -5
            : uid.StartsWith('7')
                ? 1
                : 8;
    }

    private static async Task<JsonObject> ParseRootAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        try
        {
            JsonNode? node = await JsonNode.ParseAsync(
                source,
                cancellationToken: cancellationToken);
            return node as JsonObject ??
                throw new GachaImportFormatException(
                    "The UIGF JSON root must be an object.");
        }
        catch (JsonException exception)
        {
            throw new GachaImportFormatException(
                "The file is not valid UIGF JSON.",
                exception);
        }
    }

    private static bool NeedsItemId(JsonNode? node)
    {
        return node is JsonObject record &&
            ReadScalar(record["item_id"]) is null;
    }

    private static string? NormalizeUigfLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        string normalized = language.Trim().ToLowerInvariant();
        return UigfToEmbeddedLanguage.ContainsKey(normalized)
            ? normalized
            : null;
    }

    private static string? ReadScalar(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out string? text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        if (value.TryGetValue<long>(out long integer))
        {
            return integer.ToString(CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static bool TryReadInt64(JsonNode? node, out long value)
    {
        string? text = ReadScalar(node);
        return long.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }

    private static GachaImportFormatException UnsupportedVersion(
        string version)
    {
        return new GachaImportFormatException(
            $"Unsupported UIGF version: {version}. " +
            "Supported Genshin versions are v2.2, v2.3, v2.4, " +
            "v3.0, v4.0, v4.1, and v4.2.");
    }

    private sealed class LegacyNameIndex
    {
        private readonly IReadOnlyDictionary<
            string,
            IReadOnlyDictionary<string, string>> byLanguage;
        private readonly IReadOnlyDictionary<string, string?> unambiguous;

        private LegacyNameIndex(
            IReadOnlyDictionary<
                string,
                IReadOnlyDictionary<string, string>> byLanguage,
            IReadOnlyDictionary<string, string?> unambiguous)
        {
            this.byLanguage = byLanguage;
            this.unambiguous = unambiguous;
        }

        public static LegacyNameIndex Create(
            IReadOnlyDictionary<
                string,
                IReadOnlyDictionary<string, string>> localizations)
        {
            var mutableByLanguage = new Dictionary<
                string,
                Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);
            var mutableUnambiguous = new Dictionary<string, string?>(
                StringComparer.Ordinal);

            foreach ((string itemId,
                IReadOnlyDictionary<string, string> names) in localizations)
            {
                foreach ((string language, string name) in names)
                {
                    if (!mutableByLanguage.TryGetValue(
                        language,
                        out Dictionary<string, string>? languageNames))
                    {
                        languageNames = new Dictionary<string, string>(
                            StringComparer.Ordinal);
                        mutableByLanguage.Add(language, languageNames);
                    }

                    languageNames.TryAdd(name, itemId);
                    if (!mutableUnambiguous.TryAdd(name, itemId) &&
                        !string.Equals(
                            mutableUnambiguous[name],
                            itemId,
                            StringComparison.Ordinal))
                    {
                        mutableUnambiguous[name] = null;
                    }
                }
            }

            return new LegacyNameIndex(
                mutableByLanguage.ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, string>)pair.Value,
                    StringComparer.OrdinalIgnoreCase),
                mutableUnambiguous);
        }

        public string? Resolve(string? itemName, string? uigfLanguage)
        {
            if (itemName is null)
            {
                return null;
            }

            if (uigfLanguage is not null &&
                UigfToEmbeddedLanguage.TryGetValue(
                    uigfLanguage,
                    out string? embeddedLanguage) &&
                byLanguage.TryGetValue(
                    embeddedLanguage,
                    out IReadOnlyDictionary<string, string>? names) &&
                names.TryGetValue(itemName, out string? preferredItemId))
            {
                return preferredItemId;
            }

            return unambiguous.TryGetValue(itemName, out string? itemId)
                ? itemId
                : null;
        }
    }
}

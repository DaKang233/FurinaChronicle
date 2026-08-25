using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.Infrastructure.Gacha.Exporting;

internal sealed class GachaExportValueResolver(
    IGachaItemMetadataProvider metadataProvider)
{
    private readonly Dictionary<string, GachaItemMetadata?> metadataCache =
        new(StringComparer.Ordinal);

    public async ValueTask<string> GetItemNameAsync(
        WishRecord record,
        string language,
        CancellationToken cancellationToken)
    {
        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        string metadataLanguage = language switch
        {
            GachaExportLanguages.TraditionalChinese => "cht",
            GachaExportLanguages.English => "en",
            _ => "chs"
        };

        if (metadata?.LocalizedNames?.TryGetValue(
            metadataLanguage,
            out string? localizedName) == true &&
            !string.IsNullOrWhiteSpace(localizedName))
        {
            return localizedName;
        }

        string? fallback = record.ItemName ?? metadata?.Name;
        return !string.IsNullOrWhiteSpace(fallback)
            ? fallback
            : throw MissingValue(record, "name");
    }

    public async ValueTask<string> GetItemTypeAsync(
        WishRecord record,
        string language,
        CancellationToken cancellationToken)
    {
        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        string? sourceType = record.ItemType ?? metadata?.ItemType;
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            throw MissingValue(record, "item_type");
        }

        bool isWeapon =
            sourceType.Contains("weapon", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Contains("武器", StringComparison.Ordinal);
        bool isCharacter =
            sourceType.Contains("avatar", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Contains("character", StringComparison.OrdinalIgnoreCase) ||
            sourceType.Contains("角色", StringComparison.Ordinal);

        if (!isWeapon && !isCharacter)
        {
            return sourceType.Trim();
        }

        return language switch
        {
            GachaExportLanguages.English =>
                isWeapon ? "Weapon" : "Character",
            _ => isWeapon ? "武器" : "角色"
        };
    }

    public async ValueTask<int> GetRankTypeAsync(
        WishRecord record,
        CancellationToken cancellationToken)
    {
        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        return record.RankType ?? metadata?.RankType
            ?? throw MissingValue(record, "rank_type");
    }

    public static string GetPoolName(
        WishRecord record,
        string language)
    {
        string type = record.UigfGachaType ??
            record.GachaType ??
            string.Empty;

        return language switch
        {
            GachaExportLanguages.TraditionalChinese => type switch
            {
                "100" => "新手祈願",
                "200" => "常駐祈願",
                "301" => "角色活動祈願",
                "302" => "武器活動祈願",
                "500" => "集錄祈願",
                _ => $"未知卡池（{type}）"
            },
            GachaExportLanguages.English => type switch
            {
                "100" => "Beginners' Wish",
                "200" => "Wanderlust Invocation",
                "301" => "Character Event Wish",
                "302" => "Weapon Event Wish",
                "500" => "Chronicled Wish",
                _ => $"Unknown Wish ({type})"
            },
            _ => type switch
            {
                "100" => "新手祈愿",
                "200" => "常驻祈愿",
                "301" => "角色活动祈愿",
                "302" => "武器活动祈愿",
                "500" => "集录祈愿",
                _ => $"未知卡池（{type}）"
            }
        };
    }

    private async ValueTask<GachaItemMetadata?> GetMetadataAsync(
        WishRecord record,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.ItemId))
        {
            return null;
        }

        if (!metadataCache.TryGetValue(record.ItemId, out GachaItemMetadata? metadata))
        {
            metadata = await metadataProvider.FindByIdAsync(
                GachaGame.GenshinImpact,
                record.ItemId,
                cancellationToken);
            metadataCache.Add(record.ItemId, metadata);
        }

        return metadata;
    }

    private static InvalidDataException MissingValue(
        WishRecord record,
        string field)
    {
        return new InvalidDataException(
            $"记录 {record.ExternalRecordId} 无法补全 {field}。");
    }
}

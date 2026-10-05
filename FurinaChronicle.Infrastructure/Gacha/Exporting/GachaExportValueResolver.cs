// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;

namespace FurinaChronicle.Infrastructure.Gacha.Exporting;

internal sealed class GachaExportValueResolver(
    IGachaItemMetadataProvider metadataProvider)
{
    private readonly Dictionary<string, GachaItemMetadata?> metadataCache =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, GachaItemMetadata?> metadataByName =
        new(StringComparer.Ordinal);

    public async ValueTask<string> GetItemIdAsync(
        GachaRecord record,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(record.ItemId))
        {
            return record.ItemId.Trim();
        }

        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        return !string.IsNullOrWhiteSpace(metadata?.ItemId)
            ? metadata.ItemId
            : throw MissingValue(record, "item_id");
    }

    public async ValueTask<string> GetItemNameAsync(
        GachaRecord record,
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
        GachaRecord record,
        string language,
        CancellationToken cancellationToken)
    {
        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        string? sourceType = metadata?.ItemType ?? record.ItemType;
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
        GachaRecord record,
        CancellationToken cancellationToken)
    {
        GachaItemMetadata? metadata =
            await GetMetadataAsync(record, cancellationToken);
        return record.RankType ?? metadata?.RankType
            ?? throw MissingValue(record, "rank_type");
    }

    public async ValueTask<string> GetReadableItemNameAsync(
        GachaRecord record,
        string language,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetItemNameAsync(record, language, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return !string.IsNullOrWhiteSpace(record.ItemId)
                ? $"Unknown ({record.ItemId})"
                : "Unknown";
        }
    }

    public async ValueTask<string> GetReadableItemTypeAsync(
        GachaRecord record,
        string language,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetItemTypeAsync(record, language, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return "Unknown";
        }
    }

    public async ValueTask<int?> GetReadableRankTypeAsync(
        GachaRecord record,
        CancellationToken cancellationToken)
    {
        try
        {
            return await GetRankTypeAsync(record, cancellationToken);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    public static string GetPoolName(
        GachaRecord record,
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
                "100" => "Beginners' Gacha",
                "200" => "Wanderlust Invocation",
                "301" => "Character Event Gacha",
                "302" => "Weapon Event Gacha",
                "500" => "Chronicled Gacha",
                _ => $"Unknown Gacha ({type})"
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
        GachaRecord record,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(record.ItemId))
        {
            if (string.IsNullOrWhiteSpace(record.ItemName))
            {
                return null;
            }

            string itemName = record.ItemName.Trim();
            if (!metadataByName.TryGetValue(
                itemName,
                out GachaItemMetadata? nameMetadata))
            {
                nameMetadata = await metadataProvider.FindByNameAsync(
                    GachaGame.GenshinImpact,
                    itemName,
                    cancellationToken);
                metadataByName.Add(itemName, nameMetadata);
                if (nameMetadata is not null)
                {
                    metadataCache.TryAdd(
                        nameMetadata.ItemId,
                        nameMetadata);
                }
            }

            return nameMetadata;
        }

        string itemId = record.ItemId.Trim();
        if (!metadataCache.TryGetValue(itemId, out GachaItemMetadata? metadata))
        {
            metadata = await metadataProvider.FindByIdAsync(
                GachaGame.GenshinImpact,
                itemId,
                cancellationToken);
            metadataCache.Add(itemId, metadata);
        }

        return metadata;
    }

    private static InvalidDataException MissingValue(
        GachaRecord record,
        string field)
    {
        return new InvalidDataException(
            $"记录 {record.ExternalRecordId} 无法补全 {field}。");
    }
}

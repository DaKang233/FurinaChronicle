using FurinaChronicle.Core.Wishes;

namespace FurinaChronicle.App.ViewModels;

public sealed record WishRecordDisplayItem(
    string Name,
    string Rank,
    string Time,
    string Pool,
    string ItemType)
{
    public static WishRecordDisplayItem FromDomain(WishRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        string name = string.IsNullOrWhiteSpace(record.ItemName)
            ? $"物品 {record.ItemId ?? "未知"}"
            : record.ItemName;

        string rank = record.RankType is int rankType
            ? $"{rankType} 星"
            : "未知";

        return new WishRecordDisplayItem(
            name,
            rank,
            record.Time.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            GetPoolName(record.UigfGachaType ?? record.GachaType),
            GetItemTypeName(record.ItemType));
    }

    private static string GetPoolName(string? gachaType)
    {
        return gachaType switch
        {
            "100" => "新手祈愿",
            "200" => "常驻祈愿",
            "301" => "角色活动祈愿",
            "302" => "武器活动祈愿",
            "500" => "集录祈愿",
            null or "" => "未知卡池",
            _ => $"未知卡池（{gachaType}）"
        };
    }

    private static string GetItemTypeName(string? itemType)
    {
        if (string.IsNullOrWhiteSpace(itemType))
        {
            return "未知";
        }

        return itemType.Trim().ToLowerInvariant() switch
        {
            "avatar" or "character" or "角色" => "角色",
            "weapon" or "武器" => "武器",
            _ => itemType.Trim()
        };
    }
}

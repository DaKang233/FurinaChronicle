// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Chronicle;
using FurinaChronicle.Core.Inventory;

namespace FurinaChronicle.Presentation.Chronicle;

/// <summary>
/// Optional display inputs resolved by the host before rendering. The context
/// never changes entry identity or time semantics: missing names fall back to a
/// generic label and missing account labels stay unknown.
/// </summary>
public sealed record ChronicleDisplayContext
{
    private static readonly IReadOnlyDictionary<string, string> NoItems =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<Guid, string> NoAccounts =
        new Dictionary<Guid, string>();

    public static ChronicleDisplayContext Empty { get; } = new();

    /// <summary>
    /// Item display names keyed by either the raw item/type ID or an inventory
    /// stable item key such as <c>1:10000002</c>. Names are display data only.
    /// </summary>
    public IReadOnlyDictionary<string, string> ItemNames { get; init; } = NoItems;

    /// <summary>Archive-scoped account labels keyed by game account copy ID.</summary>
    public IReadOnlyDictionary<Guid, string> AccountLabels { get; init; } = NoAccounts;

    /// <summary>
    /// Time zone used to render local time. Original offsets are always kept in
    /// the secondary text, so changing this never rewrites the fact time.
    /// </summary>
    public TimeZoneInfo DisplayTimeZone { get; init; } = TimeZoneInfo.Local;

    public string? ResolveItemName(ChronicleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.SummaryArguments.TryGetValue("item_key", out string? stableKey) &&
            !string.IsNullOrWhiteSpace(stableKey) &&
            ItemNames.TryGetValue(stableKey.Trim(), out string? keyed))
        {
            return keyed;
        }
        if (entry.ItemId is { Length: > 0 } itemId &&
            ItemNames.TryGetValue(itemId, out string? named))
        {
            return named;
        }
        return null;
    }

    public string AccountLabel(Guid gameAccountId) =>
        AccountLabels.TryGetValue(gameAccountId, out string? label) &&
        !string.IsNullOrWhiteSpace(label)
            ? label
            : "未知账号";
}

/// <summary>
/// Builds the archive-scoped account label shown on Chronicle entries. Names
/// and UIDs are display data; the stable game account copy ID remains the
/// identity used for grouping and links.
/// </summary>
public static class ChronicleAccountLabel
{
    public static string Build(GameAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        string region = ServerRegion(account.ServerRegion);
        string prefix = string.IsNullOrWhiteSpace(account.DisplayName)
            ? region
            : $"{account.DisplayName} · {region}";
        return $"{prefix} · UID {account.Uid}";
    }

    public static string ServerRegion(GameServerRegion region) => region switch
    {
        GameServerRegion.ChinaOfficial => "天空岛（官服）",
        GameServerRegion.ChinaBilibili => "世界树（B服）",
        GameServerRegion.America => "美服",
        GameServerRegion.Europe => "欧服",
        GameServerRegion.Asia => "亚服",
        GameServerRegion.TaiwanHongKongMacao => "港澳台服",
        _ => "未知区服",
    };
}

/// <summary>
/// Parses the inventory stable item key persisted in Chronicle summary
/// arguments (<c>kind:typeId</c> or <c>kind:typeId:instanceId</c>) without
/// depending on inventory rows.
/// </summary>
public static class ChronicleItemKey
{
    public static bool TryParse(
        string? stableKey,
        out InventoryItemKind kind,
        out string typeId)
    {
        kind = default;
        typeId = string.Empty;
        if (string.IsNullOrWhiteSpace(stableKey))
        {
            return false;
        }

        string[] parts = stableKey.Trim().Split(':');
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out int kindValue) ||
            !Enum.IsDefined(typeof(InventoryItemKind), kindValue) ||
            string.IsNullOrWhiteSpace(parts[1]))
        {
            return false;
        }

        kind = (InventoryItemKind)kindValue;
        typeId = parts[1];
        return true;
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Wishes;

public enum WishPoolGroup
{
    CharacterEvent,
    WeaponEvent,
    Standard,
    Novice,
    Chronicled,
    Unknown
}

public static class WishPoolGroupResolver
{
    public static WishPoolGroup Resolve(WishRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Resolve(record.UigfGachaType, record.GachaType);
    }

    public static WishPoolGroup Resolve(
        string? uigfGachaType,
        string? gachaType)
    {
        string? type = string.IsNullOrWhiteSpace(uigfGachaType)
            ? gachaType?.Trim()
            : uigfGachaType.Trim();

        return type switch
        {
            "100" => WishPoolGroup.Novice,
            "200" => WishPoolGroup.Standard,
            "301" or "400" => WishPoolGroup.CharacterEvent,
            "302" => WishPoolGroup.WeaponEvent,
            "500" => WishPoolGroup.Chronicled,
            _ => WishPoolGroup.Unknown
        };
    }

    public static string GetDisplayName(WishPoolGroup group)
    {
        return group switch
        {
            WishPoolGroup.CharacterEvent => "角色活动祈愿",
            WishPoolGroup.WeaponEvent => "武器活动祈愿",
            WishPoolGroup.Standard => "常驻祈愿",
            WishPoolGroup.Novice => "新手祈愿",
            WishPoolGroup.Chronicled => "集录祈愿",
            _ => "未知卡池"
        };
    }
}

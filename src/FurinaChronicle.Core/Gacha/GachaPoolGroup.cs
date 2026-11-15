// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Core.Gacha;

public enum GachaPoolGroup
{
    CharacterEvent,
    WeaponEvent,
    Standard,
    Novice,
    Chronicled,
    Unknown
}

public static class GachaPoolGroupResolver
{
    public static GachaPoolGroup Resolve(GachaRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return Resolve(record.UigfGachaType, record.GachaType);
    }

    public static GachaPoolGroup Resolve(
        string? uigfGachaType,
        string? gachaType)
    {
        string? type = string.IsNullOrWhiteSpace(uigfGachaType)
            ? gachaType?.Trim()
            : uigfGachaType.Trim();

        return type switch
        {
            "100" => GachaPoolGroup.Novice,
            "200" => GachaPoolGroup.Standard,
            "301" or "400" => GachaPoolGroup.CharacterEvent,
            "302" => GachaPoolGroup.WeaponEvent,
            "500" => GachaPoolGroup.Chronicled,
            _ => GachaPoolGroup.Unknown
        };
    }

    public static string GetDisplayName(GachaPoolGroup group)
    {
        return group switch
        {
            GachaPoolGroup.CharacterEvent => "角色活动祈愿",
            GachaPoolGroup.WeaponEvent => "武器活动祈愿",
            GachaPoolGroup.Standard => "常驻祈愿",
            GachaPoolGroup.Novice => "新手祈愿",
            GachaPoolGroup.Chronicled => "集录祈愿",
            _ => "未知卡池"
        };
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;

namespace FurinaChronicle.Core.Archives;

public static class GenshinGameRoleIdentity
{
    public const string MainlandGameBiz = "hk4e_cn";
    public const string GlobalGameBiz = "hk4e_global";

    public static GameRoleNaturalIdentity Create(
        string uid,
        GameServerRegion serverRegion)
    {
        if (!GameUidValidation.IsValidUid(uid))
        {
            throw new ArgumentException(
                "The UID is not a valid Genshin Impact UID.",
                nameof(uid));
        }

        (string gameBiz, string server) = serverRegion switch
        {
            GameServerRegion.ChinaOfficial => (MainlandGameBiz, "cn_gf01"),
            GameServerRegion.ChinaBilibili => (MainlandGameBiz, "cn_qd01"),
            GameServerRegion.America => (GlobalGameBiz, "os_usa"),
            GameServerRegion.Europe => (GlobalGameBiz, "os_euro"),
            GameServerRegion.Asia => (GlobalGameBiz, "os_asia"),
            GameServerRegion.TaiwanHongKongMacao =>
                (GlobalGameBiz, "os_cht"),
            _ => throw new ArgumentException(
                "The server region cannot be mapped to a canonical Genshin server.",
                nameof(serverRegion)),
        };

        return new GameRoleNaturalIdentity(gameBiz, server, uid);
    }

    public static bool TryCreate(
        string uid,
        GameServerRegion serverRegion,
        [NotNullWhen(true)]
        out GameRoleNaturalIdentity? naturalIdentity)
    {
        if (!GameUidValidation.IsValidUid(uid) ||
            serverRegion == GameServerRegion.Unknown ||
            !Enum.IsDefined(serverRegion))
        {
            naturalIdentity = null;
            return false;
        }

        naturalIdentity = Create(uid, serverRegion);
        return true;
    }

    public static GameRoleIdentity CreateIdentity(
        string uid,
        GameServerRegion serverRegion)
    {
        GameRoleNaturalIdentity naturalIdentity = Create(uid, serverRegion);
        return new GameRoleIdentity(
            GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
            naturalIdentity);
    }
}

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
        if (!GameUidValidation.IsStructurallyValidUid(uid))
        {
            throw new ArgumentException(
                "The UID is not a valid Genshin Impact UID.",
                nameof(uid));
        }

        GameServerRegion resolvedRegion = ResolveServerRegion(uid, serverRegion);

        (string gameBiz, string server) = resolvedRegion switch
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
        if (!GameUidValidation.IsStructurallyValidUid(uid))
        {
            naturalIdentity = null;
            return false;
        }

        try
        {
            naturalIdentity = Create(uid, serverRegion);
            return true;
        }
        catch (ArgumentException)
        {
            naturalIdentity = null;
            return false;
        }
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

    public static GameServerRegion ResolveServerRegion(
        string uid,
        GameServerRegion manualFallback)
    {
        if (!GameUidValidation.IsStructurallyValidUid(uid))
        {
            throw new ArgumentException(
                "The UID is not a structurally valid Genshin Impact UID.",
                nameof(uid));
        }

        GameServerRegion inferred = GameServerRegionResolver.Resolve(uid);
        if (inferred != GameServerRegion.Unknown)
        {
            if (manualFallback != GameServerRegion.Unknown &&
                manualFallback != inferred)
            {
                throw new ArgumentException(
                    "The selected server region conflicts with the UID-derived region.",
                    nameof(manualFallback));
            }

            return inferred;
        }

        if (!Enum.IsDefined(manualFallback) ||
            manualFallback == GameServerRegion.Unknown)
        {
            throw new ArgumentException(
                "A valid manual server region is required when the UID region cannot be inferred.",
                nameof(manualFallback));
        }

        return manualFallback;
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.Presentation.Management;

internal static class GachaPassportRoleChoices
{
    internal static IReadOnlyList<GachaSTokenRoleChoice> From(IReadOnlyList<IsolatedCredentialSummary> stored) =>
        stored.Where(item => item.IsUsable && item.CanRefreshGacha)
            .SelectMany(item => (item.RoleUids ?? []).Append(item.BoundRoleUid)
                .Distinct(StringComparer.Ordinal)
                .Where(uid => GenshinGameRoleIdentity.TryCreate(uid, GameServerRegion.Unknown, out _) &&
                    GameServerRegionResolver.Resolve(uid) is GameServerRegion.ChinaOfficial or GameServerRegion.ChinaBilibili)
                .Select(uid => new GachaSTokenRoleChoice(item.Id, uid, $"{item.Label ?? "通行证"} · UID {uid}")))
            .ToArray();
}

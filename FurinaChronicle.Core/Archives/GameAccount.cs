// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Core.Archives
{
    public sealed record GameAccount(
        Guid Id, 
        Guid PlayerArchiveId, string Uid, 
        GameServerRegion ServerRegion, 
        string? DisplayName, 
        bool IsPlaceholder, 
        DateTimeOffset CreatedAt, 
        DateTimeOffset UpdatedAt,
        GameRoleIdentity? RoleIdentity = null)
    {
        // Preserve the supplied explanation; live identity diagnostics remain separate.
        public string? UnresolvedReason { get; init; } = RoleIdentity is not null
            ? null
            : IsPlaceholder ? "placeholder_account" : "legacy_unresolved_identity";

        public GameRoleIdentityId? GameRoleIdentityId => RoleIdentity?.Id;

        public GameRoleIdentityResolutionState IdentityResolutionState =>
            RoleIdentity is null
                ? GameRoleIdentityResolutionState.Unresolved
                : GameRoleIdentityResolutionState.Resolved;
    }
}

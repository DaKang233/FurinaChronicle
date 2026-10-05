// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class UpdateGameAccount(IGameAccountRepository repository)
{
    public async Task<GameAccount> ExecuteAsync(Guid gameAccountId, string uid, GameServerRegion serverRegion, string? displayName, CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
        }

        GameAccount? current = await repository.GetByIdAsync(gameAccountId, cancellationToken);

        if (current is null)
        {
            throw new KeyNotFoundException("要更新的游戏账号不存在。");
        }

        if (string.IsNullOrWhiteSpace(uid))
        {
            throw new ArgumentException("UID 不能为空。", nameof(uid));
        }

        string normalizedUid = uid.Trim();

        if (!normalizedUid.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("UID 只能包含数字。", nameof(uid));
        }

        if (!GameUidValidation.IsStructurallyValidUid(normalizedUid))
        {
            throw new ArgumentException("无效的 UID。有效的 UID 是长度为 9~10 个的数字。", nameof(uid));
        }

        GameRoleNaturalIdentity naturalIdentity =
            GenshinGameRoleIdentity.Create(normalizedUid, serverRegion);
        GameServerRegion resolvedRegion =
            GenshinGameRoleIdentity.ResolveServerRegion(
                normalizedUid,
                serverRegion);

        string? normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        if (normalizedDisplayName?.Length - uid.Length - 3 > 50)
        {
            throw new ArgumentException("账号备注不能超过 50 个字符。", nameof(displayName));
        }

        GameAccount? conflictingAccount =
            await repository.GetByArchiveIdAndNaturalIdentityAsync(
                current.PlayerArchiveId,
                naturalIdentity,
                cancellationToken);

        if (conflictingAccount is not null && conflictingAccount.Id != current.Id)
        {
            throw new InvalidOperationException("该档案下已经存在相同 UID 的账号。");
        }

        GameRoleIdentity roleIdentity;
        if (current.RoleIdentity is not null)
        {
            if (current.RoleIdentity.NaturalIdentity != naturalIdentity)
            {
                throw new InvalidOperationException(
                    "Changing the natural identity of a resolved account is not supported. " +
                    "Use a future identity-correction workflow instead.");
            }

            roleIdentity = current.RoleIdentity;
        }
        else
        {
            roleIdentity =
                await repository.GetRoleIdentityByNaturalIdentityAsync(
                    naturalIdentity,
                    cancellationToken) ??
                new GameRoleIdentity(
                    GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
                    naturalIdentity);
        }

        GameAccount updated = current with
        {
            Uid = normalizedUid,
            ServerRegion = resolvedRegion,
            DisplayName = normalizedDisplayName,
            IsPlaceholder = false,
            UpdatedAt = DateTimeOffset.UtcNow,
            RoleIdentity = roleIdentity,
        };

        await repository.UpdateAsync(updated, cancellationToken);

        return updated;
    }
}

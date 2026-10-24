// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Archives
{
    public sealed class AddGameAccount(IPlayerArchiveRepository archiveRepository, IGameAccountRepository accountRepository)
    {
        public async Task<GameAccount> ExecuteAsync(Guid playerArchiveId, string uid, GameServerRegion serverRegion, string? displayName, CancellationToken cancellationToken = default)
        {
            if (playerArchiveId == Guid.Empty)
            {
                throw new ArgumentException("玩家档案 ID 不能为空",nameof(playerArchiveId));
            }
            PlayerArchive archive = await archiveRepository.GetByIdAsync(playerArchiveId, cancellationToken) ?? throw new KeyNotFoundException("指定的玩家档案不存在。");
            GameAccount account = await PrepareAsync(
                archive,
                uid,
                serverRegion,
                displayName,
                archiveIsProposed: false,
                cancellationToken);
            await accountRepository.AddAsync(account, cancellationToken);
            return account;
        }

        public async Task<GameAccount> PrepareAsync(
            PlayerArchive archive,
            string uid,
            GameServerRegion serverRegion,
            string? displayName,
            bool archiveIsProposed,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(archive);
            PlayerArchive? persistedArchive = await archiveRepository.GetByIdAsync(
                archive.Id,
                cancellationToken);
            if (archiveIsProposed ? persistedArchive is not null : persistedArchive is null)
            {
                throw new InvalidOperationException(
                    archiveIsProposed
                        ? "待创建的玩家档案已经存在。"
                        : "指定的玩家档案不存在。");
            }
            string normalizedUid = uid.Trim();
            if (string.IsNullOrEmpty(normalizedUid)) throw new ArgumentException("UID 不能为空", nameof(uid));
            if (!normalizedUid.All(char.IsAsciiDigit)) throw new ArgumentException("UID 只能包含数字", nameof(uid));
            if (!GameUidValidation.IsStructurallyValidUid(normalizedUid)) throw new ArgumentException("无效的 UID。有效的 UID 是长度为 9~10 个的数字。", nameof(uid));
            GameRoleNaturalIdentity naturalIdentity =
                GenshinGameRoleIdentity.Create(normalizedUid, serverRegion);
            GameAccount? existing =
                await accountRepository.GetByArchiveIdAndNaturalIdentityAsync(
                    archive.Id,
                    naturalIdentity,
                    cancellationToken);
            if (existing is not null) throw new InvalidOperationException("该档案下已存在相同游戏角色的账号");
            string? normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            if (!string.IsNullOrEmpty(normalizedDisplayName) && normalizedDisplayName.Length - uid.Length - 3 > 50) throw new ArgumentException("显示名称不能超过 50 个字符", nameof(displayName));
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GameRoleIdentity roleIdentity =
                await accountRepository.GetRoleIdentityByNaturalIdentityAsync(
                    naturalIdentity,
                    cancellationToken) ??
                new GameRoleIdentity(
                    GameRoleIdentityId.FromNaturalIdentity(naturalIdentity),
                    naturalIdentity);
            GameServerRegion resolvedRegion =
                GenshinGameRoleIdentity.ResolveServerRegion(
                    normalizedUid,
                    serverRegion);

            var account = new GameAccount(
                Guid.NewGuid(),
                archive.Id,
                normalizedUid,
                resolvedRegion,
                normalizedDisplayName,
                IsPlaceholder: false,
                now,
                now,
                roleIdentity);
            return account;
        }
    }
}

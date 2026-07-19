using FurinaArchive.Core.Archives;
using FurinaArchive.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Archives
{
    public sealed class AddGameAccount(IPlayerArchiveRepository archiveRepository, IGameAccountRepository accountRepository)
    {
        public async Task<GameAccount> ExecuteAsync(Guid playerArchiveId, string uid, GameServerRegion serverRegion, string? displayName, CancellationToken cancellationToken = default)
        {
            if (playerArchiveId == Guid.Empty)
            {
                throw new ArgumentException("玩家档案 ID 不能为空",nameof(playerArchiveId));
            }
            PlayerArchive? archive = await archiveRepository.GetByIdAsync(playerArchiveId, cancellationToken);
            if (archive is null) throw new ArgumentException("指定的玩家档案不存在。");
            string normalizedUid = uid.Trim();
            if (string.IsNullOrEmpty(normalizedUid)) throw new ArgumentException("UID 不能为空", nameof(uid));
            if (!normalizedUid.All(char.IsAsciiDigit)) throw new ArgumentException("UID 只能包含数字", nameof(uid));
            if (serverRegion == GameServerRegion.Unknown) throw new ArgumentException("必须选择服务器区域",nameof(serverRegion));
            GameAccount? existing = await accountRepository.FindByUidAsync(serverRegion, uid, cancellationToken);
            if (existing is not null) throw new InvalidOperationException("该服务器区域下已存在相同 UID 的账号");
            string? normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var account = new GameAccount(Guid.NewGuid(), playerArchiveId, uid, serverRegion, displayName, false, now, now);
            await accountRepository.AddAsync(account, cancellationToken);
            return account;
        }
    }
}

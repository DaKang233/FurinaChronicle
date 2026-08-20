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
            PlayerArchive? archive = await archiveRepository.GetByIdAsync(playerArchiveId, cancellationToken) ?? throw new KeyNotFoundException("指定的玩家档案不存在。");
            string normalizedUid = uid.Trim();
            if (string.IsNullOrEmpty(normalizedUid)) throw new ArgumentException("UID 不能为空", nameof(uid));
            if (!normalizedUid.All(char.IsAsciiDigit)) throw new ArgumentException("UID 只能包含数字", nameof(uid));
            if (serverRegion == GameServerRegion.Unknown) throw new ArgumentException("必须选择服务器区域",nameof(serverRegion));
            if (!GameUidValidation.IsValidUid(normalizedUid)) throw new ArgumentException("无效的 UID。有效的 UID 是长度为 9~10 个的数字。", nameof(uid));
            GameAccount? existing = await accountRepository.GetByArchiveIdAndUidAsync(playerArchiveId, normalizedUid, cancellationToken);
            if (existing is not null) throw new InvalidOperationException("该档案下已存在相同 UID 的账号");
            string? normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
            if (!string.IsNullOrEmpty(normalizedDisplayName) && normalizedDisplayName.Length - uid.Length - 3 > 50) throw new ArgumentException("显示名称不能超过 50 个字符", nameof(displayName));
            DateTimeOffset now = DateTimeOffset.UtcNow;

            var account = new GameAccount(Guid.NewGuid(), playerArchiveId, normalizedUid, serverRegion, normalizedDisplayName, IsPlaceholder: false, now, now);
            await accountRepository.AddAsync(account, cancellationToken);
            return account;
        }
    }
}

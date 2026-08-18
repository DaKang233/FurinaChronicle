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

        if (!Enum.IsDefined(serverRegion) || serverRegion == GameServerRegion.Unknown)
        {
            throw new ArgumentException("必须选择有效的服务器区域。", nameof(serverRegion));
        }

        string? normalizedDisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();

        if (normalizedDisplayName?.Length > 50)
        {
            throw new ArgumentException("账号备注不能超过 50 个字符。", nameof(displayName));
        }

        GameAccount? conflictingAccount = await repository.FindByUidAsync(serverRegion, normalizedUid, cancellationToken);

        if (conflictingAccount is not null && conflictingAccount.Id != current.Id)
        {
            throw new InvalidOperationException("该服务器区域下已经存在相同 UID 的账号。");
        }

        GameAccount updated = current with
        {
            Uid = normalizedUid,
            ServerRegion = serverRegion,
            DisplayName = normalizedDisplayName,
            IsPlaceholder = false,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await repository.UpdateAsync(updated, cancellationToken);

        return updated;
    }
}

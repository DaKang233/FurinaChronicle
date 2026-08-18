using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class GetGameAccounts(IPlayerArchiveRepository archiveRepository, IGameAccountRepository accountRepository)
{
    public async Task<IReadOnlyList<GameAccount>> ExecuteAsync(Guid playerArchiveId, CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException("玩家档案 ID 不能为空。", nameof(playerArchiveId));
        }

        PlayerArchive? archive = await archiveRepository.GetByIdAsync(playerArchiveId, cancellationToken) ?? throw new KeyNotFoundException("指定的玩家档案不存在。");
        return await accountRepository.GetByArchiveIdAsync(playerArchiveId, cancellationToken);
    }
}

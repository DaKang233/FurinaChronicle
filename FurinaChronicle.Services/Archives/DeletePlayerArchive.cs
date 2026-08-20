using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class DeletePlayerArchive(IPlayerArchiveRepository repository)
{
    public async Task ExecuteAsync(Guid playerArchiveId, CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException("玩家档案 ID 不能为空。", nameof(playerArchiveId));
        }

        PlayerArchive? archive = await repository.GetByIdAsync(playerArchiveId, cancellationToken) ?? throw new KeyNotFoundException("要删除的玩家档案不存在。");
        await repository.DeleteAsync(playerArchiveId, cancellationToken);
    }
}
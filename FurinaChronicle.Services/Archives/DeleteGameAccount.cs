using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;

namespace FurinaChronicle.Services.Archives;

public sealed class DeleteGameAccount(IGameAccountRepository repository)
{
    public async Task ExecuteAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
        }

        GameAccount? account = await repository.GetByIdAsync(gameAccountId, cancellationToken) ?? throw new KeyNotFoundException("要删除的游戏账号不存在。");

        await repository.DeleteAsync(gameAccountId, cancellationToken);
    }
}

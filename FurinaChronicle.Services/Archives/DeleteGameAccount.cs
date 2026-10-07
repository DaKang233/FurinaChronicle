// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Services.Archives;

public sealed class DeleteGameAccount(IGachaAtomicChangeStore atomicChangeStore)
{
    public async Task ExecuteAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
    {
        if (gameAccountId == Guid.Empty)
        {
            throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
        }

        DateTimeOffset now = DateTimeOffset.Now;
        await atomicChangeStore.PurgeAccountAsync(
            new GachaScopePurgeRequest(
                OperationId.New(),
                gameAccountId,
                now,
                now,
                "永久删除游戏账号及其本地数据"),
            cancellationToken);
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Text;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Services.Archives;

public sealed class DeletePlayerArchive(
    IArchiveOperationHistoryStore operationHistoryStore)
{
    public async Task ExecuteAsync(Guid playerArchiveId, CancellationToken cancellationToken = default)
    {
        if (playerArchiveId == Guid.Empty)
        {
            throw new ArgumentException("玩家档案 ID 不能为空。", nameof(playerArchiveId));
        }

        DateTimeOffset now = DateTimeOffset.Now;
        await operationHistoryStore.PurgeArchiveAsync(
            new ArchiveScopePurgeRequest(
                OperationId.New(),
                playerArchiveId,
                now,
                now,
                "永久删除档案及其本地数据"),
            cancellationToken);
    }
}

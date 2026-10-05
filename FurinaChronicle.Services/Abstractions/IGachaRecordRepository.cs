// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Gacha;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IGachaRecordRepository
    {
        Task<IReadOnlyList<GachaRecord>> GetRecentAsync(Guid gameAccountId,int count, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GachaRecord>> GetPageAsync(
            Guid gameAccountId,
            int offset,
            int count,
            CancellationToken cancellationToken = default);

        Task<int> CountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GachaRecord>> QueryAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default);

        Task<int> CountAsync(
            GachaRecordQuery query,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<GachaRecord>> GetByExternalRecordIdsAsync(
            Guid gameAccountId,
            IReadOnlyCollection<string> externalRecordIds,
            CancellationToken cancellationToken = default);

        Task<GachaSaveResult> SaveBatchAsync(
            IReadOnlyCollection<GachaRecord> records,
            CancellationToken cancellationToken = default,
            GachaRecordConflictPolicy conflictPolicy =
                GachaRecordConflictPolicy.PreserveExisting);
    }
}

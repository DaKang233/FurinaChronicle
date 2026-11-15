// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Gacha
{
    public sealed class GetRecentGachaRecords(
        IGachaRecordRepository repository)
    {
        public Task<IReadOnlyList<GachaRecord>> ExecuteAsync(
            Guid gameAccountId,
            int count = 20,
            CancellationToken cancellationToken = default)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(count),
                    "查询数量必须大于零。");
            }

            return repository.GetRecentAsync(gameAccountId, count, cancellationToken);
        }
    }
}

using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes
{
    public sealed class GetRecentWishRecords(
        IWishRecordRepository repository)
    {
        public Task<IReadOnlyList<WishRecord>> ExecuteAsync(
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

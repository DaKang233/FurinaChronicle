using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IWishRecordRepository
    {
        Task<IReadOnlyList<WishRecord>> GetRecentAsync(Guid gameAccountId,int count, CancellationToken cancellationToken = default);

        Task<WishSaveResult> SaveBatchAsync(IReadOnlyCollection<WishRecord> records, CancellationToken cancellationToken = default);
    }
}

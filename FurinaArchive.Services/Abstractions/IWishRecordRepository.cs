using FurinaArchive.Core.Wishes;
using FurinaArchive.Services.Wishes;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Abstractions
{
    public interface IWishRecordRepository
    {
        Task<IReadOnlyList<WishRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

        Task<WishSaveResult> SaveBatchAsync(IReadOnlyCollection<WishRecord> records, CancellationToken cancellationToken = default);
    }
}

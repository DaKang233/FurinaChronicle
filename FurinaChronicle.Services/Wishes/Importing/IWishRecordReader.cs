using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Wishes.Importing
{
    public interface IWishRecordReader
    {
        Task<WishReadResult> ReadAsync(Stream source, Guid gameAccountId, CancellationToken cancellationToken = default);
    }
}

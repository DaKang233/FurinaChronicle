using FurinaChronicle.Core.Archives;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IPlayerArchiveRepository
    {
        Task<IReadOnlyList<PlayerArchive>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<PlayerArchive?> GetByIdAsync(Guid archiveId, CancellationToken cancellationToken = default);
        Task AddAsync(PlayerArchive archive, CancellationToken cancellationToken = default);
        Task UpdateAsync(PlayerArchive archive, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid archiveId, CancellationToken cancellationToken= default);
    }
}

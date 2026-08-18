using FurinaChronicle.Core.Archives;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IGameAccountRepository
    {
        Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(Guid archiveId, CancellationToken cancellationToken = default);
        Task<GameAccount?> GetByIdAsync(Guid gameAcountId, CancellationToken cancellationToken = default);
        Task<GameAccount?> FindByUidAsync(GameServerRegion serverRegion, string uid, CancellationToken cancellationToken = default);
        Task AddAsync(GameAccount account,  CancellationToken cancellationToken = default);
        Task UpdateAsync(GameAccount account, CancellationToken cancellationToken = default);
        Task DeleteAsync(Guid gameAccountId,  CancellationToken cancellationToken = default);
    }
}

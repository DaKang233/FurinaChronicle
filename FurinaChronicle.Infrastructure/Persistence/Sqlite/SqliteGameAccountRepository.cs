using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqliteGameAccountRepository(FurinaDatabase database) : IGameAccountRepository
    {
        public async Task<GameAccount?> GetByIdAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
        {
            if ( gameAccountId == Guid.Empty)
            {
                throw new ArgumentException("游戏账号 ID 不能为空。", nameof(gameAccountId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        public async Task<GameAccount?> FindByUidAsync(GameServerRegion serverRegion, string uid, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task UpdateAsync(GameAccount gameAccount, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync(Guid gameAccountId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<GameAccount>> GetAllByArchiveIdAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task AddAsync(GameAccount gameAccount, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
        
        public async Task<IReadOnlyList<GameAccount>> GetByArchiveIdAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}

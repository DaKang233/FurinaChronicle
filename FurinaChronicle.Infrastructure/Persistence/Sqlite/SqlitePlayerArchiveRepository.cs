using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite
{
    public sealed class SqlitePlayerArchiveRepository(FurinaDatabase database) : IPlayerArchiveRepository
    {
        public async Task<PlayerArchive?> GetByIdAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            if (archiveId == Guid.Empty)
            {
                throw new ArgumentException("存档 ID 不能为空。", nameof(archiveId));
            }
            await database.InitializeAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            throw new NotImplementedException();
        }

        public async Task UpdateAsync(PlayerArchive playerArchive, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task AddAsync(PlayerArchive playerArchive, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task DeleteAsync(Guid archiveId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<IReadOnlyList<PlayerArchive>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}

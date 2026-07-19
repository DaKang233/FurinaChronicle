using FurinaArchive.Core.Archives;
using FurinaArchive.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaArchive.Services.Archives
{
    public sealed class GetPlayerArchives(IPlayerArchiveRepository repository)
    {
        public Task<IReadOnlyList<PlayerArchive>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return repository.GetAllAsync(cancellationToken);
        }
    }
}

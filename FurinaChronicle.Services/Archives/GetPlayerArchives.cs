using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Abstractions;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Archives
{
    public sealed class GetPlayerArchives(IPlayerArchiveRepository repository)
    {
        public Task<IReadOnlyList<PlayerArchive>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return repository.GetAllAsync(cancellationToken);
        }
    }
}

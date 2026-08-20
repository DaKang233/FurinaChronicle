using FurinaChronicle.Services.Archives;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IArchiveSelectionStore
    {
        Task<ArchiveSelection?> LoadAsync(CancellationToken cancellationToken = default);
        Task<ArchiveSelection?> LoadForArchiveAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default);

        Task SaveAsync(ArchiveSelection selection, CancellationToken cancellationToken = default);

        Task ClearAsync(CancellationToken cancellationToken = default);
    }
}

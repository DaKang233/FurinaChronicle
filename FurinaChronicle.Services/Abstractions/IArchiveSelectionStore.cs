// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives;
using System;
using System.Collections.Generic;
using System.Text;

namespace FurinaChronicle.Services.Abstractions
{
    public interface IArchiveSelectionStore
    {
        Task<Guid?> LoadCurrentArchiveIdAsync(
            CancellationToken cancellationToken = default);

        Task<ArchiveSelection?> LoadAsync(CancellationToken cancellationToken = default);
        Task<ArchiveSelection?> LoadForArchiveAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default);

        Task SaveCurrentArchiveIdAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default);

        Task SaveAsync(ArchiveSelection selection, CancellationToken cancellationToken = default);

        Task ClearAsync(CancellationToken cancellationToken = default);
    }
}

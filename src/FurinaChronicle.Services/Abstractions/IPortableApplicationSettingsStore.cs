// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Abstractions;

public interface IPortableApplicationSettingsStore
{
    Task<bool> GetRequireUniqueArchiveNamesAsync(
        CancellationToken cancellationToken = default);

    Task SetRequireUniqueArchiveNamesAsync(
        bool value,
        CancellationToken cancellationToken = default);
}

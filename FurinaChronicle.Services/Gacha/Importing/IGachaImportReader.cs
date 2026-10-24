// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Services.Gacha.Importing;

public interface IGachaImportReader
{
    Task<GachaReadResult> ReadAsync(
        Stream source,
        CancellationToken cancellationToken = default);
}

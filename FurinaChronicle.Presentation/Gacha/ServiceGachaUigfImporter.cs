// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Presentation.Gacha;

// Thin adapter so the Presentation planner depends on a testable seam while the production
// ImportUigfGachaRecords use case remains the only write path.
public sealed class ServiceGachaUigfImporter(ImportUigfGachaRecords importer) : IGachaUigfImporter
{
    public Task<GachaImportResult> ExecuteAsync(
        Stream source,
        Guid playerArchiveId,
        Guid? targetGameAccountId,
        GachaImportMode mode,
        CancellationToken cancellationToken = default) =>
        importer.ExecuteAsync(source, playerArchiveId, targetGameAccountId, mode, cancellationToken);
}

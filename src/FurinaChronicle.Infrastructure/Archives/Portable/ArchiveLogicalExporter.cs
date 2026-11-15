// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Archives.Portable;

namespace FurinaChronicle.Infrastructure.Archives.Portable;

public sealed class ArchiveLogicalExporter(
    IArchiveExportSnapshotFactory snapshots,
    IArchivePackageWriter writer,
    IArchivePackageReader reader)
    : IArchiveLogicalExporter
{
    public async Task<ArchiveWriteResult> ExportAsync(
        Stream destination,
        ArchiveExportScope scope,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanSeek)
        {
            throw new ArchivePackageException(
                ArchivePackageErrorCode.InputNotSeekable,
                "A validated Archive export requires a seekable destination.");
        }

        await using IArchiveExportSnapshot snapshot = await snapshots.OpenAsync(
            scope,
            cancellationToken);
        destination.SetLength(0);
        ArchiveWriteResult result = await writer.WriteAsync(
            destination,
            snapshot,
            generator,
            cancellationToken);
        destination.Position = 0;
        ArchivePackageSummary verified = await reader.ReadAndValidateAsync(
            destination,
            cancellationToken);
        destination.Position = destination.Length;
        return result with { Summary = verified };
    }
}

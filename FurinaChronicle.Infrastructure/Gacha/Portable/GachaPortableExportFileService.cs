// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.Portable;

namespace FurinaChronicle.Infrastructure.Gacha.Portable;

public sealed class GachaPortableExportFileService(
    IGachaPortableExportSnapshotFactory snapshotFactory,
    IGachaPortablePackageWriter writer)
    : IGachaPortableExportFileService
{
    public async Task<GachaPortableWriteResult> ExportAsync(
        string destinationPath,
        Guid playerArchiveId,
        IReadOnlyCollection<Guid> gameAccountIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException(
                "The destination path cannot be empty.",
                nameof(destinationPath));
        }

        string finalPath = Path.GetFullPath(destinationPath);
        string? directory = Path.GetDirectoryName(finalPath);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException(
                "The export destination directory does not exist.");
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(finalPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using IGachaPortableExportSnapshot snapshot =
                await snapshotFactory.OpenAsync(
                    playerArchiveId,
                    gameAccountIds,
                    cancellationToken);
            GachaPortableWriteResult result;
            await using (var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                result = await writer.WriteAsync(
                    output,
                    snapshot,
                    cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            await GachaPortableContainerVerifier.VerifyAsync(
                temporaryPath,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath, overwrite: true);
            return result;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}

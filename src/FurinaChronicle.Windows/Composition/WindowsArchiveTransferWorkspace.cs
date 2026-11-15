// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;

namespace FurinaChronicle.Windows.Composition;

// Temporary export workspace for the isolated host. Export is written to a staging file, verified
// by ArchiveLogicalExporter, then published to the user-selected destination; a failed/cancelled
// export leaves no target file behind.
public sealed class WindowsArchiveTransferWorkspace(string temporaryDirectory)
    : IArchiveTransferWorkspace
{
    public string TemporaryDirectory { get; } = temporaryDirectory;

    public Task PublishAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        catch (IOException)
        {
            // Different volume: copy first, then remove the staging file.
            File.Copy(temporaryPath, destinationPath, overwrite: true);
            File.Delete(temporaryPath);
        }

        return Task.CompletedTask;
    }

    public void DeleteTemporary(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

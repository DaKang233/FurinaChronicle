// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

public sealed class DatabaseStorageCapacityProvider(
    SqliteDatabaseOptions options)
    : IHistoryStorageCapacityProvider
{
    public HistoryStorageCapacity GetCapacity()
    {
        try
        {
            string fullPath = Path.GetFullPath(options.DatabasePath);
            string? root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return new HistoryStorageCapacity(null);
            }

            var drive = new DriveInfo(root);
            return drive.IsReady
                ? new HistoryStorageCapacity(drive.AvailableFreeSpace)
                : new HistoryStorageCapacity(null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException)
        {
            return new HistoryStorageCapacity(null);
        }
    }
}

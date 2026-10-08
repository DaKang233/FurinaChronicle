// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.App.Persistence;

internal sealed class PlatformDatabaseStorageCapacityProvider(
    SqliteDatabaseOptions options)
    : IHistoryStorageCapacityProvider
{
    private readonly DatabaseStorageCapacityProvider fallback = new(options);

    public HistoryStorageCapacity GetCapacity()
    {
#if ANDROID
        try
        {
            string fullPath = Path.GetFullPath(options.DatabasePath);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return new HistoryStorageCapacity(null);
            }

            using var fileSystem = new Android.OS.StatFs(directory);
            long availableBytes = fileSystem.AvailableBytes;
            return new HistoryStorageCapacity(
                availableBytes > 0 ? availableBytes : null);
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            ArgumentException or
            Java.Lang.Exception)
        {
            return new HistoryStorageCapacity(null);
        }
#else
        return fallback.GetCapacity();
#endif
    }
}

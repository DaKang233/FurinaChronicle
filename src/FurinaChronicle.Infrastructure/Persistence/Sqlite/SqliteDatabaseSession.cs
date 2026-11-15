// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.Infrastructure.Persistence.Sqlite;

// A Windows host retains the initialization sidecar for its entire database session.
// Older hosts already need this same file before they can initialize their repositories.
public sealed class SqliteDatabaseSession(SqliteDatabaseOptions options) : IDisposable
{
    private FileStream? lease;

    public string DatabasePath => options.DatabasePath;
    public bool IsHeld => lease is not null;

    public bool TryAcquire()
    {
        if (IsHeld) return true;

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        try
        {
            lease = OpenInitializationLock(DatabasePath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    internal static FileStream OpenInitializationLock(string databasePath) =>
        new(databasePath + ".initialize.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    public void Dispose()
    {
        lease?.Dispose();
        lease = null;
    }
}

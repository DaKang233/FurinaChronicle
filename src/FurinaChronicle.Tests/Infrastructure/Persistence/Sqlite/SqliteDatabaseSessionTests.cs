// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqliteDatabaseSessionTests
{
    [Fact]
    public async Task LifetimeLeaseBlocksBothNewAndLegacyStartupUntilReleased()
    {
        if (!OperatingSystem.IsWindows()) return;

        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var options = new SqliteDatabaseOptions(Path.Combine(directory, "furinachronicle.db3"));
        try
        {
            using var first = new SqliteDatabaseSession(options);
            using var second = new SqliteDatabaseSession(options);
            await using (var database = new FurinaDatabase(options, first))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => database.InitializeAsync());
                Assert.False(File.Exists(options.DatabasePath));
                Assert.True(first.TryAcquire());
                await database.InitializeAsync();

                Assert.False(second.TryAcquire());
                Assert.Throws<IOException>(() =>
                {
                    using var oldHost = SqliteDatabaseSession.OpenInitializationLock(options.DatabasePath);
                });
            }

            first.Dispose();
            Assert.True(second.TryAcquire());
            Assert.False(first.TryAcquire());
            second.Dispose();

            await using var legacyDatabase = new FurinaDatabase(options);
            await legacyDatabase.InitializeAsync();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

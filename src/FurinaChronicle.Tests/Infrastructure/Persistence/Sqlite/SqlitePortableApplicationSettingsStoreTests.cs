// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;

namespace FurinaChronicle.Tests.Infrastructure.Persistence.Sqlite;

public sealed class SqlitePortableApplicationSettingsStoreTests
{
    [Fact]
    public async Task SetAsync_DisablesAndReenablesUniqueArchiveNames()
    {
        await using var fixture = SqliteTestFixture.Create();
        var store = new SqlitePortableApplicationSettingsStore(fixture.Database);

        Assert.True(await store.GetRequireUniqueArchiveNamesAsync());
        await store.SetRequireUniqueArchiveNamesAsync(false);
        Assert.False(await store.GetRequireUniqueArchiveNamesAsync());
        await store.SetRequireUniqueArchiveNamesAsync(true);
        Assert.True(await store.GetRequireUniqueArchiveNamesAsync());
    }

    [Fact]
    public async Task SetAsync_EnableWithDuplicateNames_RejectsWithoutChangingValue()
    {
        await using var fixture = SqliteTestFixture.Create();
        await fixture.Database.InitializeAsync();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await fixture.Database.Connection.InsertAllAsync(
        new PlayerArchiveRow[]
        {
            PlayerArchiveRow.FromDomain(
                new PlayerArchive(Guid.NewGuid(), "Duplicate", now, now)),
            PlayerArchiveRow.FromDomain(
                new PlayerArchive(Guid.NewGuid(), "Duplicate", now, now)),
        });
        var store = new SqlitePortableApplicationSettingsStore(fixture.Database);
        await store.SetRequireUniqueArchiveNamesAsync(false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.SetRequireUniqueArchiveNamesAsync(true));

        Assert.False(await store.GetRequireUniqueArchiveNamesAsync());
    }

    private sealed class SqliteTestFixture : IAsyncDisposable
    {
        private readonly string directory;

        private SqliteTestFixture(string directory, FurinaDatabase database)
        {
            this.directory = directory;
            Database = database;
        }

        public FurinaDatabase Database { get; }

        public static SqliteTestFixture Create()
        {
            string directory = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleSettingsTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            return new SqliteTestFixture(
                directory,
                new FurinaDatabase(new SqliteDatabaseOptions(
                    Path.Combine(directory, "settings.db3"))));
        }

        public async ValueTask DisposeAsync()
        {
            await Database.DisposeAsync();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

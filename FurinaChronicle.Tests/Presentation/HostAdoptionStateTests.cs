// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;
using SQLite;

namespace FurinaChronicle.Tests.Presentation;

public sealed class HostAdoptionStateTests
{
    [Fact]
    public async Task ExistingDatabaseIsAdoptedInPlaceWithBackupAndMarker()
    {
        using var fixture = new AdoptionFixture();
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();
        Assert.True(state.Snapshot.Database.Exists);
        Assert.False(state.Snapshot.IsAdopted);
        Assert.True(state.Snapshot.RequiresAction);
        string sourceHash = await fixture.Port.ComputeSha256Async(fixture.Paths.DatabasePath);

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(result.Marker);
        Assert.Equal("Adopted", result.Marker.Status);
        Assert.Equal("Reauthorize", result.Marker.CredentialsPolicy);
        Assert.Equal(
            sourceHash,
            await fixture.Port.ComputeSha256Async(fixture.Paths.DatabasePath));
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
        Assert.True(File.Exists(Path.Combine(
            fixture.Paths.BackupsDirectory,
            result.Marker.BackupFileName!)));
        Assert.True(File.Exists(fixture.Paths.MarkerPath));
        Assert.DoesNotContain(
            Path.GetDirectoryName(fixture.Paths.DatabasePath)!,
            result.Marker.DatabasePathHash);
        Assert.Contains("同一", state.BuildRollbackText());
        Assert.Contains("重新", state.CredentialsText);

        int backups = fixture.BackupCount();
        HostAdoptionState restarted = fixture.CreateState();
        await restarted.EnterAsync();
        Assert.True(restarted.Snapshot.IsAdopted);
        Assert.False(restarted.Snapshot.RequiresAction);
        HostAdoptionResult repeated = await restarted.AdoptAsync();
        Assert.True(repeated.Succeeded);
        Assert.Equal(backups, fixture.BackupCount());
        Assert.Equal(sourceHash, await fixture.Port.ComputeSha256Async(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task MissingDatabaseIsInitializedWithoutSyntheticRows()
    {
        using var fixture = new AdoptionFixture(createDatabase: false);
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();
        Assert.True(state.Snapshot.CanInitialize);
        Assert.False(state.Snapshot.Database.Exists);

        HostAdoptionResult result = await state.InitializeAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal("Initialized", result.Marker!.Status);
        Assert.Null(result.Marker.BackupFileName);
        Assert.True(File.Exists(fixture.Paths.DatabasePath));
        Assert.True((await fixture.Port.ValidateDatabaseAsync(
            fixture.Paths.DatabasePath)).IsValid);
        Assert.Equal(0, await fixture.CountRowsAsync());
        Assert.True(File.Exists(fixture.Paths.MarkerPath));
    }

    [Fact]
    public async Task IncompleteSchemaIsRejectedWithoutMarkerOrTemporaryCopy()
    {
        using var fixture = new AdoptionFixture(incompleteSchema: true);
        string before = HashFile(fixture.Paths.DatabasePath);
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("结构不完整", result.Message);
        Assert.False(File.Exists(fixture.Paths.MarkerPath));
        Assert.True(fixture.BackupCount() > 0);
        Assert.False(File.Exists(Path.Combine(
            Path.GetDirectoryName(fixture.Paths.MarkerPath)!,
            "adoption-validation.db3")));
        Assert.False(File.Exists(Path.Combine(
            Path.GetDirectoryName(fixture.Paths.MarkerPath)!,
            "adoption-validation.db3.initialize.lock")));
        Assert.Equal(before, HashFile(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task InvalidExistingDatabaseIsNotAdoptedAndNotBackedUp()
    {
        using var fixture = new AdoptionFixture();
        File.WriteAllText(fixture.Paths.DatabasePath, "not a sqlite database");
        string before = HashFile(fixture.Paths.DatabasePath);
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.Paths.MarkerPath));
        Assert.Equal(before, HashFile(fixture.Paths.DatabasePath));
        Assert.Equal(0, fixture.BackupCount());
    }

    [Fact]
    public async Task BackupFailureDoesNotWriteMarkerOrChangeDatabase()
    {
        using var fixture = new AdoptionFixture();
        fixture.Port.FailBackup = true;
        string before = HashFile(fixture.Paths.DatabasePath);
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.Paths.MarkerPath));
        Assert.Equal(before, HashFile(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task MarkerFailureDoesNotChangeDatabase()
    {
        using var fixture = new AdoptionFixture();
        fixture.Port.FailMarker = true;
        string before = HashFile(fixture.Paths.DatabasePath);
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.Paths.MarkerPath));
        Assert.Equal(before, HashFile(fixture.Paths.DatabasePath));
    }

    [Fact]
    public async Task LegacyHostBlocksAdoptionWithoutWrites()
    {
        using var fixture = new AdoptionFixture();
        fixture.Port.LegacyHostRunning = true;
        HostAdoptionState state = fixture.CreateState();
        await state.EnterAsync();

        HostAdoptionResult result = await state.AdoptAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("旧 MAUI", result.Message);
        Assert.False(File.Exists(fixture.Paths.MarkerPath));
        Assert.Equal(0, fixture.BackupCount());
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class AdoptionFixture : IDisposable
    {
        private readonly string root;

        public AdoptionFixture(bool createDatabase = true, bool incompleteSchema = false)
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleAdoption",
                Guid.NewGuid().ToString("N"));
            string dataDirectory = Path.Combine(root, "production", "Data");
            Directory.CreateDirectory(dataDirectory);
            DatabasePath = Path.Combine(dataDirectory, "furinachronicle.db3");
            Paths = new HostAdoptionPaths(
                true,
                Path.Combine(root, "production"),
                DatabasePath,
                Path.Combine(root, "production", "Backups"),
                Path.Combine(root, "production", "UI6", "host-adoption.json"));
            Port = new TestAdoptionPort(Paths);
            if (createDatabase)
            {
                if (incompleteSchema) CreateIncompleteDatabase();
                else CreateCompleteDatabase();
            }
        }

        public string DatabasePath { get; }
        public HostAdoptionPaths Paths { get; }
        public TestAdoptionPort Port { get; }

        public HostAdoptionState CreateState() =>
            new(Port, Paths, "0.10.0", TimeProvider.System);

        public int BackupCount() =>
            Directory.Exists(Paths.BackupsDirectory)
                ? Directory.GetFiles(Paths.BackupsDirectory, "*.db3").Length
                : 0;

        public Task<int> CountRowsAsync()
        {
            using var connection = new SQLiteConnection(
                DatabasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);
            return Task.FromResult(connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM PlayerArchives;"));
        }

        private void CreateCompleteDatabase()
        {
            var database = new FurinaDatabase(
                new SqliteDatabaseOptions(DatabasePath));
            try
            {
                database.InitializeAsync().GetAwaiter().GetResult();
            }
            finally
            {
                database.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
        }

        private void CreateIncompleteDatabase()
        {
            using var connection = new SQLiteConnection(
                DatabasePath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create,
                storeDateTimeAsTicks: true);
            connection.Execute($"PRAGMA application_id = {0x46554348};");
            connection.Execute("PRAGMA user_version = 12;");
            connection.Execute("CREATE TABLE AdoptionFacts (Id TEXT PRIMARY KEY, Label TEXT);");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class TestAdoptionPort(HostAdoptionPaths paths) : IHostAdoptionPort
    {
        public bool FailBackup { get; set; }
        public bool FailMarker { get; set; }
        public bool LegacyHostRunning { get; set; }

        public Task<HostDatabaseInfo> InspectDatabaseAsync(
            CancellationToken cancellationToken = default)
        {
            bool exists = File.Exists(paths.DatabasePath);
            FileInfo? info = exists ? new FileInfo(paths.DatabasePath) : null;
            return Task.FromResult(new HostDatabaseInfo(
                exists,
                info?.Length ?? 0,
                info is null ? null : info.LastWriteTimeUtc));
        }

        public async Task<HostDatabaseValidation> ValidateDatabaseAsync(
            string databasePath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                long length = new FileInfo(databasePath).Length;
                string sha = await ComputeSha256Async(databasePath, cancellationToken);
                using var connection = new SQLiteConnection(
                    databasePath,
                    SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                    storeDateTimeAsTicks: true);
                int applicationId = connection.ExecuteScalar<int>("PRAGMA application_id;");
                int schema = connection.ExecuteScalar<int>("PRAGMA user_version;");
                string quick = connection.ExecuteScalar<string>("PRAGMA quick_check;") ??
                    string.Empty;
                int tables = connection.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM sqlite_master WHERE type='table';");
                bool valid = applicationId == 0x46554348 && schema == 12 &&
                    string.Equals(quick, "ok", StringComparison.OrdinalIgnoreCase);
                return new(
                    applicationId,
                    schema,
                    valid ? "ok" : "failed",
                    valid,
                    length,
                    sha,
                    tables);
            }
            catch (Exception)
            {
                return new(0, 0, "unavailable", false, 0, string.Empty, 0);
            }
        }

        public async Task<HostDatabaseValidation> ValidateFullDatabaseAsync(
            string sourcePath,
            string validationCopyPath,
            CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(validationCopyPath)!);
                File.Copy(sourcePath, validationCopyPath, overwrite: true);
                var database = new FurinaDatabase(
                    new SqliteDatabaseOptions(validationCopyPath));
                try
                {
                    await database.InitializeAsync(cancellationToken);
                }
                finally
                {
                    await database.DisposeAsync();
                }

                return await ValidateDatabaseAsync(validationCopyPath, cancellationToken);
            }
            catch (Exception)
            {
                return new(0, 0, "schema-incomplete", false, 0, string.Empty, 0);
            }
            finally
            {
                foreach (string path in new[]
                {
                    validationCopyPath,
                    validationCopyPath + "-wal",
                    validationCopyPath + "-shm",
                    validationCopyPath + "-journal",
                    validationCopyPath + ".initialize.lock"
                })
                {
                    if (File.Exists(path)) File.Delete(path);
                }
            }
        }

        public async Task<string> ComputeSha256Async(
            string path,
            CancellationToken cancellationToken = default)
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous);
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        }

        public Task EnsureDirectoryAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        public Task BackupAsync(
            string sourcePath,
            string backupPath,
            CancellationToken cancellationToken = default)
        {
            if (FailBackup) return Task.FromException(new IOException("backup failed"));
            using var connection = new SQLiteConnection(
                sourcePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);
            connection.Backup(backupPath, "main");
            return Task.CompletedTask;
        }

        public async Task InitializeNewDatabaseAsync(
            string databasePath,
            CancellationToken cancellationToken = default)
        {
            var database = new FurinaDatabase(new SqliteDatabaseOptions(databasePath));
            try
            {
                await database.InitializeAsync(cancellationToken);
            }
            finally
            {
                await database.DisposeAsync();
            }
        }

        public async Task<HostAdoptionMarker?> ReadMarkerAsync(
            string markerPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(markerPath)) return null;
            return JsonSerializer.Deserialize<HostAdoptionMarker>(
                await File.ReadAllTextAsync(markerPath, cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public async Task WriteMarkerAsync(
            string markerPath,
            HostAdoptionMarker marker,
            CancellationToken cancellationToken = default)
        {
            if (FailMarker) throw new IOException("marker failed");
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            await File.WriteAllTextAsync(
                markerPath,
                JsonSerializer.Serialize(
                    marker,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
        }

        public Task<bool> IsLegacyHostRunningAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LegacyHostRunning);
    }
}

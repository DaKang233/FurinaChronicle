// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FurinaChronicle.Presentation.Management;
using SQLite;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ProductionMigrationStateTests
{
    [Fact]
    public async Task SuccessfulMigrationBacksUpCopiesVerifiesAndIsIdempotent()
    {
        using var fixture = new MigrationFixture();
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();
        Assert.True(state.Snapshot.ShouldPrompt);
        Assert.Equal(
            await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath),
            await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath));

        ProductionMigrationResult result = await state.StartAsync();

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(result.Marker);
        Assert.Equal("Completed", result.Marker.Status);
        Assert.Equal("Reauthorize", result.Marker.CredentialsPolicy);
        Assert.True(File.Exists(fixture.Port.SourcePath));
        Assert.True(File.Exists(fixture.Paths.TargetDatabasePath));
        Assert.True(File.Exists(Path.Combine(
            fixture.Paths.BackupDirectory,
            result.Marker.BackupFileName)));
        Assert.True((await fixture.Port.ValidateDatabaseAsync(
            fixture.Paths.TargetDatabasePath)).IsValid);
        Assert.DoesNotContain(
            Path.GetDirectoryName(fixture.Port.SourcePath)!,
            result.Marker.SourcePathHash);
        Assert.Contains("备份", state.BuildRollbackText());
        Assert.Contains("重新", state.CredentialsText);
        Assert.True(state.Snapshot.TargetValid);

        string sourceHash = await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath);
        int backupCount = fixture.BackupCount();
        ProductionMigrationResult repeated = await state.StartAsync();
        Assert.True(repeated.Succeeded);
        Assert.Contains("未重复", repeated.Message);
        Assert.Equal(backupCount, fixture.BackupCount());
        Assert.Equal(sourceHash, await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath));

        ProductionMigrationState restarted = fixture.CreateState();
        await restarted.EnterAsync();
        Assert.False(restarted.Snapshot.ShouldPrompt);
        Assert.True(restarted.Snapshot.IsCompleted);
    }

    [Fact]
    public async Task ForceRemigrateCreatesAnotherVerifiedBackup()
    {
        using var fixture = new MigrationFixture();
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();
        Assert.True((await state.StartAsync()).Succeeded);
        int before = fixture.BackupCount();
        await Task.Delay(1100);

        ProductionMigrationResult remigrated = await state.StartAsync(forceRemigrate: true);

        Assert.True(remigrated.Succeeded);
        Assert.Equal(before + 1, fixture.BackupCount());
        Assert.True((await fixture.Port.ValidateDatabaseAsync(
            fixture.Paths.TargetDatabasePath)).IsValid);
    }

    [Fact]
    public async Task BackupFailureLeavesSourceAndTargetUnchanged()
    {
        using var fixture = new MigrationFixture();
        fixture.Port.FailBackup = true;
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();
        string sourceHash = await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath);

        ProductionMigrationResult result = await state.StartAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(MigrationStage.Backup, result.Stage);
        Assert.False(File.Exists(fixture.Paths.TargetDatabasePath));
        Assert.Null(await fixture.Port.ReadMarkerAsync(fixture.Paths.MarkerPath));
        Assert.Equal(sourceHash, await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath));
    }

    [Fact]
    public async Task CorruptCopyFailsVerificationWithoutPublishingTarget()
    {
        using var fixture = new MigrationFixture();
        fixture.Port.CorruptCopy = true;
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();
        string sourceHash = await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath);

        ProductionMigrationResult result = await state.StartAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(MigrationStage.Verify, result.Stage);
        Assert.False(File.Exists(fixture.Paths.TargetDatabasePath));
        Assert.Null(await fixture.Port.ReadMarkerAsync(fixture.Paths.MarkerPath));
        Assert.Equal(sourceHash, await fixture.Port.ComputeSha256Async(fixture.Port.SourcePath));
    }

    [Fact]
    public async Task MarkerFailureDoesNotLeavePublishedTarget()
    {
        using var fixture = new MigrationFixture();
        fixture.Port.FailMarker = true;
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();

        ProductionMigrationResult result = await state.StartAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(MigrationStage.Marker, result.Stage);
        Assert.False(File.Exists(fixture.Paths.TargetDatabasePath));
        Assert.Null(await fixture.Port.ReadMarkerAsync(fixture.Paths.MarkerPath));
    }

    [Fact]
    public async Task LegacyHostRunningBlocksMigration()
    {
        using var fixture = new MigrationFixture();
        fixture.Port.LegacyHostRunning = true;
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();

        ProductionMigrationResult result = await state.StartAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("旧宿主", result.Message);
        Assert.False(File.Exists(fixture.Paths.TargetDatabasePath));
    }

    [Fact]
    public async Task MissingSourceDoesNotPromptOrWrite()
    {
        using var fixture = new MigrationFixture(createSource: false);
        ProductionMigrationState state = fixture.CreateState();
        await state.EnterAsync();

        Assert.False(state.Snapshot.ShouldPrompt);
        ProductionMigrationResult result = await state.StartAsync();
        Assert.False(result.Succeeded);
        Assert.False(File.Exists(fixture.Paths.TargetDatabasePath));
        Assert.False(Directory.Exists(fixture.Paths.BackupDirectory));
    }

    private sealed class MigrationFixture : IDisposable
    {
        private readonly string root;
        private readonly string sourceDirectory;

        public MigrationFixture(bool createSource = true)
        {
            root = Path.Combine(
                Path.GetTempPath(),
                "FurinaChronicleMigration",
                Guid.NewGuid().ToString("N"));
            sourceDirectory = Path.Combine(root, "source", "Data");
            Directory.CreateDirectory(sourceDirectory);
            SourcePath = Path.Combine(sourceDirectory, "furinachronicle.db3");
            if (createSource)
            {
                using var connection = new SQLiteConnection(
                    SourcePath,
                    SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create,
                    storeDateTimeAsTicks: true);
                connection.Execute($"PRAGMA application_id = {0x46554348};");
                connection.Execute("PRAGMA user_version = 12;");
                connection.Execute("CREATE TABLE ProbeFacts (Id TEXT PRIMARY KEY, Label TEXT);");
                connection.Execute(
                    "INSERT INTO ProbeFacts (Id, Label) VALUES (?, ?);",
                    "one",
                    "synthetic");
            }

            Paths = new ProductionMigrationPaths(
                Path.Combine(root, "target", "Data"),
                Path.Combine(root, "target", "Data", "furinachronicle.db3"),
                Path.Combine(root, "target", "backups"),
                Path.Combine(root, "target", "migration.json"));
            Port = new TestMigrationPort(SourcePath);
        }

        public string SourcePath { get; }
        public ProductionMigrationPaths Paths { get; }
        public TestMigrationPort Port { get; }

        public ProductionMigrationState CreateState() =>
            new(Port, Paths, "0.10.0", TimeProvider.System);

        public int BackupCount() =>
            Directory.Exists(Paths.BackupDirectory)
                ? Directory.GetFiles(Paths.BackupDirectory, "*.db3").Length
                : 0;

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

    private sealed class TestMigrationPort(string sourcePath) : IProductionMigrationPort
    {
        public string SourcePath { get; } = sourcePath;
        public bool FailBackup { get; set; }
        public bool CorruptCopy { get; set; }
        public bool FailMarker { get; set; }
        public bool LegacyHostRunning { get; set; }

        public Task<ProductionSourceInfo> ProbeSourceAsync(
            CancellationToken cancellationToken = default)
        {
            bool exists = File.Exists(SourcePath);
            FileInfo? info = exists ? new FileInfo(SourcePath) : null;
            return Task.FromResult(new ProductionSourceInfo(
                SourcePath,
                exists,
                info?.Length ?? 0,
                info is null ? null : info.LastWriteTimeUtc,
                true,
                exists ? "fixture" : "missing"));
        }

        public async Task<MigrationDatabaseValidation> ValidateDatabaseAsync(
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

        public Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(File.Exists(path));

        public Task<long> GetFileLengthAsync(
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileInfo(path).Length);

        public Task EnsureDirectoryAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(path);
            return Task.CompletedTask;
        }

        public Task BackupAsync(
            string sourcePathValue,
            string backupPath,
            CancellationToken cancellationToken = default)
        {
            if (FailBackup) return Task.FromException(new IOException("backup failed"));
            using var connection = new SQLiteConnection(
                sourcePathValue,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);
            connection.Backup(backupPath, "main");
            return Task.CompletedTask;
        }

        public async Task CopyAsync(
            string sourcePathValue,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            await using var input = new FileStream(
                sourcePathValue, FileMode.Open, FileAccess.Read, FileShare.Read);
            await using var output = new FileStream(
                targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            if (CorruptCopy)
            {
                byte[] garbage = Encoding.UTF8.GetBytes("not a sqlite database");
                await output.WriteAsync(garbage, cancellationToken);
                return;
            }

            await input.CopyToAsync(output, cancellationToken);
        }

        public Task PublishAsync(
            string temporaryPath,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            File.Move(temporaryPath, targetPath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task DeleteFileAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        public async Task<MigrationMarker?> ReadMarkerAsync(
            string markerPath,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(markerPath)) return null;
            return JsonSerializer.Deserialize<MigrationMarker>(
                await File.ReadAllTextAsync(markerPath, cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }

        public async Task WriteMarkerAsync(
            string markerPath,
            MigrationMarker marker,
            CancellationToken cancellationToken = default)
        {
            if (FailMarker) throw new IOException("marker failed");
            Directory.CreateDirectory(Path.GetDirectoryName(markerPath)!);
            await File.WriteAllTextAsync(
                markerPath,
                JsonSerializer.Serialize(marker, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                cancellationToken);
        }

        public Task<bool> IsLegacyHostRunningAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LegacyHostRunning);
    }
}

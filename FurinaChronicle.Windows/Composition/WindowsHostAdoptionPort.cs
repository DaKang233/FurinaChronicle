// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Presentation.Management;
using SQLite;

namespace FurinaChronicle.Windows.Composition;

// Windows-side adoption port. It only inspects file metadata until the user confirms; the source
// database is opened read-only for validation/backup and is never copied, moved or schema-migrated.
public sealed class WindowsHostAdoptionPort(WindowsPaths paths) : IHostAdoptionPort
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<HostDatabaseInfo> InspectDatabaseAsync(
        CancellationToken cancellationToken = default)
    {
        FileInfo? info = File.Exists(paths.DatabasePath)
            ? new FileInfo(paths.DatabasePath)
            : null;
        return Task.FromResult(new HostDatabaseInfo(
            info is not null,
            info?.Length ?? 0,
            info is null
                ? null
                : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    public async Task<HostDatabaseValidation> ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        long size = 0;
        string sha = string.Empty;
        try
        {
            size = new FileInfo(databasePath).Length;
            sha = await ComputeSha256Async(databasePath, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var connection = new SQLiteConnection(
                databasePath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
                storeDateTimeAsTicks: true);
            int applicationId = connection.ExecuteScalar<int>("PRAGMA application_id;");
            int schema = connection.ExecuteScalar<int>("PRAGMA user_version;");
            string quick = connection.ExecuteScalar<string>("PRAGMA quick_check;") ??
                string.Empty;
            int tables = connection.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table';");
            bool valid = applicationId == 0x46554348 && schema == 12 &&
                string.Equals(quick, "ok", StringComparison.OrdinalIgnoreCase);
            return new(
                applicationId,
                schema,
                valid ? "ok" : "failed",
                valid,
                size,
                sha,
                tables);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(0, 0, "unavailable", false, size, sha, 0);
        }
    }

    public async Task<HostDatabaseValidation> ValidateFullDatabaseAsync(
        string sourcePath,
        string validationCopyPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            string? directory = Path.GetDirectoryName(validationCopyPath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.Copy(sourcePath, validationCopyPath, overwrite: true);
            // The production file stays untouched: only this disposable copy is opened by the
            // full application Schema initializer/migrator.
            var database = new FurinaDatabase(new SqliteDatabaseOptions(validationCopyPath));
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new(0, 0, "schema-incomplete", false, 0, string.Empty, 0);
        }
        finally
        {
            DeleteWithSidecars(validationCopyPath);
        }
    }

    private static void DeleteWithSidecars(string path)
    {
        foreach (string candidate in new[]
        {
            path,
            path + "-wal",
            path + "-shm",
            path + "-journal",
            path + ".initialize.lock"
        })
        {
            try
            {
                if (File.Exists(candidate)) File.Delete(candidate);
            }
            catch (Exception)
            {
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
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(
            await SHA256.HashDataAsync(stream, cancellationToken));
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
        cancellationToken.ThrowIfCancellationRequested();
        using var source = new SQLiteConnection(
            sourcePath,
            SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
        source.Backup(backupPath, "main");
        return Task.CompletedTask;
    }

    public async Task InitializeNewDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
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
        try
        {
            string json = await File.ReadAllTextAsync(markerPath, cancellationToken);
            return JsonSerializer.Deserialize<HostAdoptionMarker>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task WriteMarkerAsync(
        string markerPath,
        HostAdoptionMarker marker,
        CancellationToken cancellationToken = default)
    {
        string? directory = Path.GetDirectoryName(markerPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        string temporary = markerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(marker, JsonOptions),
            cancellationToken);
        File.Move(temporary, markerPath, overwrite: true);
    }

    public Task<bool> IsLegacyHostRunningAsync(
        CancellationToken cancellationToken = default)
    {
        string[] knownNames =
        [
            "FurinaChronicle.App",
            "FurinaChronicle.App.Windows",
            "FurinaChronicle"
        ];
        try
        {
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == Environment.ProcessId) continue;
                    if (knownNames.Contains(
                        process.ProcessName,
                        StringComparer.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(true);
                    }
                }
                catch (Exception)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception)
        {
        }

        // The MAUI initialization lock is a conservative second signal.
        string legacyLock = paths.DatabasePath + ".initialize.lock";
        if (!File.Exists(legacyLock)) return Task.FromResult(false);
        try
        {
            using var probe = new FileStream(
                legacyLock,
                FileMode.Open,
                FileAccess.Read,
                FileShare.None);
            return Task.FromResult(false);
        }
        catch (IOException)
        {
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(true);
        }
    }
}

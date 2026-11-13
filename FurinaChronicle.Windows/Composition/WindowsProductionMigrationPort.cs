// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using FurinaChronicle.Presentation.Management;
using SQLite;

namespace FurinaChronicle.Windows.Composition;

// Windows read-only production probe + mandatory backup/copy/verify port. It never writes the
// source database; only the explicitly selected fixture path (FURINA_UI6_SOURCE_DIRECTORY) or the
// documented MAUI data path is inspected, and only after the user starts a migration.
public sealed class WindowsProductionMigrationPort(WindowsPaths paths)
    : IProductionMigrationPort
{
    public const string SourceDirectoryVariable = "FURINA_UI6_SOURCE_DIRECTORY";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public Task<ProductionSourceInfo> ProbeSourceAsync(
        CancellationToken cancellationToken = default)
    {
        string? configured = Environment.GetEnvironmentVariable(SourceDirectoryVariable);
        bool synthetic = !string.IsNullOrWhiteSpace(configured);
        bool explicitIsolatedMode =
            !synthetic &&
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("FURINA_UI1_DIRECTORY"));
        if (explicitIsolatedMode)
        {
            // An explicit isolated data root (tests, validation, developer runs) must never
            // probe or prompt for the production store. Production probing only happens for
            // the default entry point, which does not set FURINA_UI1_DIRECTORY.
            return Task.FromResult(new ProductionSourceInfo(
                null,
                false,
                0,
                null,
                false,
                "显式隔离模式（FURINA_UI1_DIRECTORY）：不探测生产数据。"));
        }

        string? databasePath;
        if (synthetic)
        {
            string value = configured!.Trim();
            if (File.Exists(value))
            {
                databasePath = value;
            }
            else
            {
                string directory = Directory.Exists(value)
                    ? value
                    : Path.GetFullPath(value);
                databasePath = new[]
                    {
                        Path.Combine(directory, "Data", "furinachronicle.db3"),
                        Path.Combine(directory, "furinachronicle.db3"),
                        Path.Combine(directory, "ui1-preview.db3")
                    }
                    .FirstOrDefault(File.Exists) ??
                    Path.Combine(directory, "Data", "furinachronicle.db3");
            }
        }
        else
        {
            databasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "User Name",
                "com.companyname.furinachronicle.app",
                "Data",
                "furinachronicle.db3");
        }

        bool exists = File.Exists(databasePath);
        FileInfo? info = exists ? new FileInfo(databasePath) : null;
        string status = synthetic
            ? exists
                ? "已发现合成迁移夹具；仅读取元数据。"
                : "合成迁移夹具未发现数据库文件。"
            : exists
                ? "已发现旧版生产数据（只读元数据探测；未读取数据库内容）。"
                : "未发现旧版生产数据；不会自动创建或读取生产库。";
        return Task.FromResult(new ProductionSourceInfo(
            databasePath,
            exists,
            info?.Length ?? 0,
            info is null
                ? null
                : new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            synthetic,
            status));
    }

    public async Task<MigrationDatabaseValidation> ValidateDatabaseAsync(
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
            string quick = connection.ExecuteScalar<string>("PRAGMA quick_check;") ?? string.Empty;
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
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public Task<bool> FileExistsAsync(
        string path,
        CancellationToken cancellationToken = default) =>
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

    public async Task CopyAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        await using var input = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(
            targetPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    public Task PublishAsync(
        string temporaryPath,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
        try
        {
            string json = await File.ReadAllTextAsync(markerPath, cancellationToken);
            return JsonSerializer.Deserialize<MigrationMarker>(json, JsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task WriteMarkerAsync(
        string markerPath,
        MigrationMarker marker,
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
        // A MAUI process is a conservative signal; do not inspect its database.
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
                    if (!knownNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    return Task.FromResult(true);
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

        // The MAUI initialize lock is held only briefly, but if present it is an explicit signal.
        string? sourceDirectory = Path.GetDirectoryName(paths.SessionLockPath);
        if (sourceDirectory is not null)
        {
            string legacyLock = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "User Name",
                "com.companyname.furinachronicle.app",
                "Data",
                "furinachronicle.db3.initialize.lock");
            if (File.Exists(legacyLock))
            {
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

        return Task.FromResult(false);
    }
}

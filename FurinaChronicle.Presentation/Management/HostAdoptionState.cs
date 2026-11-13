// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Presentation.Management;

public sealed record HostDatabaseInfo(
    bool Exists,
    long SizeBytes,
    DateTimeOffset? LastWriteUtc);

public sealed record HostDatabaseValidation(
    int ApplicationId,
    int SchemaVersion,
    string QuickCheck,
    bool IsValid,
    long SizeBytes,
    string Sha256,
    int TableCount);

public sealed record HostAdoptionMarker(
    string MarkerVersion,
    string Status,
    DateTimeOffset CompletedAtUtc,
    string Version,
    string DatabaseFileName,
    string DatabasePathHash,
    long DatabaseSizeBytes,
    string DatabaseSha256,
    string? BackupFileName,
    string? BackupSha256,
    int ApplicationId,
    int SchemaVersion,
    string QuickCheck,
    int TableCount,
    string CredentialsPolicy,
    string ReceiptId,
    bool? FullSchemaValidated = null);

public sealed record HostAdoptionPaths(
    bool IsProduction,
    string DataRoot,
    string DatabasePath,
    string BackupsDirectory,
    string MarkerPath);

public interface IHostAdoptionPort
{
    Task<HostDatabaseInfo> InspectDatabaseAsync(CancellationToken cancellationToken = default);

    Task<HostDatabaseValidation> ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default);

    Task<HostDatabaseValidation> ValidateFullDatabaseAsync(
        string sourcePath,
        string validationCopyPath,
        CancellationToken cancellationToken = default);

    Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default);

    Task EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task BackupAsync(
        string sourcePath,
        string backupPath,
        CancellationToken cancellationToken = default);

    Task InitializeNewDatabaseAsync(string databasePath, CancellationToken cancellationToken = default);

    Task<HostAdoptionMarker?> ReadMarkerAsync(
        string markerPath,
        CancellationToken cancellationToken = default);

    Task WriteMarkerAsync(
        string markerPath,
        HostAdoptionMarker marker,
        CancellationToken cancellationToken = default);

    Task<bool> IsLegacyHostRunningAsync(CancellationToken cancellationToken = default);
}

public sealed record HostAdoptionSnapshot
{
    public HostAdoptionPaths Paths { get; init; } =
        new(false, string.Empty, string.Empty, string.Empty, string.Empty);
    public HostDatabaseInfo Database { get; init; } = new(false, 0, null);
    public HostAdoptionMarker? Marker { get; init; }
    public bool LegacyHostRunning { get; init; }
    public bool DatabaseValid { get; init; }
    public bool IsAdopted { get; init; }
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "正在只读探测现有数据…";
    public string? Error { get; init; }
    public bool RequiresAction => !IsAdopted;
    public bool CanInitialize => !Database.Exists;
}

public sealed record HostAdoptionResult(bool Succeeded, string Message)
{
    public HostAdoptionMarker? Marker { get; init; }
}

// First-run adoption of the same MAUI production database. It never copies/moves/switches the
// database; it validates it read-only, writes a mandatory pre-adoption backup and a sanitized
// marker, then the same host opens that exact path. Isolated mode never constructs this state.
public sealed class HostAdoptionState(
    IHostAdoptionPort port,
    HostAdoptionPaths paths,
    string version,
    TimeProvider timeProvider)
{
    public HostAdoptionSnapshot Snapshot { get; private set; } = new() { Paths = paths };

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Set(Snapshot with
        {
            Paths = paths,
            IsBusy = true,
            Error = null,
            Status = "正在只读探测生产数据路径与接管标记…"
        });
        try
        {
            HostDatabaseInfo database = await port.InspectDatabaseAsync(cancellationToken);
            HostAdoptionMarker? marker = await port.ReadMarkerAsync(
                paths.MarkerPath,
                cancellationToken);
            bool legacy = await port.IsLegacyHostRunningAsync(cancellationToken);
            bool valid = false;
            if (!legacy && marker is not null && database.Exists)
            {
                valid = (await port.ValidateDatabaseAsync(
                    paths.DatabasePath,
                    cancellationToken)).IsValid;
                if (valid && marker.FullSchemaValidated != true)
                {
                    // Markers written before the full-Schema validation fix must not be trusted
                    // just because PRAGMA values look valid. Validate a disposable copy before
                    // allowing startup; an old incomplete marker must fall back to adoption.
                    string validationDirectory = Path.GetDirectoryName(paths.MarkerPath) ??
                        paths.DataRoot;
                    valid = (await port.ValidateFullDatabaseAsync(
                        paths.DatabasePath,
                        Path.Combine(validationDirectory, "adoption-validation.db3"),
                        cancellationToken)).IsValid;
                }
            }

            bool adopted = marker is { Status: "Adopted" or "Initialized" } && database.Exists && valid;
            Set(new HostAdoptionSnapshot
            {
                Paths = paths,
                Database = database,
                Marker = marker,
                LegacyHostRunning = legacy,
                DatabaseValid = valid,
                IsAdopted = adopted,
                Status = legacy
                    ? "检测到旧 MAUI 宿主可能正在运行；请先完全关闭旧宿主。"
                    : adopted
                        ? "已接管现有生产数据：WinUI 将直接使用同一个 furinachronicle.db3。"
                        : database.Exists
                            ? "发现现有生产数据库；需确认接管（只读校验 + 强制备份 + 脱敏标记）。"
                            : "未发现现有生产数据库；可初始化新生产库（不播种合成数据）。"
            });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Set(Snapshot with
            {
                IsBusy = false,
                Error = LogSanitizer.Sanitize(exception.Message),
                Status = "生产数据探测失败；未读取数据库内容，未写入任何数据。"
            });
        }
    }

    public async Task<HostAdoptionResult> AdoptAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.LegacyHostRunning)
        {
            return Fail("检测到旧 MAUI 宿主可能正在运行；请先完全关闭旧宿主，未写入任何数据。");
        }

        if (Snapshot.IsAdopted && !force)
        {
            return new(true, "现有生产数据已完成接管，未重复备份或写入标记。")
            {
                Marker = Snapshot.Marker
            };
        }

        if (!Snapshot.Database.Exists)
        {
            return Fail("未发现现有数据库；请使用“初始化新生产库”。");
        }

        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            Status = "正在只读校验现有生产数据库…"
        });
        try
        {
            HostDatabaseValidation validation = await port.ValidateDatabaseAsync(
                paths.DatabasePath,
                cancellationToken);
            if (!validation.IsValid)
            {
                return Fail("现有数据库只读校验失败（application_id/Schema/quick_check）；" +
                    "未接管、未备份、原库未改变。");
            }

            await port.EnsureDirectoryAsync(paths.BackupsDirectory, cancellationToken);
            string backupName =
                $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}-" +
                $"{Guid.NewGuid().ToString("N")[..8]}-pre-adoption.db3";
            string backupPath = Path.Combine(paths.BackupsDirectory, backupName);
            Set(Snapshot with { Status = "正在写入接管前强制备份…" });
            await port.BackupAsync(paths.DatabasePath, backupPath, cancellationToken);
            HostDatabaseValidation backup = await port.ValidateDatabaseAsync(
                backupPath,
                cancellationToken);
            if (!backup.IsValid)
            {
                return Fail("接管前备份校验失败；未接管、未写标记，原库未改变。");
            }

            Set(Snapshot with { Status = "正在用完整应用 Schema 校验临时副本…" });
            string validationDirectory = Path.GetDirectoryName(paths.MarkerPath) ??
                paths.DataRoot;
            string validationCopyPath = Path.Combine(
                validationDirectory,
                "adoption-validation.db3");
            HostDatabaseValidation fullValidation;
            try
            {
                fullValidation = await port.ValidateFullDatabaseAsync(
                    paths.DatabasePath,
                    validationCopyPath,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return Fail(
                    "数据库结构不完整或无法由当前版本打开；未接管、未修改原库。");
            }

            if (!fullValidation.IsValid)
            {
                return Fail(
                    "数据库结构不完整或无法由当前版本打开；未接管、未修改原库。");
            }

            Set(Snapshot with { Status = "正在写入脱敏接管标记…" });
            HostAdoptionMarker marker = new(
                "ui6-host-adoption-v1",
                "Adopted",
                timeProvider.GetUtcNow(),
                version,
                Path.GetFileName(paths.DatabasePath),
                HashPath(paths.DatabasePath),
                validation.SizeBytes,
                validation.Sha256,
                backupName,
                backup.Sha256,
                validation.ApplicationId,
                validation.SchemaVersion,
                validation.QuickCheck,
                validation.TableCount,
                "Reauthorize",
                Guid.NewGuid().ToString("N"),
                FullSchemaValidated: true);
            await port.WriteMarkerAsync(paths.MarkerPath, marker, cancellationToken);
            Set(Snapshot with
            {
                IsBusy = false,
                Marker = marker,
                DatabaseValid = true,
                IsAdopted = true,
                Status = "接管完成：WinUI 将直接使用现有数据库；备份与标记已写入。"
            });
            return new(
                true,
                "已接管现有生产数据库（同一文件，未复制/未移动）；强制备份与脱敏标记已写入。")
            {
                Marker = marker
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                "接管失败；未写标记、原库未改变。" +
                $"原因：{LogSanitizer.Sanitize(exception.Message)}");
        }
    }

    public async Task<HostAdoptionResult> InitializeAsync(
        bool force = false,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.LegacyHostRunning)
        {
            return Fail("检测到旧 MAUI 宿主可能正在运行；请先完全关闭旧宿主，未写入任何数据。");
        }

        if (Snapshot.Database.Exists && !force)
        {
            return Fail("数据库已存在；请使用“接管现有数据”。");
        }

        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            Status = "正在按 MAUI Schema 初始化新的生产数据库（不播种合成数据）…"
        });
        try
        {
            await port.EnsureDirectoryAsync(
                Path.GetDirectoryName(paths.DatabasePath)!,
                cancellationToken);
            await port.InitializeNewDatabaseAsync(paths.DatabasePath, cancellationToken);
            HostDatabaseValidation validation = await port.ValidateDatabaseAsync(
                paths.DatabasePath,
                cancellationToken);
            if (!validation.IsValid)
            {
                return Fail("新生产库初始化校验失败；未写标记。");
            }

            HostAdoptionMarker marker = new(
                "ui6-host-adoption-v1",
                "Initialized",
                timeProvider.GetUtcNow(),
                version,
                Path.GetFileName(paths.DatabasePath),
                HashPath(paths.DatabasePath),
                validation.SizeBytes,
                validation.Sha256,
                null,
                null,
                validation.ApplicationId,
                validation.SchemaVersion,
                validation.QuickCheck,
                validation.TableCount,
                "Reauthorize",
                Guid.NewGuid().ToString("N"),
                FullSchemaValidated: true);
            await port.WriteMarkerAsync(paths.MarkerPath, marker, cancellationToken);
            Set(Snapshot with
            {
                IsBusy = false,
                Database = new HostDatabaseInfo(
                    true,
                    validation.SizeBytes,
                    timeProvider.GetUtcNow()),
                Marker = marker,
                DatabaseValid = true,
                IsAdopted = true,
                Status = "新生产库已初始化（无合成种子）；WinUI 将直接使用该数据库。"
            });
            return new(
                true,
                "已初始化新的生产数据库（不播种合成数据），脱敏标记已写入。")
            {
                Marker = marker
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(
                "新生产库初始化失败；未写标记。" +
                $"原因：{LogSanitizer.Sanitize(exception.Message)}");
        }
    }

    public string BuildRollbackText()
    {
        string backup = Snapshot.Marker?.BackupFileName ??
            "接管时写入的 Backups 强制备份";
        return "回退边界：数据库未被复制/移动；关闭 WinUI 后可直接用旧 MAUI 宿主打开同一库。" +
            "接管前强制备份保留在 Backups；不要删除原库或备份。回退=回到接管点，" +
            "接管后写入的数据不会被带回。" + Environment.NewLine +
            $"备份文件：{backup}；凭据不会迁移，请重新登录/粘贴 SToken。";
    }

    public string CredentialsText =>
        "旧 SecureStorage 凭据不复制、不解密；需在 WinUI 重新登录或手动粘贴 SToken。" +
        "不迁移旧凭据文件。";

    private HostAdoptionResult Fail(string message)
    {
        Set(Snapshot with
        {
            IsBusy = false,
            Error = message,
            Status = message
        });
        return new(false, message);
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetDirectoryName(path) ?? path)));

    private void Set(HostAdoptionSnapshot snapshot) => Snapshot = snapshot;
}

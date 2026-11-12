// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;

namespace FurinaChronicle.Presentation.Management;

public sealed record ProductionSourceInfo(
    string? DatabasePath,
    bool Exists,
    long SizeBytes,
    DateTimeOffset? LastWriteUtc,
    bool IsSyntheticFixture,
    string Status);

public sealed record MigrationDatabaseValidation(
    int ApplicationId,
    int SchemaVersion,
    string QuickCheck,
    bool IsValid,
    long SizeBytes,
    string Sha256,
    int TableCount);

public sealed record MigrationMarker(
    string MarkerVersion,
    string Status,
    DateTimeOffset CompletedAtUtc,
    string Version,
    string SourceFileName,
    string SourcePathHash,
    long SourceSizeBytes,
    string SourceSha256,
    string BackupFileName,
    string BackupSha256,
    string TargetFileName,
    string TargetSha256,
    int ApplicationId,
    int SchemaVersion,
    string QuickCheck,
    int TableCount,
    string CredentialsPolicy,
    string ReceiptId);

public sealed record ProductionMigrationPaths(
    string TargetDirectory,
    string TargetDatabasePath,
    string BackupDirectory,
    string MarkerPath);

public interface IProductionMigrationPort
{
    Task<ProductionSourceInfo> ProbeSourceAsync(
        CancellationToken cancellationToken = default);

    Task<MigrationDatabaseValidation> ValidateDatabaseAsync(
        string databasePath,
        CancellationToken cancellationToken = default);

    Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default);

    Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken = default);

    Task EnsureDirectoryAsync(string path, CancellationToken cancellationToken = default);

    Task BackupAsync(
        string sourcePath,
        string backupPath,
        CancellationToken cancellationToken = default);

    Task CopyAsync(
        string sourcePath,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task PublishAsync(
        string temporaryPath,
        string targetPath,
        CancellationToken cancellationToken = default);

    Task DeleteFileAsync(string path, CancellationToken cancellationToken = default);

    Task<MigrationMarker?> ReadMarkerAsync(
        string markerPath,
        CancellationToken cancellationToken = default);

    Task WriteMarkerAsync(
        string markerPath,
        MigrationMarker marker,
        CancellationToken cancellationToken = default);

    Task<bool> IsLegacyHostRunningAsync(CancellationToken cancellationToken = default);
}

public enum MigrationStage
{
    Probe = 1,
    ValidateSource = 2,
    Backup = 3,
    Copy = 4,
    Verify = 5,
    Marker = 6,
    Completed = 7
}

public sealed record ProductionMigrationSnapshot
{
    public ProductionSourceInfo? Source { get; init; }
    public MigrationMarker? Marker { get; init; }
    public ProductionMigrationPaths Paths { get; init; } =
        new(string.Empty, string.Empty, string.Empty, string.Empty);
    public bool TargetExists { get; init; }
    public bool TargetValid { get; init; }
    public bool LegacyHostRunning { get; init; }
    public bool ShouldPrompt { get; init; }
    public bool IsBusy { get; init; }
    public MigrationStage Stage { get; init; } = MigrationStage.Probe;
    public string Status { get; init; } = "正在探测旧版数据。";
    public string? Error { get; init; }
    public string SourceDisplayPath => Source?.DatabasePath ?? "未发现";
    public bool IsCompleted => Marker?.Status == "Completed" && TargetValid;
    public string BackupHint => string.IsNullOrWhiteSpace(Paths.BackupDirectory)
        ? string.Empty
        : Path.Combine(Paths.BackupDirectory, "<timestamp>-furinachronicle.db3");
}

public sealed record ProductionMigrationResult(
    bool Succeeded,
    MigrationStage Stage,
    string Message,
    MigrationMarker? Marker = null);

// First-run copy migration. It never moves or edits the source database; a backup is mandatory and
// the target becomes visible only after validation. Production-composition enablement remains a
// separate UI-6b step; this state only prepares and records a verified copy.
public sealed class ProductionMigrationState(
    IProductionMigrationPort port,
    ProductionMigrationPaths paths,
    string version,
    TimeProvider timeProvider)
{
    public ProductionMigrationSnapshot Snapshot { get; private set; } = new();

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        Set(Snapshot with
        {
            Paths = paths,
            IsBusy = true,
            Error = null,
            Status = "正在只读探测旧版数据与迁移标记…"
        });
        try
        {
            ProductionSourceInfo source = await port.ProbeSourceAsync(cancellationToken);
            MigrationMarker? marker = await port.ReadMarkerAsync(
                paths.MarkerPath,
                cancellationToken);
            bool targetExists = await port.FileExistsAsync(
                paths.TargetDatabasePath,
                cancellationToken);
            bool targetValid = false;
            if (targetExists)
            {
                try
                {
                    targetValid = (await port.ValidateDatabaseAsync(
                        paths.TargetDatabasePath,
                        cancellationToken)).IsValid;
                }
                catch (Exception)
                {
                    targetValid = false;
                }
            }

            bool legacy = source.Exists &&
                await port.IsLegacyHostRunningAsync(cancellationToken);
            bool completed = marker?.Status == "Completed" && targetValid;
            bool shouldPrompt = source.Exists && !completed;
            Set(new ProductionMigrationSnapshot
            {
                Source = source,
                Marker = marker,
                Paths = paths,
                TargetExists = targetExists,
                TargetValid = targetValid,
                LegacyHostRunning = legacy,
                ShouldPrompt = shouldPrompt,
                Status = !source.Exists
                    ? "未发现旧版生产数据；宿主继续使用隔离数据目录，不会自动读取生产内容。"
                    : completed
                        ? "已发现完成的迁移记录；源库保留，凭据需在新宿主重新授权。"
                        : legacy
                            ? "发现旧版生产数据，但检测到旧宿主仍可能运行；请先关闭旧宿主。"
                            : "发现旧版生产数据；可只读校验、强制备份并复制到新宿主目录。"
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
                Status = "旧版数据探测失败；未读取生产数据内容，未写入任何数据。"
            });
        }
    }

    public async Task<ProductionMigrationResult> StartAsync(
        bool forceRemigrate = false,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Source is not { Exists: true, DatabasePath: { } sourcePath } source)
        {
            return Fail(MigrationStage.Probe,
                "未发现可迁移的旧版数据库；未写入任何数据。");
        }

        if (Snapshot.LegacyHostRunning)
        {
            return Fail(MigrationStage.Probe,
                "检测到旧宿主可能正在运行；请先完全关闭旧宿主，再重试。未写入任何数据。");
        }

        if (Snapshot.IsCompleted && !forceRemigrate)
        {
            return new(
                true,
                MigrationStage.Completed,
                "迁移记录已完成且目标副本校验通过；未重复复制。重新迁移需明确确认。",
                Snapshot.Marker);
        }

        string targetDirectory = paths.TargetDirectory;
        string temporaryPath = paths.TargetDatabasePath + ".migrating";
        string backupPath = string.Empty;
        Set(Snapshot with
        {
            IsBusy = true,
            Error = null,
            Stage = MigrationStage.ValidateSource,
            Status = "正在只读校验旧版数据库（不写源库）…"
        });
        try
        {
            MigrationDatabaseValidation sourceBefore =
                await port.ValidateDatabaseAsync(sourcePath, cancellationToken);
            if (!sourceBefore.IsValid)
            {
                return Fail(MigrationStage.ValidateSource,
                    "源库只读校验未通过（application_id/Schema/quick_check）；" +
                    "未切换、未复制，源库未改变。");
            }

            Set(Snapshot with
            {
                Stage = MigrationStage.Backup,
                Status = "正在执行强制一致性备份…"
            });
            await port.EnsureDirectoryAsync(paths.BackupDirectory, cancellationToken);
            backupPath = Path.Combine(
                paths.BackupDirectory,
                $"{timeProvider.GetUtcNow():yyyyMMdd-HHmmss-fff}-" +
                $"{Guid.NewGuid().ToString("N")[..8]}-furinachronicle.db3");
            await port.BackupAsync(sourcePath, backupPath, cancellationToken);
            MigrationDatabaseValidation backup =
                await port.ValidateDatabaseAsync(backupPath, cancellationToken);
            if (!backup.IsValid)
            {
                return Fail(MigrationStage.Backup,
                    "迁移备份校验失败；未复制到新宿主、未切换，源库未改变。");
            }

            Set(Snapshot with
            {
                Stage = MigrationStage.Copy,
                Status = "正在把校验通过的备份复制到新宿主目录…"
            });
            await port.EnsureDirectoryAsync(targetDirectory, cancellationToken);
            await port.CopyAsync(backupPath, temporaryPath, cancellationToken);

            Set(Snapshot with
            {
                Stage = MigrationStage.Verify,
                Status = "正在校验新宿主副本（只读）…"
            });
            MigrationDatabaseValidation targetTemp =
                await port.ValidateDatabaseAsync(temporaryPath, cancellationToken);
            string sourceHashAfter = await port.ComputeSha256Async(
                sourcePath,
                cancellationToken);
            if (!targetTemp.IsValid ||
                !string.Equals(backup.Sha256, targetTemp.Sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(sourceBefore.Sha256, sourceHashAfter, StringComparison.OrdinalIgnoreCase))
            {
                await TryDeleteAsync(temporaryPath, cancellationToken);
                return Fail(MigrationStage.Verify,
                    "副本校验失败（完整性/哈希不一致或源库在迁移中发生变化）；" +
                    "未切换，源库与现有目标不变。");
            }

            bool targetExisted = await port.FileExistsAsync(
                paths.TargetDatabasePath,
                cancellationToken);
            string previousPath = paths.TargetDatabasePath + ".previous";
            if (targetExisted)
            {
                await port.CopyAsync(
                    paths.TargetDatabasePath,
                    previousPath,
                    cancellationToken);
            }

            MigrationDatabaseValidation finalTarget;
            try
            {
                await port.PublishAsync(
                    temporaryPath,
                    paths.TargetDatabasePath,
                    cancellationToken);
                finalTarget = await port.ValidateDatabaseAsync(
                    paths.TargetDatabasePath,
                    cancellationToken);
            }
            catch (Exception)
            {
                if (targetExisted) await TryRestoreAsync(previousPath, cancellationToken);
                else await TryDeleteAsync(paths.TargetDatabasePath, cancellationToken);
                throw;
            }

            if (!finalTarget.IsValid)
            {
                if (targetExisted) await TryRestoreAsync(previousPath, cancellationToken);
                else await TryDeleteAsync(paths.TargetDatabasePath, cancellationToken);
                await TryDeleteAsync(temporaryPath, cancellationToken);
                return Fail(MigrationStage.Verify,
                    "最终目标校验失败；已恢复迁移前目标（如存在），" +
                    "源库与备份保留，请重试。");
            }

            Set(Snapshot with
            {
                Stage = MigrationStage.Marker,
                Status = "正在写入迁移标记与回退信息…"
            });
            DateTimeOffset completedAt = timeProvider.GetUtcNow();
            MigrationMarker marker = new(
                "ui6-migration-v1",
                "Completed",
                completedAt,
                version,
                Path.GetFileName(sourcePath),
                HashPath(sourcePath),
                source.SizeBytes,
                sourceBefore.Sha256,
                Path.GetFileName(backupPath),
                backup.Sha256,
                Path.GetFileName(paths.TargetDatabasePath),
                finalTarget.Sha256,
                finalTarget.ApplicationId,
                finalTarget.SchemaVersion,
                finalTarget.QuickCheck,
                finalTarget.TableCount,
                "Reauthorize",
                Guid.NewGuid().ToString("N"));
            try
            {
                await port.WriteMarkerAsync(paths.MarkerPath, marker, cancellationToken);
            }
            catch (Exception)
            {
                if (targetExisted) await TryRestoreAsync(previousPath, cancellationToken);
                else await TryDeleteAsync(paths.TargetDatabasePath, cancellationToken);
                await TryDeleteAsync(previousPath, cancellationToken);
                return Fail(MigrationStage.Marker,
                    "迁移标记写入失败；已恢复迁移前目标（如存在），未切换，源库与备份保留。");
            }

            if (targetExisted) await TryDeleteAsync(previousPath, cancellationToken);
            await TryDeleteAsync(temporaryPath, cancellationToken);
            Set(Snapshot with
            {
                IsBusy = false,
                Marker = marker,
                TargetExists = true,
                TargetValid = true,
                Stage = MigrationStage.Completed,
                Status = "迁移副本已校验并写入迁移标记。源库保留，凭据需在新宿主重新授权。"
            });
            return new(
                true,
                MigrationStage.Completed,
                "迁移完成：源库未被修改，强制备份与目标副本均通过只读校验，迁移标记已写入。" +
                "当前构建仍使用隔离宿主；生产组合根/默认入口切换由 UI-6b 启用。",
                marker);
        }
        catch (OperationCanceledException)
        {
            await TryDeleteAsync(temporaryPath, cancellationToken);
            throw;
        }
        catch (Exception exception)
        {
            await TryDeleteAsync(temporaryPath, cancellationToken);
            return Fail(
                Snapshot.Stage,
                $"迁移在 {Snapshot.Stage} 阶段失败；未切换，源库与现有目标不变。" +
                $"原因：{LogSanitizer.Sanitize(exception.Message)}");
        }
        finally
        {
            Set(Snapshot with { IsBusy = false });
        }
    }

    public string BuildRollbackText()
    {
        string backup = string.IsNullOrWhiteSpace(Snapshot.Marker?.BackupFileName)
            ? "同目录的强制备份文件"
            : Snapshot.Marker!.BackupFileName;
        return "回退边界：源库始终未移动/未修改；迁移前强制备份保留。若需回到迁移点，" +
            "关闭新宿主，继续使用旧宿主或按旧宿主流程恢复；不要删除源库和备份。" +
            "回退=回到迁移点，迁移后在新区写入的数据不会被带回。" +
            Environment.NewLine +
            $"备份文件：{backup}；凭据不会迁移，请在新宿主重新登录/粘贴 SToken。";
    }

    public string CredentialsText =>
        "旧 SecureStorage 凭据不复制、不解密；需在新宿主重新登录或手动粘贴 SToken。" +
        "不迁移旧凭据文件。";

    private ProductionMigrationResult Fail(MigrationStage stage, string message)
    {
        Set(Snapshot with
        {
            IsBusy = false,
            Stage = stage,
            Error = message,
            Status = message
        });
        return new(false, stage, message);
    }

    private async Task TryDeleteAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await port.DeleteFileAsync(path, cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    private async Task TryRestoreAsync(string previousPath, CancellationToken cancellationToken)
    {
        try
        {
            await port.PublishAsync(
                previousPath,
                paths.TargetDatabasePath,
                cancellationToken);
        }
        catch (Exception)
        {
        }
    }

    private static string HashPath(string path) =>
        Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(Path.GetDirectoryName(path) ?? path)));

    private void Set(ProductionMigrationSnapshot snapshot) => Snapshot = snapshot;
}

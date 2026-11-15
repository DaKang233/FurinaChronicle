// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Management;

public enum SettingsTheme
{
    FollowSystem = 0,
    Light = 1,
    Dark = 2
}

public interface ISettingsAppearanceStore
{
    Task<SettingsTheme> LoadThemeAsync(CancellationToken cancellationToken = default);

    Task SaveThemeAsync(SettingsTheme theme, CancellationToken cancellationToken = default);
}

public interface ISettingsStoragePort
{
    string DataRootPath { get; }

    string DatabasePath { get; }

    string CacheDirectoryPath { get; }

    long? GetDatabaseSizeBytes();

    long? GetCacheSizeBytes();

    Task ClearIconCacheAsync(CancellationToken cancellationToken = default);

    Task<string> ExportDiagnosticsAsync(
        string versionText,
        string phaseLabel,
        string commit,
        CancellationToken cancellationToken = default);
}

public interface ISettingsHistoryPort
{
    Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken cancellationToken = default);

    Task<int> GetUndoLimitAsync(Guid archiveId, CancellationToken cancellationToken = default);

    Task<UndoLimitChangeResult> SetUndoLimitAsync(
        Guid archiveId,
        int limit,
        CancellationToken cancellationToken = default);
}

public interface ISettingsApplicationPort
{
    string VersionText { get; }

    string PhaseLabel { get; }

    string Commit { get; }

    string LicenseText { get; }

    string ThirdPartyNoticesText { get; }
}

public sealed record SettingsSnapshot
{
    public SettingsTheme Theme { get; init; } = SettingsTheme.FollowSystem;
    public IReadOnlyList<PlayerArchive> Archives { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public int UndoLimit { get; init; }
    public string DataRootPath { get; init; } = string.Empty;
    public string DatabasePath { get; init; } = string.Empty;
    public string CacheDirectoryPath { get; init; } = string.Empty;
    public long? DatabaseSizeBytes { get; init; }
    public long? CacheSizeBytes { get; init; }
    public string VersionText { get; init; } = string.Empty;
    public string PhaseLabel { get; init; } = string.Empty;
    public string Commit { get; init; } = string.Empty;
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入设置";
    public string? Error { get; init; }

    public PlayerArchive? SelectedArchive =>
        Archives.FirstOrDefault(archive => archive.Id == SelectedArchiveId);

    public string ThemeLabel => Theme switch
    {
        SettingsTheme.Light => "浅色",
        SettingsTheme.Dark => "深色",
        _ => "跟随系统"
    };

    public string DatabaseSizeText => FormatSize(DatabaseSizeBytes);

    public string CacheSizeText => FormatSize(CacheSizeBytes);

    private static string FormatSize(long? bytes) => bytes switch
    {
        null => "未知",
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
    };
}

public sealed record SettingsActionResult(bool Succeeded, string Message);

// Settings page state. Appearance is persisted through the host port (isolated settings file);
// undo retention uses the same operation-history service as the history page, so both pages show
// one source of truth. Storage actions only touch caches for the isolated preview environment.
public sealed class SettingsState(
    ISettingsAppearanceStore appearance,
    ISettingsStoragePort storage,
    ISettingsHistoryPort history,
    ISettingsApplicationPort application)
{
    private Guid? preferredArchiveId;

    public SettingsSnapshot Snapshot { get; private set; } = new();

    public string LicenseText => application.LicenseText;

    public string ThirdPartyNoticesText => application.ThirdPartyNoticesText;

    public Task EnterAsync(CancellationToken cancellationToken = default) =>
        ReloadAsync("设置已载入。", cancellationToken);

    public async Task<SettingsActionResult> SetThemeAsync(
        SettingsTheme theme,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(theme))
        {
            return new(false, "不支持的外观选项。");
        }

        try
        {
            await appearance.SaveThemeAsync(theme, cancellationToken);
            Set(Snapshot with
            {
                Theme = theme,
                Status = $"外观已切换为“{new SettingsSnapshot { Theme = theme }.ThemeLabel}”。",
                Error = null
            });
            return new(true, Snapshot.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public Task SelectArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        return ReloadAsync(null, cancellationToken);
    }

    public async Task<SettingsActionResult> SetUndoLimitAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            return new(false, "请先选择档案。");
        }

        if (limit is < 0 or > 1000)
        {
            string message = "撤销保留次数必须为 0～1000 的整数。";
            Set(Snapshot with { Error = message });
            return new(false, message);
        }

        try
        {
            UndoLimitChangeResult result = await history.SetUndoLimitAsync(
                archiveId,
                limit,
                cancellationToken);
            Set(Snapshot with
            {
                UndoLimit = result.CurrentLimit,
                Status = result.EvictedChangeSetIds.Count == 0
                    ? $"撤销保留次数已设为 {result.CurrentLimit}。"
                    : $"撤销保留次数已设为 {result.CurrentLimit}，" +
                        $"并清理了 {result.EvictedChangeSetIds.Count} 份较早撤销材料。",
                Error = null
            });
            return new(true, Snapshot.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<SettingsActionResult> ClearIconCacheAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await storage.ClearIconCacheAsync(cancellationToken);
            RefreshStorage();
            Set(Snapshot with
            {
                Status = "已清除图标缓存；本地抽卡事实、档案和元数据未改变。",
                Error = null
            });
            return new(true, Snapshot.Status);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    public async Task<SettingsActionResult> ExportDiagnosticsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            string fileName = await storage.ExportDiagnosticsAsync(
                Snapshot.VersionText,
                Snapshot.PhaseLabel,
                Snapshot.Commit,
                cancellationToken);
            string message = $"已导出本地诊断文本：{fileName}（不含抽卡数据内容、凭据或完整用户路径）。";
            Set(Snapshot with { Status = message, Error = null });
            return new(true, message);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = ManagementTexts.DescribeOperationFailure(exception);
            Set(Snapshot with { Error = message });
            return new(false, message);
        }
    }

    private async Task ReloadAsync(string? operationStatus, CancellationToken cancellationToken)
    {
        Set(Snapshot with { IsBusy = true, Status = "正在读取设置…", Error = null });
        try
        {
            SettingsTheme theme = await appearance.LoadThemeAsync(cancellationToken);
            IReadOnlyList<PlayerArchive> archives = await history.GetArchivesAsync(cancellationToken);
            Guid? archiveId = preferredArchiveId is Guid preferred &&
                archives.Any(archive => archive.Id == preferred)
                ? preferred
                : archives.FirstOrDefault()?.Id;
            preferredArchiveId = archiveId;
            int undoLimit = archiveId is Guid selected
                ? await history.GetUndoLimitAsync(selected, cancellationToken)
                : 0;
            long? databaseSize = storage.GetDatabaseSizeBytes();
            long? cacheSize = storage.GetCacheSizeBytes();
            Set(new SettingsSnapshot
            {
                Theme = theme,
                Archives = archives,
                SelectedArchiveId = archiveId,
                UndoLimit = undoLimit,
                DataRootPath = storage.DataRootPath,
                DatabasePath = storage.DatabasePath,
                CacheDirectoryPath = storage.CacheDirectoryPath,
                DatabaseSizeBytes = databaseSize,
                CacheSizeBytes = cacheSize,
                VersionText = application.VersionText,
                PhaseLabel = application.PhaseLabel,
                Commit = application.Commit,
                Status = operationStatus ??
                    "外观、数据与存储、历史和应用信息均来自隔离宿主本地状态。"
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
                Status = "设置读取失败；本地数据未改变。",
                Error = ManagementTexts.DescribeOperationFailure(exception)
            });
        }
    }

    private void RefreshStorage()
    {
        Set(Snapshot with
        {
            DatabaseSizeBytes = storage.GetDatabaseSizeBytes(),
            CacheSizeBytes = storage.GetCacheSizeBytes()
        });
    }

    private void Set(SettingsSnapshot snapshot) => Snapshot = snapshot;
}

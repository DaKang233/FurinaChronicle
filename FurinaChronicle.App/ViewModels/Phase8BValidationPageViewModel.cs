// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Persistence.Sqlite;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.History;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.App.ViewModels;

public sealed record Phase8BValidationDatasetOption(
    ArchiveDomainDatasetKind Kind,
    string DisplayName);

public sealed record Phase8BValidationImportSession(
    OperationId OperationId,
    ArchiveDomainPortablePackage Package,
    ArchiveDomainPortablePlan Plan);

public sealed record Phase8BValidationExportArtifact(
    string FileName,
    MemoryStream Content);

public partial class Phase8BValidationPageViewModel(
    FurinaDatabase database,
    IPlayerArchiveRepository archiveRepository,
    IArchiveDomainPortableFileService portableFiles,
    IArchiveDomainPortableApplier portableApplier,
    ApplicationVersionInfo versionInfo)
    : ObservableObject
{
    public ObservableCollection<PlayerArchive> Archives { get; } = [];

    public IReadOnlyList<Phase8BValidationDatasetOption> DatasetOptions { get; } =
    [
        new(
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            "角色／武器历史"),
        new(
            ArchiveDomainDatasetKind.ChallengeHistory,
            "深境螺旋挑战历史")
    ];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial PlayerArchive? SelectedArchive { get; set; }

    [ObservableProperty]
    public partial Phase8BValidationDatasetOption SelectedDataset { get; set; }
        = null!;

    [ObservableProperty]
    public partial string DiagnosticsText { get; set; } =
        "正在初始化隔离验收数据库。";

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "尚未执行验收操作。";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public async Task InitializeAsync()
    {
        if (SelectedDataset is null)
        {
            SelectedDataset = DatasetOptions[0];
        }

        if (!TryBeginOperation("正在读取隔离验收数据库。"))
        {
            return;
        }

        try
        {
            await ReloadCoreAsync(SelectedArchive?.Id);
            CompleteOperation("隔离验收数据库已就绪。", error: null);
        }
        catch (Exception exception)
        {
            CompleteOperation(
                "隔离验收数据库初始化失败。",
                exception.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ReloadAsync()
    {
        if (!TryBeginOperation("正在重新读取验收状态。"))
        {
            return;
        }

        try
        {
            await ReloadCoreAsync(SelectedArchive?.Id);
            CompleteOperation("验收状态已重新加载。", error: null);
        }
        catch (Exception exception)
        {
            CompleteOperation("重新加载失败。", exception.Message);
        }
    }

    public bool TryBeginOperation(string status)
    {
        if (IsBusy)
        {
            return false;
        }

        IsBusy = true;
        ErrorMessage = null;
        StatusMessage = status;
        return true;
    }

    public void CompleteOperation(string status, string? error)
    {
        StatusMessage = status;
        ErrorMessage = error;
        IsBusy = false;
    }

    public async Task<Phase8BValidationImportSession> PrepareImportAsync(
        Stream source,
        Guid? targetArchiveId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArchiveDomainPortablePackage package =
            await portableFiles.ReadAsync(source, cancellationToken);
        ArchiveDomainPortablePlan plan = await portableFiles.PlanAsync(
            package,
            targetArchiveId,
            cancellationToken);
        SelectedDataset = DatasetOptions.Single(
            option => option.Kind == package.DatasetKind);
        StatusMessage = FormatPlan(package, plan);
        return new Phase8BValidationImportSession(
            OperationId.New(),
            package,
            plan);
    }

    public Task<ArchiveOperationResult> ApplyImportAsync(
        Phase8BValidationImportSession session,
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        DateTimeOffset now = DateTimeOffset.Now;
        return portableApplier.ApplyArchiveDomainPortableAsync(
            new ArchiveDomainPortableApplyRequest(
                session.OperationId,
                session.Package,
                session.Plan,
                now,
                $"Phase 8B.7 validation import: {session.Package.DatasetKind}",
                CaptureUndo: true,
                CleanupConfirmationId: cleanupConfirmationId),
            cancellationToken);
    }

    public async Task RefreshAfterImportAsync(
        Phase8BValidationImportSession session,
        CancellationToken cancellationToken = default)
    {
        await ReloadCoreAsync(session.Plan.TargetArchiveId, cancellationToken);
    }

    public async Task<Phase8BValidationExportArtifact>
        CreatePortableExportAsync(
            CancellationToken cancellationToken = default)
    {
        PlayerArchive archive = RequireArchive();
        ArchiveDomainDatasetKind kind = SelectedDataset.Kind;
        var stream = new MemoryStream();
        try
        {
            await portableFiles.ExportAsync(
                archive.Id,
                kind,
                stream,
                DateTimeOffset.Now,
                cancellationToken);
            stream.Position = 0;
            return new Phase8BValidationExportArtifact(
                $"furina-{DatasetSlug(kind)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json",
                stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public async Task<Phase8BValidationExportArtifact>
        CreateMarkdownExportAsync(
            CancellationToken cancellationToken = default)
    {
        PlayerArchive archive = RequireArchive();
        ArchiveDomainDatasetKind kind = SelectedDataset.Kind;
        var stream = new MemoryStream();
        try
        {
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                             bufferSize: 4096,
                             leaveOpen: true))
            {
                await portableFiles.ExportMarkdownAsync(
                    archive.Id,
                    kind,
                    writer,
                    cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            stream.Position = 0;
            return new Phase8BValidationExportArtifact(
                $"furina-{DatasetSlug(kind)}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.md",
                stream);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    public static string FormatPlan(
        ArchiveDomainPortablePackage package,
        ArchiveDomainPortablePlan plan)
    {
        string target = plan.CreateArchive
            ? "将创建来源同名档案"
            : "将映射到现有档案";
        string summary =
            $"数据集：{DatasetName(package.DatasetKind)}；" +
            $"来源档案：{package.SourceArchiveName}；{target}；" +
            $"账号映射：{plan.Accounts.Count}；新增：{plan.AddCount}；" +
            $"跳过：{plan.SkipCount}；冲突：{plan.ConflictCount}。";
        if (plan.Conflicts.Count > 0)
        {
            summary += Environment.NewLine + string.Join(
                Environment.NewLine,
                plan.Conflicts.Take(5));
        }
        return summary;
    }

    public static string FormatResult(ArchiveOperationResult result) =>
        result.Status switch
        {
            ChangeExecutionStatus.Applied =>
                $"导入完成，写入 {result.AffectedRecordCount} 项。",
            ChangeExecutionStatus.NoOp =>
                "导入为幂等 NoOp，没有产生重复事实或修订。",
            ChangeExecutionStatus.AlreadyCommitted =>
                "同一操作已经提交；已返回原提交结果。",
            ChangeExecutionStatus.Conflict =>
                $"导入冲突：{result.ConflictReason ?? "目标状态已变化。"}",
            ChangeExecutionStatus.NeedsConfirmation =>
                "空间不足，需要确认清理较早的撤销材料后重试。",
            _ => $"导入未提交：{result.Status}。"
        };

    private async Task ReloadCoreAsync(
        Guid? selectedArchiveId,
        CancellationToken cancellationToken = default)
    {
        FurinaDatabaseDiagnostics diagnostics =
            await database.GetDiagnosticsAsync(cancellationToken);
        IReadOnlyList<PlayerArchive> archives =
            await archiveRepository.GetAllAsync(cancellationToken);
        Archives.Clear();
        foreach (PlayerArchive archive in archives)
        {
            Archives.Add(archive);
        }
        SelectedArchive = selectedArchiveId is Guid id
            ? Archives.FirstOrDefault(archive => archive.Id == id) ??
              Archives.FirstOrDefault()
            : Archives.FirstOrDefault();

        DiagnosticsText =
            "模式：Phase 8B.7 隔离验收（不使用主数据库）" +
            Environment.NewLine +
            $"平台：{DeviceInfo.Platform} / {DeviceInfo.VersionString}" +
            Environment.NewLine +
            $"版本：{versionInfo.Version}" + Environment.NewLine +
            $"Phase：{versionInfo.PhaseLabel}" + Environment.NewLine +
            $"Commit：{versionInfo.Commit}；Dirty：{versionInfo.Dirty}" +
            Environment.NewLine +
            $"数据库：Schema {diagnostics.SchemaVersion}；" +
            $"application_id 0x{diagnostics.ApplicationId:X8}；" +
            $"quick_check {diagnostics.IntegrityCheck}";
    }

    private PlayerArchive RequireArchive() => SelectedArchive ??
        throw new InvalidOperationException(
            "请先导入数据或选择一个验收档案。空验收库不能导出业务数据集。");

    private bool CanRun() => !IsBusy;

    private static string DatasetSlug(ArchiveDomainDatasetKind kind) =>
        kind switch
        {
            ArchiveDomainDatasetKind.CharacterWeaponHistory =>
                "character-weapon-history",
            ArchiveDomainDatasetKind.ChallengeHistory =>
                "challenge-history",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

    private static string DatasetName(ArchiveDomainDatasetKind kind) =>
        kind switch
        {
            ArchiveDomainDatasetKind.CharacterWeaponHistory =>
                "角色／武器历史",
            ArchiveDomainDatasetKind.ChallengeHistory =>
                "深境螺旋挑战历史",
            _ => kind.ToString()
        };
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Persistence;
using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Abstractions;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.HistoryDomains.Portable;

namespace FurinaChronicle.App.ViewModels;

public sealed record ArchiveDatasetOption(
    ArchiveDomainDatasetKind Kind,
    string DisplayName,
    string FileSlug);

public sealed class ArchiveTransferExportArtifact(
    string fileName,
    string temporaryPath,
    string summary) : IAsyncDisposable
{
    public string FileName { get; } = fileName;

    public string TemporaryPath { get; } = temporaryPath;

    public string Summary { get; } = summary;

    public ValueTask DisposeAsync()
    {
        try
        {
            if (File.Exists(TemporaryPath))
            {
                File.Delete(TemporaryPath);
            }
        }
        catch (IOException)
        {
            // The temporary export is best-effort cleanup material.
        }
        catch (UnauthorizedAccessException)
        {
            // A later cache cleanup can remove a provider-held temporary file.
        }
        return ValueTask.CompletedTask;
    }
}

public partial class ArchiveTransferPageViewModel(
    IPlayerArchiveRepository archiveRepository,
    IGameAccountRepository accountRepository,
    IArchiveLogicalExporter logicalExporter,
    IArchiveRestoreInputStager restoreInputStager,
    IArchiveRestorePlanner restorePlanner,
    IArchiveAtomicRestorer atomicRestorer,
    IArchiveDomainPortableFileService domainPortableFiles,
    ExportGachaMarkdown exportGachaMarkdown,
    ApplicationVersionInfo versionInfo,
    TimeProvider timeProvider) : ObservableObject, IAsyncDisposable
{
    private readonly List<ArchiveRestoreArchiveDecision> archiveDecisions = [];
    private readonly List<ArchiveRestoreAccountDecision> accountDecisions = [];
    private readonly List<ArchiveRestoreConfirmation> confirmations = [];
    private readonly List<ArchiveReplaceSelection> replaceSelections = [];
    private IArchiveRestoreStagedPackage? stagedPackage;
    private OperationId? operationId;

    public ObservableCollection<PlayerArchive> Archives { get; } = [];

    public IReadOnlyList<ArchiveDatasetOption> DatasetOptions { get; } =
    [
        new(
            ArchiveDomainDatasetKind.CharacterWeaponHistory,
            "角色／武器历史",
            "character-weapon-history"),
        new(
            ArchiveDomainDatasetKind.ChallengeHistory,
            "挑战历史",
            "challenge-history")
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanInteractWithPlan))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial PlayerArchive? SelectedArchive { get; set; }

    [ObservableProperty]
    public partial ArchiveDatasetOption SelectedDataset { get; set; } = null!;

    [ObservableProperty]
    public partial bool ExportAllArchives { get; set; } = true;

    [ObservableProperty]
    public partial bool ApplyPortableSettings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPlan))]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(CanInteractWithPlan))]
    public partial ArchiveRestorePlan? CurrentPlan { get; private set; }

    public bool HasPlan => CurrentPlan is not null;

    public bool CanApply => CurrentPlan?.CanApply == true && !IsBusy;

    public bool CanConfirm =>
        CurrentPlan is ArchiveRestorePlan plan &&
        !IsBusy &&
        plan.Archives.All(item =>
            item.Kind != ArchiveRestoreArchiveMappingKind.RequiresSelection) &&
        plan.Accounts.All(item =>
            item.Kind is not ArchiveRestoreAccountMappingKind.RequiresSelection and
                not ArchiveRestoreAccountMappingKind.Collision) &&
        plan.Conflicts.Count == 0;

    public bool CanInteractWithPlan => HasPlan && !IsBusy;

    [ObservableProperty]
    public partial string PackageSummary { get; private set; } =
        "尚未选择完整逻辑 Archive。";

    [ObservableProperty]
    public partial string PlanSummary { get; private set; } =
        "选择文件后才会生成恢复预览。";

    [ObservableProperty]
    public partial string MappingSummary { get; private set; } =
        "尚无档案或账号映射。";

    [ObservableProperty]
    public partial string DiagnosticSummary { get; private set; } =
        "尚无冲突或确认项。";

    [ObservableProperty]
    public partial string RecoveryPointSummary { get; private set; } =
        "正在读取设备本地恢复点状态。";

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } =
        "可以导出或选择 Archive 文件进行预览。";

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public ArchiveCatalog? StagedCatalog => stagedPackage?.Catalog;

    public ArchiveRestoreMode RestoreMode { get; private set; } =
        ArchiveRestoreMode.Merge;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedDataset is null)
        {
            SelectedDataset = DatasetOptions[0];
        }

        Guid? selectedId = SelectedArchive?.Id;
        IReadOnlyList<PlayerArchive> archives =
            await archiveRepository.GetAllAsync(cancellationToken);
        Archives.Clear();
        foreach (PlayerArchive archive in archives)
        {
            Archives.Add(archive);
        }
        SelectedArchive = selectedId is Guid id
            ? Archives.FirstOrDefault(item => item.Id == id) ??
              Archives.FirstOrDefault()
            : Archives.FirstOrDefault();
        await RefreshRecoveryPointAsync(cancellationToken);
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
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanInteractWithPlan));
        return true;
    }

    public void CompleteOperation(string status, string? error = null)
    {
        StatusMessage = status;
        ErrorMessage = error;
        IsBusy = false;
        OnPropertyChanged(nameof(CanApply));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanInteractWithPlan));
    }

    public async Task<ArchiveTransferExportArtifact> CreateArchiveExportAsync(
        CancellationToken cancellationToken = default)
    {
        ArchiveExportScope scope = ExportAllArchives
            ? ArchiveExportScope.All()
            : ArchiveExportScope.Selected(
                [RequireSelectedArchive().Id]);
        string path = CreateTemporaryPath(".fca");
        try
        {
            ArchiveWriteResult result;
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                result = await logicalExporter.ExportAsync(
                    destination,
                    scope,
                    CreateGeneratorInfo(),
                    cancellationToken);
            }

            ArchivePackageSummary summary = result.Summary;
            return new ArchiveTransferExportArtifact(
                $"FurinaChronicle_Archive_{timeProvider.GetLocalNow():yyyyMMdd_HHmmss}.fca",
                path,
                $"完整逻辑 Archive：{summary.Catalog.Archives.Count} 个档案、" +
                $"{summary.Catalog.AccountCount} 个账号、" +
                $"{summary.GachaCurrentFactCount + summary.DomainFactCount} 条当前事实、" +
                $"{summary.GachaRevisionCount + summary.DomainRevisionCount} 条修订。");
        }
        catch
        {
            DeleteTemporaryFile(path);
            throw;
        }
    }

    public async Task<ArchiveTransferExportArtifact> CreateDomainExportAsync(
        bool markdown,
        CancellationToken cancellationToken = default)
    {
        PlayerArchive archive = RequireSelectedArchive();
        ArchiveDatasetOption dataset = SelectedDataset ??
            throw new InvalidOperationException("请先选择分类数据集。");
        string extension = markdown ? ".md" : ".json";
        string path = CreateTemporaryPath(extension);
        try
        {
            await using var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (markdown)
            {
                await using var writer = new StreamWriter(
                    destination,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    bufferSize: 81920,
                    leaveOpen: true);
                await domainPortableFiles.ExportMarkdownAsync(
                    archive.Id,
                    dataset.Kind,
                    writer,
                    cancellationToken);
                await writer.FlushAsync(cancellationToken);
            }
            else
            {
                await domainPortableFiles.ExportAsync(
                    archive.Id,
                    dataset.Kind,
                    destination,
                    timeProvider.GetUtcNow(),
                    cancellationToken);
            }

            string kind = markdown
                ? "只读 Markdown（不可重新导入）"
                : "分类 Portable（internal / unpublished，可重新导入）";
            return new ArchiveTransferExportArtifact(
                $"furina-{dataset.FileSlug}-{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}{extension}",
                path,
                $"{kind}：档案“{archive.Name}”，数据集“{dataset.DisplayName}”。");
        }
        catch
        {
            DeleteTemporaryFile(path);
            throw;
        }
    }

    public async Task<ArchiveTransferExportArtifact> CreateGachaMarkdownAsync(
        bool includeDetails,
        CancellationToken cancellationToken = default)
    {
        PlayerArchive archive = RequireSelectedArchive();
        IReadOnlyList<GameAccount> accounts =
            await accountRepository.GetByArchiveIdAsync(
                archive.Id,
                cancellationToken);
        if (accounts.Count == 0)
        {
            throw new InvalidOperationException(
                "所选档案没有可导出的游戏账号。");
        }

        string path = CreateTemporaryPath(".md");
        try
        {
            GachaExportResult result;
            await using (var destination = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                result = await exportGachaMarkdown.ExecuteAsync(
                    destination,
                    archive.Id,
                    accounts.Select(item => item.Id).ToArray(),
                    new GachaMarkdownExportOptions(includeDetails),
                    cancellationToken);
            }
            string suffix = includeDetails ? "details" : "summary";
            return new ArchiveTransferExportArtifact(
                $"furina-gacha-{suffix}-{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.md",
                path,
                $"抽卡 Markdown { (includeDetails ? "明细" : "摘要") }：" +
                $"{result.AccountCount} 个账号、{result.RecordCount} 条记录；不可重新导入。");
        }
        catch
        {
            DeleteTemporaryFile(path);
            throw;
        }
    }

    public async Task StageRestoreAsync(
        Stream source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        await ReleaseRestoreSessionAsync();
        stagedPackage = await restoreInputStager.StageAsync(
            source,
            cancellationToken);
        operationId = OperationId.New();
        archiveDecisions.Clear();
        accountDecisions.Clear();
        confirmations.Clear();
        replaceSelections.Clear();
        ApplyPortableSettings = false;
        CurrentPlan = null;

        ArchivePackageSummary summary = stagedPackage.Summary;
        PackageSummary =
            $"Archive v{summary.Manifest.FormatVersion}；" +
            $"{summary.Catalog.Archives.Count} 个档案、" +
            $"{summary.Catalog.AccountCount} 个账号；" +
            $"{summary.GachaCurrentFactCount + summary.DomainFactCount} 条当前事实、" +
            $"{summary.GachaRevisionCount + summary.DomainRevisionCount} 条修订；" +
            $"容器 {stagedPackage.ContainerLength} 字节，" +
            $"staging {stagedPackage.StagingLength} 字节。";
        StatusMessage = "文件已在应用私有目录完成校验；请选择恢复方式并生成预览。";
    }

    public async Task PlanRestoreAsync(
        ArchiveRestoreMode mode,
        ArchiveReplaceSelection? replaceSelection = null,
        CancellationToken cancellationToken = default)
    {
        RequireStagedPackage();
        RestoreMode = mode;
        replaceSelections.Clear();
        if (mode == ArchiveRestoreMode.Replace)
        {
            replaceSelections.Add(replaceSelection ??
                throw new InvalidOperationException(
                    "Replace 必须明确选择档案、账号或领域范围。"));
        }
        await ReplanAsync(cancellationToken);
    }

    public async Task ResolveArchiveAsync(
        Guid sourceArchiveReference,
        Guid? targetArchiveId,
        CancellationToken cancellationToken = default)
    {
        archiveDecisions.RemoveAll(item =>
            item.SourceArchiveReference == sourceArchiveReference);
        archiveDecisions.Add(targetArchiveId is Guid target
            ? new ArchiveRestoreArchiveDecision(
                sourceArchiveReference,
                ArchiveRestoreArchiveDecisionKind.MapExisting,
                TargetArchiveId: target)
            : new ArchiveRestoreArchiveDecision(
                sourceArchiveReference,
                ArchiveRestoreArchiveDecisionKind.CreateNew));
        await ReplanAsync(cancellationToken);
    }

    public async Task ResolveAccountAsync(
        Guid sourceAccountReference,
        Guid? targetAccountId,
        CancellationToken cancellationToken = default)
    {
        accountDecisions.RemoveAll(item =>
            item.SourceAccountReference == sourceAccountReference);
        accountDecisions.Add(targetAccountId is Guid target
            ? new ArchiveRestoreAccountDecision(
                sourceAccountReference,
                ArchiveRestoreAccountDecisionKind.MapExisting,
                target)
            : new ArchiveRestoreAccountDecision(
                sourceAccountReference,
                ArchiveRestoreAccountDecisionKind.CreateNew));
        await ReplanAsync(cancellationToken);
    }

    public Task<IReadOnlyList<GameAccount>> GetTargetAccountsAsync(
        Guid targetArchiveId,
        CancellationToken cancellationToken = default) =>
        accountRepository.GetByArchiveIdAsync(
            targetArchiveId,
            cancellationToken);

    public async Task SetApplyPortableSettingsAsync(
        bool apply,
        CancellationToken cancellationToken = default)
    {
        ApplyPortableSettings = apply;
        await ReplanAsync(cancellationToken);
    }

    public async Task AcceptPendingConfirmationsAsync(
        CancellationToken cancellationToken = default)
    {
        ArchiveRestorePlan plan = CurrentPlan ??
            throw new InvalidOperationException("尚未生成恢复预览。");
        foreach (ArchiveRestoreConfirmationRequirement requirement in
            plan.Confirmations.Where(item => !item.Satisfied))
        {
            confirmations.RemoveAll(item =>
                item.Kind == requirement.Kind &&
                item.Reference == requirement.Reference);
            confirmations.Add(new ArchiveRestoreConfirmation(
                requirement.Kind,
                requirement.Reference,
                requirement.MarkerKey,
                requirement.MarkerVersion));
        }
        await ReplanAsync(cancellationToken);
    }

    public async Task<ArchiveRestoreApplyResult> ApplyRestoreAsync(
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        IArchiveRestoreStagedPackage package = RequireStagedPackage();
        ArchiveRestorePlan plan = CurrentPlan ??
            throw new InvalidOperationException("尚未生成恢复预览。");
        if (!plan.CanApply)
        {
            throw new InvalidOperationException(
                "恢复预览仍有未解决映射、冲突或确认项。");
        }
        OperationId id = operationId ??
            throw new InvalidOperationException("恢复操作 ID 不存在。");
        DateTimeOffset startedAt = timeProvider.GetUtcNow();
        try
        {
            return await atomicRestorer.ApplyAsync(
                new ArchiveRestoreApplyRequest(
                    package,
                    plan,
                    id,
                    startedAt,
                    timeProvider.GetUtcNow(),
                    plan.Mode == ArchiveRestoreMode.Replace
                        ? "Restore Furina Archive with scoped Replace"
                        : "Restore Furina Archive with Merge",
                    cleanupConfirmationId),
                cancellationToken);
        }
        catch
        {
            ArchiveRestoreReceipt? receipt =
                await atomicRestorer.FindReceiptAsync(
                    id,
                    CancellationToken.None);
            if (receipt is not null)
            {
                return new ArchiveRestoreApplyResult(
                    ChangeExecutionStatus.AlreadyCommitted,
                    receipt);
            }
            throw;
        }
    }

    public async Task RefreshAfterRestoreAsync(
        CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken);
        await RefreshRecoveryPointAsync(cancellationToken);
    }

    public async Task ReleaseRestoreSessionAsync()
    {
        if (stagedPackage is not null)
        {
            await stagedPackage.DisposeAsync();
            stagedPackage = null;
        }
        operationId = null;
        CurrentPlan = null;
        archiveDecisions.Clear();
        accountDecisions.Clear();
        confirmations.Clear();
        replaceSelections.Clear();
        PackageSummary = "尚未选择完整逻辑 Archive。";
        PlanSummary = "选择文件后才会生成恢复预览。";
        MappingSummary = "尚无档案或账号映射。";
        DiagnosticSummary = "尚无冲突或确认项。";
    }

    public async ValueTask DisposeAsync()
    {
        await ReleaseRestoreSessionAsync();
    }

    public static string FormatApplyResult(ArchiveRestoreApplyResult result)
    {
        ArchiveRestoreReceipt? receipt = result.Receipt;
        return result.Status switch
        {
            ChangeExecutionStatus.Applied =>
                $"恢复已提交：{receipt?.AffectedRecordCount ?? 0} 项；" +
                $"Receipt {receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.NoOp =>
                $"恢复为幂等 NoOp；Receipt {receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.AlreadyCommitted =>
                $"该操作此前已经提交，已按持久化 Receipt 恢复结果：" +
                $"{receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.NeedsConfirmation =>
                "空间不足，需要确认清理计划列出的较早撤销材料后重试。",
            ChangeExecutionStatus.Conflict =>
                $"恢复未提交：{result.ConflictReason ?? "目标状态已变化，请重新预览。"}",
            _ => $"恢复未提交：{result.Status}。"
        };
    }

    private async Task ReplanAsync(CancellationToken cancellationToken)
    {
        IArchiveRestoreStagedPackage package = RequireStagedPackage();
        CurrentPlan = await restorePlanner.PlanAsync(
            new ArchiveRestorePlanRequest(
                package,
                archiveDecisions,
                accountDecisions,
                confirmations,
                ApplyPortableSettings,
                RestoreMode,
                replaceSelections),
            cancellationToken);
        UpdatePlanPresentation(CurrentPlan);
    }

    private void UpdatePlanPresentation(ArchiveRestorePlan plan)
    {
        PlanSummary =
            $"模式：{(plan.Mode == ArchiveRestoreMode.Merge ? "Merge（补充／合并）" : "Scoped Replace（明确范围替换）")}；" +
            $"档案 新建 {plan.Preview.ArchiveCreateCount} / 复用 {plan.Preview.ArchiveReuseCount}；" +
            $"账号 新建 {plan.Preview.AccountCreateCount} / 复用 {plan.Preview.AccountReuseCount} / 删除 {plan.Preview.AccountRemoveCount}；" +
            $"冲突 {plan.Preview.ConflictCount}；Tombstone {plan.Preview.TombstoneCount}。" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                plan.Preview.Datasets.Select(item =>
                    $"{FactKindName(item.Kind)}：新增 {item.AddCount}，保留 {item.SkipCount}，" +
                    $"替换 {item.ReplaceCount}，移除 {item.RemoveCount}，" +
                    $"冲突 {item.ConflictCount}，重引入 {item.TombstoneCount}。")) +
            Environment.NewLine +
            $"预计额外数据库 {plan.Resources.EstimatedAdditionalDatabaseBytes} 字节；" +
            $"Undo {plan.Resources.EstimatedUndoBytes} 字节；" +
            $"恢复点 {plan.Resources.EstimatedRecoveryPointBytes} 字节。";

        MappingSummary = string.Join(
            Environment.NewLine,
            plan.Archives.Select(item =>
                $"档案“{item.SourceName}” → {ArchiveMappingName(item)}")
            .Concat(plan.Accounts.Select(item =>
                $"账号 {SourceAccountLabel(item.SourceAccountReference)} → {AccountMappingName(item)}")));
        if (string.IsNullOrWhiteSpace(MappingSummary))
        {
            MappingSummary = "所选范围没有父对象映射。";
        }

        var diagnostics = new List<string>();
        diagnostics.AddRange(plan.Conflicts.Select(item =>
            $"[冲突/{item.Code}] {item.Message}"));
        diagnostics.AddRange(plan.Confirmations.Where(item => !item.Satisfied)
            .Select(item => $"[待确认/{item.Kind}] {item.Message}"));
        diagnostics.AddRange(plan.Undo.IneligibleReasons.Select(item =>
            $"[不可撤销] {item}"));
        DiagnosticSummary = diagnostics.Count == 0
            ? "没有阻塞冲突或待确认项。"
            : string.Join(Environment.NewLine, diagnostics);
        StatusMessage = plan.CanApply
            ? "预览已满足全部前置条件，可以提交。"
            : "预览尚有待处理的映射、冲突或确认项；数据库未改变。";
    }

    private async Task RefreshRecoveryPointAsync(
        CancellationToken cancellationToken)
    {
        ArchiveReplaceRecoveryPoint? point =
            await atomicRestorer.FindCurrentRecoveryPointAsync(
                cancellationToken);
        RecoveryPointSummary = point is null
            ? "当前没有设备本地 Replace 恢复点。"
            : $"当前恢复点：{point.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}；" +
              $"{point.Length} 字节；Schema {point.DatabaseSchemaVersion}；" +
              $"校验 {(point.IsValid ? "有效" : "无效")}。" +
              " 8C.5 只显示状态，不提供主库回切。";
    }

    private ArchiveGeneratorInfo CreateGeneratorInfo() => new(
        "FurinaChronicle",
        versionInfo.Version,
        versionInfo.PhaseLabel,
        versionInfo.Commit,
        DeviceInfo.Platform.ToString());

    private PlayerArchive RequireSelectedArchive() =>
        SelectedArchive ?? throw new InvalidOperationException(
            "请先选择档案。");

    private IArchiveRestoreStagedPackage RequireStagedPackage() =>
        stagedPackage ?? throw new InvalidOperationException(
            "请先选择并校验完整逻辑 Archive。");

    private string SourceAccountLabel(Guid sourceAccountReference)
    {
        ArchiveCatalogAccount? account = stagedPackage?.Catalog.Archives
            .SelectMany(item => item.Accounts)
            .FirstOrDefault(item =>
                item.AccountReference == sourceAccountReference);
        return account is null
            ? sourceAccountReference.ToString("D")
            : string.IsNullOrWhiteSpace(account.DisplayName)
                ? account.LegacyUid
                : $"{account.DisplayName} ({account.LegacyUid})";
    }

    private static string ArchiveMappingName(
        ArchiveRestoreArchiveMapping mapping) =>
        mapping.Kind switch
        {
            ArchiveRestoreArchiveMappingKind.MapExisting =>
                $"现有档案 {mapping.ProposedName}",
            ArchiveRestoreArchiveMappingKind.CreateNew =>
                $"新建档案 {mapping.ProposedName}",
            _ => "需要手动选择"
        };

    private static string AccountMappingName(
        ArchiveRestoreAccountMapping mapping) =>
        mapping.Kind switch
        {
            ArchiveRestoreAccountMappingKind.MapExisting =>
                $"现有账号 {mapping.TargetAccountId:D}",
            ArchiveRestoreAccountMappingKind.CreateNew =>
                $"新建账号 {mapping.ProposedAccountId:D}",
            ArchiveRestoreAccountMappingKind.Collision => "身份冲突",
            _ => "需要手动选择"
        };

    private static string FactKindName(ArchiveRestoreFactKind kind) =>
        kind switch
        {
            ArchiveRestoreFactKind.GachaCurrent => "抽卡当前事实",
            ArchiveRestoreFactKind.GachaRevision => "抽卡修订",
            ArchiveRestoreFactKind.ItemObservation => "物品观察",
            ArchiveRestoreFactKind.ManualAcquisition => "手工获取",
            ArchiveRestoreFactKind.ManualAcquisitionRevision => "手工获取修订",
            ArchiveRestoreFactKind.ChallengeObservation => "挑战观察",
            _ => kind.ToString()
        };

    private static string CreateTemporaryPath(string extension)
    {
        string directory = Path.Combine(
            PlatformStoragePaths.CacheDirectory,
            "archive-product-exports");
        Directory.CreateDirectory(directory);
        return Path.Combine(
            directory,
            $"{Guid.NewGuid():N}{extension}");
    }

    private static void DeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best-effort cache cleanup; keep the original export failure.
        }
        catch (UnauthorizedAccessException)
        {
            // A provider-held temporary file can be removed by cache cleanup.
        }
    }
}

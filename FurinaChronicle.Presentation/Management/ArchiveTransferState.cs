// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Presentation.Management;

// Platform port for the presentation state: file staging/publishing and temporary cleanup live in
// the Windows host, while export/restore planning stays platform-neutral.
public interface IArchiveTransferWorkspace
{
    string TemporaryDirectory { get; }

    Task PublishAsync(
        string temporaryPath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    void DeleteTemporary(string path);
}

// Read/export/restore services for the logical .fca workflow. This is the formal production
// implementation (ArchiveLogicalExporter / SqliteArchiveRestoreInputStager / PlanArchiveRestore /
// SqliteGachaAtomicChangeStore), not a SQLite file copy path.
public interface IArchiveTransferSource
{
    Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default);

    Task<ArchiveWriteResult> ExportAsync(
        Stream destination,
        ArchiveExportScope scope,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default);

    Task<IArchiveRestoreStagedPackage> StageAsync(
        Stream source,
        CancellationToken cancellationToken = default);

    Task<ArchiveRestorePlan> PlanAsync(
        ArchiveRestorePlanRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveRestorePlanFreshness> CheckFreshnessAsync(
        ArchiveRestorePlan plan,
        CancellationToken cancellationToken = default);

    Task<ArchiveRestoreApplyResult> ApplyAsync(
        ArchiveRestoreApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<ArchiveRestoreReceipt?> FindReceiptAsync(
        OperationId operationId,
        CancellationToken cancellationToken = default);

    Task<ArchiveReplaceRecoveryPoint?> FindCurrentRecoveryPointAsync(
        CancellationToken cancellationToken = default);
}

public sealed record ArchiveTransferSnapshot
{
    public IReadOnlyList<PlayerArchive> Archives { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public bool ExportAllArchives { get; init; } = true;
    public bool HasPackage { get; init; }
    public bool HasPlan { get; init; }
    public bool CanApply { get; init; }
    public ArchiveRestorePlan? Plan { get; init; }
    public bool ApplyPortableSettings { get; init; }
    public string PackageSummary { get; init; } = "尚未选择逻辑档案文件。";
    public string PlanSummary { get; init; } = "选择文件并生成预览后显示。";
    public string MappingSummary { get; init; } = "尚无档案或账号映射。";
    public string DiagnosticSummary { get; init; } = "尚无冲突或待确认项。";
    public string RecoveryPointSummary { get; init; } = "正在读取设备本地恢复点状态。";
    public string Status { get; init; } = "可以导出或选择 .fca 文件进行预览。";
    public string? Error { get; init; }
    public bool IsBusy { get; init; }

    public PlayerArchive? SelectedArchive =>
        Archives.FirstOrDefault(archive => archive.Id == SelectedArchiveId);

    public string ExportScopeLabel => ExportAllArchives
        ? "全部档案"
        : SelectedArchive is null ? "尚未选择档案" : $"所选档案：{SelectedArchive.Name}";
}

public sealed record ArchiveTransferActionResult(bool Succeeded, string Message);

public sealed record ArchiveTransferApplyOutcome(
    bool Succeeded,
    string Message,
    ArchiveRestoreApplyResult? Result)
{
    public ArchiveRestoreReceipt? Receipt => Result?.Receipt;
}

// Page-local export / staged-package / restore-plan state. It never applies a plan without a
// freshness check and never treats a missing source dataset as a request to clear target facts.
public sealed class ArchiveTransferState(
    IArchiveTransferSource source,
    IArchiveTransferWorkspace workspace,
    TimeProvider timeProvider)
{
    private readonly List<ArchiveRestoreArchiveDecision> archiveDecisions = [];
    private readonly List<ArchiveRestoreAccountDecision> accountDecisions = [];
    private readonly List<ArchiveRestoreConfirmation> confirmations = [];
    private IArchiveRestoreStagedPackage? package;
    private OperationId? operationId;
    private Guid? preferredArchiveId;

    public ArchiveTransferSnapshot Snapshot { get; private set; } = new();

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            IReadOnlyList<PlayerArchive> archives =
                await source.GetArchivesAsync(cancellationToken);
            Guid? selected = preferredArchiveId is Guid preferred &&
                archives.Any(archive => archive.Id == preferred)
                ? preferred
                : archives.FirstOrDefault()?.Id;
            preferredArchiveId = selected;
            Set(Snapshot with
            {
                Archives = archives,
                SelectedArchiveId = selected,
                Status = "可以导出或选择 .fca 文件进行预览。",
                Error = null
            });
            await RefreshRecoveryPointAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Set(Snapshot with
            {
                Error = Describe(exception),
                Status = "读取备份与恢复信息失败；本地数据未改变。"
            });
        }
    }

    public Task SelectArchiveAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        Set(Snapshot with { SelectedArchiveId = archiveId });
        return Task.CompletedTask;
    }

    public void SetExportAll(bool all) =>
        Set(Snapshot with { ExportAllArchives = all });

    public async Task<ArchiveTransferActionResult> ExportAsync(
        string destinationPath,
        ArchiveGeneratorInfo generator,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return new(false, "未选择保存位置。");
        }

        ArchiveExportScope scope;
        try
        {
            scope = Snapshot.ExportAllArchives
                ? ArchiveExportScope.All()
                : ArchiveExportScope.Selected([RequireSelectedArchive().Id]);
        }
        catch (Exception exception)
        {
            return Fail(exception.Message);
        }

        string temporaryPath = Path.Combine(
            workspace.TemporaryDirectory,
            $"furina-archive-{Guid.NewGuid():N}.fca");
        try
        {
            Directory.CreateDirectory(workspace.TemporaryDirectory);
            ArchiveWriteResult result;
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                result = await source.ExportAsync(
                    destination,
                    scope,
                    generator,
                    cancellationToken);
            }

            await workspace.PublishAsync(temporaryPath, destinationPath, cancellationToken);
            ArchivePackageSummary summary = result.Summary;
            string message =
                $"已导出完整逻辑档案（可合并/替换恢复，不是 SQLite 原始拷贝）：" +
                $"{summary.Catalog.Archives.Count} 个档案、" +
                $"{summary.Catalog.AccountCount} 个账号、" +
                $"{summary.GachaCurrentFactCount + summary.DomainFactCount} 条当前事实、" +
                $"{summary.GachaRevisionCount + summary.DomainRevisionCount} 条修订。";
            Set(Snapshot with { Status = message, Error = null });
            return new(true, message);
        }
        catch (OperationCanceledException)
        {
            return Fail("已取消导出，未写出目标文件。");
        }
        catch (Exception exception)
        {
            return Fail(Describe(exception));
        }
        finally
        {
            workspace.DeleteTemporary(temporaryPath);
        }
    }

    public async Task<ArchiveTransferActionResult> OpenPackageAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        await ReleasePackageAsync();
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            package = await source.StageAsync(stream, cancellationToken);
            operationId = OperationId.New();
            archiveDecisions.Clear();
            accountDecisions.Clear();
            confirmations.Clear();
            Set(Snapshot with
            {
                PackageSummary = DescribePackage(package),
                PlanSummary = "选择文件后已校验；正在生成 Merge 预览。",
                MappingSummary = "尚无档案或账号映射。",
                DiagnosticSummary = "尚无冲突或待确认项。",
                ApplyPortableSettings = false,
                Plan = null,
                HasPackage = true,
                HasPlan = false,
                CanApply = false,
                Status = "文件已完成校验；请检查恢复预览。",
                Error = null
            });
            ArchiveTransferActionResult plan = await PlanAsync(cancellationToken);
            return new(
                true,
                plan.Succeeded
                    ? "文件已校验并生成预览。"
                    : "文件已校验；预览仍有未解决的映射、冲突或确认项，数据库未改变。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            await ReleasePackageAsync();
            return Fail(Describe(exception));
        }
    }

    public async Task<ArchiveTransferActionResult> PlanAsync(
        CancellationToken cancellationToken = default)
    {
        if (package is null) return Fail("请先选择 .fca 文件。");
        try
        {
            ArchiveRestorePlan plan = await ReplanAsync(cancellationToken);
            return new(
                plan.CanApply,
                plan.CanApply
                    ? "Merge 预览已满足全部前置条件，可以确认提交。"
                    : "Merge 预览仍有未解决的映射、冲突或确认项；数据库未改变。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(Describe(exception));
        }
    }

    public async Task<ArchiveTransferActionResult> ResolveArchiveAsync(
        Guid sourceArchiveReference,
        Guid? targetArchiveId,
        string? newArchiveName,
        CancellationToken cancellationToken = default)
    {
        archiveDecisions.RemoveAll(item =>
            item.SourceArchiveReference == sourceArchiveReference);
        archiveDecisions.Add(targetArchiveId is Guid target
            ? new ArchiveRestoreArchiveDecision(
                sourceArchiveReference,
                ArchiveRestoreArchiveDecisionKind.MapExisting,
                target)
            : new ArchiveRestoreArchiveDecision(
                sourceArchiveReference,
                ArchiveRestoreArchiveDecisionKind.CreateNew,
                NewArchiveName: newArchiveName));
        return await ReplanActionAsync(cancellationToken);
    }

    public async Task<ArchiveTransferActionResult> ResolveAccountAsync(
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
        return await ReplanActionAsync(cancellationToken);
    }

    public async Task<ArchiveTransferActionResult> SetApplyPortableSettingsAsync(
        bool apply,
        CancellationToken cancellationToken = default)
    {
        Set(Snapshot with { ApplyPortableSettings = apply });
        return package is null
            ? new(false, "请先选择 .fca 文件。")
            : await ReplanActionAsync(cancellationToken);
    }

    public async Task<ArchiveTransferActionResult> AcceptPendingConfirmationsAsync(
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.Plan is not ArchiveRestorePlan plan) return Fail("尚未生成恢复预览。");
        foreach (ArchiveRestoreConfirmationRequirement requirement in
            plan.Confirmations.Where(item => !item.Satisfied))
        {
            confirmations.RemoveAll(item =>
                item.Kind == requirement.Kind && item.Reference == requirement.Reference);
            confirmations.Add(new ArchiveRestoreConfirmation(
                requirement.Kind,
                requirement.Reference,
                requirement.MarkerKey,
                requirement.MarkerVersion));
        }

        return await ReplanActionAsync(cancellationToken);
    }

    public async Task<ArchiveTransferApplyOutcome> ApplyAsync(
        Guid? cleanupConfirmationId = null,
        CancellationToken cancellationToken = default)
    {
        if (package is null || Snapshot.Plan is not ArchiveRestorePlan plan ||
            operationId is not OperationId id)
        {
            return ApplyFail("请先选择文件并生成恢复预览。");
        }

        if (plan.PackageFingerprint != package.PackageFingerprint)
        {
            return ApplyFail("预览对应的文件已变化；请重新选择文件并生成预览。");
        }

        if (!plan.CanApply)
        {
            return ApplyFail("预览仍存在未解决的映射、冲突或确认项，数据库未改变。");
        }

        Set(Snapshot with { IsBusy = true, Error = null, Status = "正在校验目标状态…" });
        try
        {
            ArchiveRestorePlanFreshness freshness =
                await source.CheckFreshnessAsync(plan, cancellationToken);
            if (!freshness.IsCurrent)
            {
                return ApplyFail(
                    "预览已过期：目标状态或 Schema 在预览后发生变化；请重新生成预览，" +
                    "旧预览不会写入任何数据。");
            }

            DateTimeOffset startedAt = timeProvider.GetUtcNow();
            ArchiveRestoreApplyResult result;
            try
            {
                result = await source.ApplyAsync(
                    new ArchiveRestoreApplyRequest(
                        package,
                        plan,
                        id,
                        startedAt,
                        timeProvider.GetUtcNow(),
                        "Restore Furina Archive with Merge",
                        cleanupConfirmationId),
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                ArchiveRestoreReceipt? receipt = await source.FindReceiptAsync(
                    id,
                    CancellationToken.None);
                if (receipt is not null)
                {
                    result = new ArchiveRestoreApplyResult(
                        ChangeExecutionStatus.AlreadyCommitted,
                        receipt);
                }
                else
                {
                    throw;
                }
            }

            string message = DescribeApplyResult(result);
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.NoOp or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                await ReleasePackageAsync();
                await EnterAsync(cancellationToken);
                Set(Snapshot with { Status = message, Error = null });
                return new(true, message, result);
            }

            Set(Snapshot with { IsBusy = false, Status = "恢复未提交；数据库保持提交前状态。", Error = message });
            return new(false, message, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            string message = Describe(exception);
            Set(Snapshot with
            {
                IsBusy = false,
                Status = "恢复结果未知；请使用同一预览重试或重新检查数据。",
                Error = message
            });
            return ApplyFail(message);
        }
    }

    public async Task ReleaseAsync()
    {
        await ReleasePackageAsync();
        Set(Snapshot with
        {
            Status = "当前预览会话已释放；数据库未改变。",
            Error = null
        });
    }

    public Task<IReadOnlyList<GameAccount>> GetTargetAccountsAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default) =>
        source.GetAccountsAsync(archiveId, cancellationToken);

    public async Task RefreshRecoveryPointAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArchiveReplaceRecoveryPoint? point =
                await source.FindCurrentRecoveryPointAsync(cancellationToken);
            Set(Snapshot with
            {
                RecoveryPointSummary = point is null
                    ? "当前没有设备本地 Replace 恢复点。"
                    : $"当前恢复点：{point.CreatedAt:yyyy-MM-dd HH:mm:ss zzz}；" +
                        $"{point.Length} 字节；Schema {point.DatabaseSchemaVersion}；" +
                        $"校验 {(point.IsValid ? "有效" : "无效")}。" +
                        " 本页只显示状态，不提供主库热回切。"
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
                RecoveryPointSummary = "无法读取恢复点状态；本页不提供切换操作。",
                Error = Describe(exception)
            });
        }
    }

    private async Task<ArchiveTransferActionResult> ReplanActionAsync(
        CancellationToken cancellationToken)
    {
        if (package is null) return Fail("请先选择 .fca 文件。");
        try
        {
            ArchiveRestorePlan plan = await ReplanAsync(cancellationToken);
            return new(
                plan.CanApply,
                plan.CanApply
                    ? "预览已满足全部前置条件，可以确认提交。"
                    : "预览仍有未解决的映射、冲突或确认项；数据库未改变。");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return Fail(Describe(exception));
        }
    }

    private async Task<ArchiveRestorePlan> ReplanAsync(CancellationToken cancellationToken)
    {
        if (package is null) throw new InvalidOperationException("尚未选择 .fca 文件。");
        ArchiveRestorePlan plan = await source.PlanAsync(
            new ArchiveRestorePlanRequest(
                package,
                archiveDecisions,
                accountDecisions,
                confirmations,
                Snapshot.ApplyPortableSettings,
                ArchiveRestoreMode.Merge,
                ReplaceSelections: null),
            cancellationToken);
        UpdatePlan(plan);
        return plan;
    }

    private void UpdatePlan(ArchiveRestorePlan plan)
    {
        string settings = plan.Settings.Differences.Count == 0
            ? "设置：无差异。"
            : "设置差异：" + string.Join(
                "；",
                plan.Settings.Differences.Select(item =>
                    $"{item.Setting} {item.SourceValue ?? "无"} → {item.TargetValue ?? "无"}"));
        string planSummary =
            $"模式：Merge（补充/合并，不是原始 SQLite 拷贝）；" +
            $"档案 新建 {plan.Preview.ArchiveCreateCount} / 复用 {plan.Preview.ArchiveReuseCount}；" +
            $"账号 新建 {plan.Preview.AccountCreateCount} / 复用 {plan.Preview.AccountReuseCount}；" +
            $"冲突 {plan.Preview.ConflictCount}；Tombstone {plan.Preview.TombstoneCount}。" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                plan.Preview.Datasets.Select(item =>
                    $"{FactKindName(item.Kind)}：新增 {item.AddCount}，保留 {item.SkipCount}，" +
                    $"冲突 {item.ConflictCount}，重引入 {item.TombstoneCount}。")) +
            Environment.NewLine + settings +
            Environment.NewLine +
            "Merge 边界：来源缺失的 Dataset 不会清空目标；Tombstone 独立确认；" +
            "提交失败不改变原数据；本页不提供热回切。";
        string mapping = string.Join(
            Environment.NewLine,
            plan.Archives.Select(item =>
                $"档案“{item.SourceName}” → {ArchiveMappingLabel(item)}")
                .Concat(plan.Accounts.Select(item =>
                    $"账号 {SourceAccountLabel(item.SourceAccountReference)} → {AccountMappingLabel(item)}")));
        List<string> diagnostics = [];
        diagnostics.AddRange(plan.Conflicts.Select(item =>
            $"[冲突/{ConflictLabel(item.Code)}] {item.Message}"));
        diagnostics.AddRange(plan.Confirmations.Where(item => !item.Satisfied)
            .Select(item => $"[待确认/{ConfirmationLabel(item.Kind)}] {item.Message}"));
        diagnostics.AddRange(plan.Undo.IneligibleReasons.Select(item =>
            $"[不可撤销] {item}"));
        if (plan.Tombstones.Count > 0)
        {
            diagnostics.Add(
                $"[Tombstone] 命中 {plan.Tombstones.Count} 项；确认只作用于当前 marker/version，" +
                "目标变化后必须重新预览。");
        }

        string diagnosis = diagnostics.Count == 0
            ? "没有阻塞冲突或待确认项。"
            : string.Join(Environment.NewLine, diagnostics);
        Set(Snapshot with
        {
            Plan = plan,
            HasPlan = true,
            CanApply = plan.CanApply,
            PlanSummary = planSummary,
            MappingSummary = string.IsNullOrWhiteSpace(mapping)
                ? "所选范围没有父对象映射。"
                : mapping,
            DiagnosticSummary = diagnosis,
            Status = plan.CanApply
                ? "预览已满足全部前置条件，可以确认提交。"
                : "预览尚有待处理的映射、冲突或确认项；数据库未改变。",
            Error = null
        });
    }

    private async Task ReleasePackageAsync()
    {
        IArchiveRestoreStagedPackage? released = package;
        package = null;
        operationId = null;
        archiveDecisions.Clear();
        accountDecisions.Clear();
        confirmations.Clear();
        if (released is not null)
        {
            try { await released.DisposeAsync(); } catch (Exception) { }
        }

        Set(Snapshot with
        {
            HasPackage = false,
            HasPlan = false,
            CanApply = false,
            Plan = null,
            PackageSummary = "尚未选择逻辑档案文件。",
            PlanSummary = "选择文件并生成预览后显示。",
            MappingSummary = "尚无档案或账号映射。",
            DiagnosticSummary = "尚无冲突或待确认项。"
        });
    }

    private string DescribePackage(IArchiveRestoreStagedPackage staged)
    {
        ArchivePackageSummary summary = staged.Summary;
        return $"Archive v{summary.Manifest.FormatVersion}；" +
            $"来源 {summary.Manifest.Generator.App} {summary.Manifest.Generator.AppVersion}" +
            $" ({summary.Manifest.Generator.SourcePlatform})；" +
            $"{summary.Catalog.Archives.Count} 个档案、" +
            $"{summary.Catalog.AccountCount} 个账号；" +
            $"{summary.GachaCurrentFactCount + summary.DomainFactCount} 条当前事实、" +
            $"{summary.GachaRevisionCount + summary.DomainRevisionCount} 条修订。";
    }

    private string ArchiveMappingLabel(ArchiveRestoreArchiveMapping mapping) =>
        mapping.Kind switch
        {
            ArchiveRestoreArchiveMappingKind.MapExisting =>
                $"现有档案“{mapping.ProposedName}”",
            ArchiveRestoreArchiveMappingKind.CreateNew =>
                $"新建档案“{mapping.ProposedName}”（唯一名）",
            _ => "需要手动选择"
        };

    private string AccountMappingLabel(ArchiveRestoreAccountMapping mapping) =>
        mapping.Kind switch
        {
            ArchiveRestoreAccountMappingKind.MapExisting =>
                $"现有账号 {mapping.TargetAccountId:D}",
            ArchiveRestoreAccountMappingKind.CreateNew =>
                $"新建账号 {mapping.ProposedAccountId:D}",
            ArchiveRestoreAccountMappingKind.Collision => "身份冲突",
            _ => "需要手动选择"
        };

    private string SourceAccountLabel(Guid sourceAccountReference)
    {
        ArchiveCatalogAccount? account = package?.Catalog.Archives
            .SelectMany(item => item.Accounts)
            .FirstOrDefault(item => item.AccountReference == sourceAccountReference);
        return account is null
            ? sourceAccountReference.ToString("D")
            : string.IsNullOrWhiteSpace(account.DisplayName)
                ? account.LegacyUid
                : $"{account.DisplayName} ({account.LegacyUid})";
    }

    private static string FactKindName(ArchiveRestoreFactKind kind) => kind switch
    {
        ArchiveRestoreFactKind.GachaCurrent => "抽卡当前事实",
        ArchiveRestoreFactKind.GachaRevision => "抽卡修订",
        ArchiveRestoreFactKind.ItemObservation => "角色/武器观察",
        ArchiveRestoreFactKind.ManualAcquisition => "手工获取",
        ArchiveRestoreFactKind.ManualAcquisitionRevision => "手工获取修订",
        ArchiveRestoreFactKind.ChallengeObservation => "挑战观察",
        _ => kind.ToString()
    };

    private static string ConflictLabel(ArchiveRestoreConflictCode code) => code switch
    {
        ArchiveRestoreConflictCode.ArchiveMapping => "档案映射",
        ArchiveRestoreConflictCode.AccountMapping => "账号映射",
        ArchiveRestoreConflictCode.IdentityCollision => "身份冲突",
        ArchiveRestoreConflictCode.DuplicateSourceIdentity => "来源身份重复",
        ArchiveRestoreConflictCode.CurrentFactDifference => "当前事实差异",
        ArchiveRestoreConflictCode.RevisionDifference => "修订差异",
        ArchiveRestoreConflictCode.SettingsConflict => "设置冲突",
        ArchiveRestoreConflictCode.UnsupportedPartialRestore => "不支持的部分恢复",
        _ => code.ToString()
    };

    private static string ConfirmationLabel(ArchiveRestoreConfirmationKind kind) => kind switch
    {
        ArchiveRestoreConfirmationKind.PartialOptionalDatasets => "可选数据集不完整",
        ArchiveRestoreConfirmationKind.SimilarArchiveName => "相似档案名",
        ArchiveRestoreConfirmationKind.UnresolvedAccountMapping => "待确认账号映射",
        ArchiveRestoreConfirmationKind.TombstoneReintroduction => "Tombstone 重引入",
        ArchiveRestoreConfirmationKind.ApplyPortableSettings => "应用包设置",
        ArchiveRestoreConfirmationKind.NonUndoableOperation => "不可撤销操作",
        ArchiveRestoreConfirmationKind.DestructiveReplace => "破坏性 Replace",
        _ => kind.ToString()
    };

    private static string DescribeApplyResult(ArchiveRestoreApplyResult result) =>
        result.Status switch
        {
            ChangeExecutionStatus.Applied =>
                $"恢复已提交：影响 {result.Receipt?.AffectedRecordCount ?? 0} 项；" +
                $"Receipt {result.Receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.NoOp =>
                $"恢复为幂等 NoOp；Receipt {result.Receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.AlreadyCommitted =>
                $"该操作此前已经提交，已按持久化 Receipt 恢复结果：" +
                $"{result.Receipt?.ReceiptId:D}。",
            ChangeExecutionStatus.NeedsConfirmation =>
                "空间不足，需要确认清理较早撤销材料后重试；当前恢复未提交。",
            ChangeExecutionStatus.Conflict =>
                "恢复未提交：目标状态或预览已变化，请重新生成预览。",
            ChangeExecutionStatus.Suppressed =>
                "恢复未提交：命中永久删除抑制，数据未改变。",
            _ => "恢复未提交，数据库保持提交前状态。"
        };

    private static string Describe(Exception exception) => exception switch
    {
        ArchivePackageException packageException => packageException.Code switch
        {
            ArchivePackageErrorCode.UnsupportedVersion =>
                "包版本不受支持；未写入任何数据。",
            ArchivePackageErrorCode.HashMismatch =>
                "包校验和/哈希不匹配，文件可能被截断或损坏；未写入任何数据。",
            ArchivePackageErrorCode.MissingEntry or ArchivePackageErrorCode.LengthMismatch =>
                "包缺少必要内容或长度不匹配；未写入任何数据。",
            ArchivePackageErrorCode.InvalidPackage =>
                "所选文件不是有效的完整逻辑档案；未写入任何数据。",
            ArchivePackageErrorCode.ResourceLimitExceeded =>
                "包超出当前资源限制；未写入任何数据。",
            ArchivePackageErrorCode.InvalidPath or ArchivePackageErrorCode.InvalidReference =>
                "包内引用或路径无效；未写入任何数据。",
            ArchivePackageErrorCode.UnknownRequiredDataset =>
                "包声明了当前版本不支持的必需数据集；未写入任何数据。",
            _ => "读取逻辑档案失败；未写入任何数据。"
        },
        _ => ManagementTexts.DescribeOperationFailure(exception)
    };

    private PlayerArchive RequireSelectedArchive() =>
        Snapshot.SelectedArchive ??
        throw new InvalidOperationException("请先选择要导出的档案。");

    private ArchiveTransferActionResult Fail(string message)
    {
        Set(Snapshot with { Error = message, Status = "操作未完成；本地数据未改变。" });
        return new(false, message);
    }

    private ArchiveTransferApplyOutcome ApplyFail(string message)
    {
        Set(Snapshot with
        {
            IsBusy = false,
            Error = message,
            Status = "恢复未提交；数据库保持提交前状态。"
        });
        return new(false, message, null);
    }

    private void Set(ArchiveTransferSnapshot snapshot) => Snapshot = snapshot;
}

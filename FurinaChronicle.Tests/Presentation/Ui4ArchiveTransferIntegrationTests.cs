// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Challenges;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Inventory;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Archives.Portable;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Archives.Portable;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Writing;
using FurinaChronicle.Services.HistoryDomains;

namespace FurinaChronicle.Tests.Presentation;

// Real SQLite coverage for UI-4 WP-A3a: export a complete logical .fca, open/validate it, plan a
// Merge, apply atomically, then export/query again. The source/target are separate isolated
// environments; no production database is involved.
public sealed class Ui4ArchiveTransferIntegrationTests
{
    [Fact]
    public async Task ExportOpenPlanMergeAndReexport()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            string sourceDirectory = Path.Combine(root, "source");
            await using var sourceEnvironment = new Ui1PreviewEnvironment(sourceDirectory, "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");

            ArchiveTransferActionResult export = await sourceState.ExportAsync(
                exportPath, Generator());
            Assert.True(export.Succeeded, export.Message);
            Assert.True(File.Exists(exportPath));
            Assert.Contains("3 个档案", export.Message);
            Assert.Contains("71 条当前事实", export.Message);

            string targetDirectory = Path.Combine(root, "target");
            await using var targetEnvironment = new Ui1PreviewEnvironment(targetDirectory, "WinUI");
            await targetEnvironment.InitializeAsync();
            var delete = new DeletePlayerArchive(targetEnvironment.Changes);
            foreach (PlayerArchive archive in
                await targetEnvironment.Archives.GetAllAsync(default))
            {
                await delete.ExecuteAsync(archive.Id);
            }

            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.Empty(targetState.Snapshot.Archives);
            ArchiveTransferActionResult open = await targetState.OpenPackageAsync(exportPath);
            Assert.True(open.Succeeded, open.Message);
            Assert.NotNull(targetState.Snapshot.Plan);
            Assert.Contains("Archive v1.0", targetState.Snapshot.PackageSummary);
            Assert.Contains("Merge", targetState.Snapshot.PlanSummary);
            Assert.All(
                targetState.Snapshot.Plan!.Archives,
                mapping => Assert.Equal(
                    ArchiveRestoreArchiveMappingKind.CreateNew,
                    mapping.Kind));
            Assert.All(
                targetState.Snapshot.Plan.Archives,
                mapping => Assert.NotEmpty(mapping.ProposedName));
            Assert.True(await MakePlanReadyAsync(targetState), targetState.Snapshot.DiagnosticSummary);

            ArchiveTransferApplyOutcome apply = await targetState.ApplyAsync();
            Assert.True(apply.Succeeded, targetState.Snapshot.DiagnosticSummary);
            Assert.NotNull(apply.Receipt);
            Assert.Equal(3, (await targetEnvironment.Archives.GetAllAsync(default)).Count);
            Assert.Equal(4, await CountAllAccountsAsync(targetEnvironment));
            Assert.Equal(71, await CountAllRecordsAsync(targetEnvironment));

            string reexportPath = Path.Combine(root, "reexport.fca");
            ArchiveTransferActionResult reexport = await targetState.ExportAsync(
                reexportPath, Generator());
            Assert.True(reexport.Succeeded, reexport.Message);
            await using var stream = new FileStream(
                reexportPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            ArchivePackageSummary summary =
                await new ArchivePackageReader().ReadAndValidateAsync(stream);
            Assert.Equal(3, summary.Catalog.Archives.Count);
            Assert.Equal(4, summary.Catalog.AccountCount);
            Assert.Equal(71, summary.GachaCurrentFactCount);
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task SelectedArchiveExportContainsOnlyThatArchive()
    {
        string root = TempDirectory();
        ArchiveTransferState? state = null;
        try
        {
            await using var environment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await environment.InitializeAsync();
            state = CreateState(environment, root);
            await state.EnterAsync();
            PlayerArchive selected = Assert.Single(
                await environment.Archives.GetAllAsync(default),
                archive => archive.Name == "02 同 UID 独立副本");
            state.SetExportAll(false);
            await state.SelectArchiveAsync(selected.Id);
            string path = Path.Combine(root, "selected.fca");

            ArchiveTransferActionResult export = await state.ExportAsync(path, Generator());

            Assert.True(export.Succeeded, export.Message);
            Assert.Contains("1 个档案", export.Message);
            string temporary = Path.Combine(root, "temporary");
            if (Directory.Exists(temporary))
            {
                Assert.Empty(Directory.EnumerateFiles(temporary, "*.fca"));
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            ArchivePackageSummary summary =
                await new ArchivePackageReader().ReadAndValidateAsync(stream);
            Assert.Single(summary.Catalog.Archives);
            Assert.Equal(1, summary.Catalog.AccountCount);
            Assert.Equal(3, summary.GachaCurrentFactCount);
        }
        finally
        {
            if (state is not null) await state.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task StalePlanIsRejectedAndDoesNotApply()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            var delete = new DeletePlayerArchive(targetEnvironment.Changes);
            foreach (PlayerArchive archive in
                await targetEnvironment.Archives.GetAllAsync(default))
            {
                await delete.ExecuteAsync(archive.Id);
            }

            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.True((await targetState.OpenPackageAsync(exportPath)).Succeeded);
            Assert.True(await MakePlanReadyAsync(targetState), targetState.Snapshot.DiagnosticSummary);
            int archiveCountBefore = (await targetEnvironment.Archives.GetAllAsync(default)).Count;

            // Change the target after planning; the plan freshness token is now stale.
            await new CreatePlayerArchive(targetEnvironment.Archives)
                .ExecuteAsync("UI-4 plan staleness probe", default);

            ArchiveTransferApplyOutcome apply = await targetState.ApplyAsync();
            Assert.False(apply.Succeeded);
            Assert.Contains("预览已过期", apply.Message);
            Assert.Equal(
                archiveCountBefore + 1,
                (await targetEnvironment.Archives.GetAllAsync(default)).Count);
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task InvalidPackageDoesNotWriteTargetDatabase()
    {
        string root = TempDirectory();
        ArchiveTransferState? state = null;
        try
        {
            string invalid = Path.Combine(root, "invalid.fca");
            await File.WriteAllTextAsync(invalid, "this is not a logical archive");
            await using var environment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await environment.InitializeAsync();
            int archiveCount = (await environment.Archives.GetAllAsync(default)).Count;
            state = CreateState(environment, root);
            await state.EnterAsync();

            ArchiveTransferActionResult result = await state.OpenPackageAsync(invalid);

            Assert.False(result.Succeeded);
            Assert.NotNull(state.Snapshot.Error);
            Assert.Contains("有效", result.Message);
            Assert.Equal(
                archiveCount,
                (await environment.Archives.GetAllAsync(default)).Count);
        }
        finally
        {
            if (state is not null) await state.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    private static async Task DeleteDirectoryAsync(string path)
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(100);
            }
        }
    }

    [Fact]
    public async Task ConflictingTargetFactsBlockMergeWithoutWriting()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            PlayerArchive archive = Assert.Single(
                await targetEnvironment.Archives.GetAllAsync(default),
                item => item.Name == "01 合成档案");
            GameAccount account = Assert.Single(
                await targetEnvironment.Accounts.GetByArchiveIdAsync(archive.Id),
                item => item.Uid == "100000001");
            GachaRecord record = Assert.Single(
                (await new GetGachaRecordPage(targetEnvironment.Records).ExecuteAsync(
                    new GachaRecordQuery([account.Id], Limit: 1),
                    1,
                    1)).Records);
            await new ManageGachaHistory(targetEnvironment.Changes).CorrectAsync(
                OperationId.New(),
                new GachaFactReference(record.GameAccountId, record.ExternalRecordId),
                new GachaCorrectionInput("UI-4 conflict probe", record.ItemId, 5, record.Time));

            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.True((await targetState.OpenPackageAsync(exportPath)).Succeeded);
            Assert.NotNull(targetState.Snapshot.Plan);
            Assert.NotEmpty(targetState.Snapshot.Plan.Conflicts);

            ArchiveTransferApplyOutcome apply = await targetState.ApplyAsync();
            Assert.False(apply.Succeeded);
            Assert.Contains("数据库保持提交前状态", targetState.Snapshot.Status);
            GachaRecord after = Assert.Single(
                await targetEnvironment.Records.GetByExternalRecordIdsAsync(
                    account.Id,
                    [record.ExternalRecordId]));
            Assert.Equal("UI-4 conflict probe", after.ItemName);
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task ReplaceScopesRejectOverlapAndReplaceRemovesTargetOnlyFacts()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            PlayerArchive targetArchive = Assert.Single(
                await targetEnvironment.Archives.GetAllAsync(default),
                item => item.Name == "01 合成档案");
            GameAccount targetAccount = Assert.Single(
                await targetEnvironment.Accounts.GetByArchiveIdAsync(targetArchive.Id),
                item => item.Uid == "100000001");
            DateTimeOffset now = DateTimeOffset.Now;
            var commit = new CommitGachaRecords(targetEnvironment.Changes);
            CommitGachaRecordsResult seeded = await commit.ExecuteAsync(
                new CommitGachaRecordsRequest(
                    OperationId.New(),
                    DataChangeOperationKind.Import,
                    DataOrigin.UserEntered,
                    "target-only fact",
                    now,
                    now.AddSeconds(1),
                    [
                        new GachaRecord(
                            targetAccount.Id,
                            "ui4-target-only",
                            "目标独有记录",
                            5,
                            now)
                        {
                            ItemId = "ui4-target-only"
                        }
                    ],
                    GachaRecordConflictPolicy.PreserveExisting,
                    SuppressTombstonesAndContinue: false));
            Assert.Equal(ChangeExecutionStatus.Applied, seeded.Status);

            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.True((await targetState.OpenPackageAsync(exportPath)).Succeeded);
            await targetState.SetModeAsync(ArchiveRestoreMode.Replace);
            ArchiveCatalogArchive sourceArchive = targetState.Snapshot.PackageArchives
                .Single(item => item.Name == "01 合成档案");
            ArchiveCatalogAccount sourceAccount = sourceArchive.Accounts
                .Single(item => item.LegacyUid == targetAccount.Uid);
            ArchiveTransferActionResult accountScope =
                await targetState.AddReplaceSelectionAsync(
                    new ArchiveReplaceSelection(
                        sourceArchive.ArchiveReference,
                        ArchiveReplaceScopeKind.Account,
                        sourceAccount.AccountReference));
            Assert.True(targetState.Snapshot.Plan is not null);

            ArchiveTransferActionResult overlap =
                await targetState.AddReplaceSelectionAsync(
                    new ArchiveReplaceSelection(
                        sourceArchive.ArchiveReference,
                        ArchiveReplaceScopeKind.Archive));
            Assert.False(overlap.Succeeded);
            Assert.Contains("重叠", overlap.Message);
            Assert.Contains("未写入", overlap.Message);

            ArchiveTransferActionResult mergeMode =
                await targetState.SetModeAsync(ArchiveRestoreMode.Merge);
            Assert.True(mergeMode.Succeeded || targetState.Snapshot.Plan is not null);
            Assert.Empty(targetState.Snapshot.ReplaceSelections);
            Assert.Equal(ArchiveRestoreMode.Merge, targetState.Snapshot.Mode);
            Assert.Equal(ArchiveRestoreMode.Merge, targetState.Snapshot.Plan?.Mode);

            await targetState.SetModeAsync(ArchiveRestoreMode.Replace);
            await targetState.AddReplaceSelectionAsync(
                new ArchiveReplaceSelection(
                    sourceArchive.ArchiveReference,
                    ArchiveReplaceScopeKind.Account,
                    sourceAccount.AccountReference));
            Assert.True(await MakePlanReadyAsync(targetState));
            ArchiveTransferApplyOutcome apply = await targetState.ApplyAsync();
            Assert.True(apply.Succeeded, targetState.Snapshot.DiagnosticSummary);
            Assert.Equal(ArchiveRestoreMode.Replace, apply.Receipt?.Mode);

            IReadOnlyList<GachaRecord> targetOnly =
                await targetEnvironment.Records.GetByExternalRecordIdsAsync(
                    targetAccount.Id,
                    ["ui4-target-only"]);
            Assert.Empty(targetOnly);
            Assert.Equal(61, await CountRecordsAsync(targetEnvironment, targetAccount.Id));
            _ = accountScope;
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task ReplaceMissingDatasetIsAConflictAndIsNotTreatedAsClear()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        IArchiveRestoreStagedPackage? staged = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            ServiceArchiveTransferSource source = CreateTransferSource(targetEnvironment, root);
            await using (var stream = new FileStream(
                exportPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                staged = await source.StageAsync(stream);
            }

            ArchiveCatalogArchive archive = staged.Catalog.Archives
                .Single(item => item.Name == "01 合成档案");
            ArchiveCatalogAccount account = archive.Accounts[0];
            var filtered = new FilteredPackage(staged);
            ArchiveRestorePlan plan = await source.PlanAsync(
                new ArchiveRestorePlanRequest(
                    filtered,
                    Mode: ArchiveRestoreMode.Replace,
                    ReplaceSelections:
                    [
                        new ArchiveReplaceSelection(
                            archive.ArchiveReference,
                            ArchiveReplaceScopeKind.Domain,
                            account.AccountReference,
                            ArchiveReplaceDomain.ChallengeObservation)
                    ]));
            Assert.Contains(
                plan.Conflicts,
                item => item.Code == ArchiveRestoreConflictCode.UnsupportedPartialRestore);
            Assert.False(plan.CanApply);
            Assert.Equal(
                3,
                (await targetEnvironment.Archives.GetAllAsync(default)).Count);
        }
        finally
        {
            // The wrapped package is the real staged package; dispose it before deleting staging.
            if (staged is not null) await staged.DisposeAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task TombstoneReintroductionRequiresIndependentConfirmation()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            PlayerArchive archive = Assert.Single(
                await targetEnvironment.Archives.GetAllAsync(default),
                item => item.Name == "01 合成档案");
            GameAccount account = Assert.Single(
                await targetEnvironment.Accounts.GetByArchiveIdAsync(archive.Id),
                item => item.Uid == "100000001");
            GachaRecord record = Assert.Single(
                (await new GetGachaRecordPage(targetEnvironment.Records).ExecuteAsync(
                    new GachaRecordQuery([account.Id], Limit: 1),
                    1,
                    1)).Records);
            var history = new ManageGachaHistory(targetEnvironment.Changes);
            GachaAtomicChangeResult deleted = await history.IrreversiblyDeleteAsync(
                OperationId.New(),
                new GachaFactReference(account.Id, record.ExternalRecordId));
            Assert.Equal(ChangeExecutionStatus.Applied, deleted.Status);

            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.True((await targetState.OpenPackageAsync(exportPath)).Succeeded);
            ArchiveRestorePlan plan = Assert.IsType<ArchiveRestorePlan>(
                targetState.Snapshot.Plan);
            Assert.NotEmpty(plan.Tombstones);
            Assert.Contains(
                plan.Confirmations,
                item => item.Kind == ArchiveRestoreConfirmationKind.TombstoneReintroduction &&
                    !item.Satisfied);
            Assert.False(plan.CanApply);

            ArchiveTransferApplyOutcome before =
                await targetState.ApplyAsync();
            Assert.False(before.Succeeded);
            Assert.Empty(await targetEnvironment.Records.GetByExternalRecordIdsAsync(
                account.Id,
                [record.ExternalRecordId]));

            Assert.True(await MakePlanReadyAsync(targetState));
            ArchiveTransferApplyOutcome after =
                await targetState.ApplyAsync();
            Assert.True(after.Succeeded, targetState.Snapshot.DiagnosticSummary);
            Assert.Single(await targetEnvironment.Records.GetByExternalRecordIdsAsync(
                account.Id,
                [record.ExternalRecordId]));
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task ReplacePlanExpiryRejectsStalePlanWithoutWriting()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        ArchiveTransferState? targetState = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            targetState = CreateState(targetEnvironment, root);
            await targetState.EnterAsync();
            Assert.True((await targetState.OpenPackageAsync(exportPath)).Succeeded);
            await targetState.SetModeAsync(ArchiveRestoreMode.Replace);
            ArchiveCatalogArchive archive = targetState.Snapshot.PackageArchives
                .First(item => item.Accounts.Count > 0);
            ArchiveCatalogAccount account = archive.Accounts[0];
            await targetState.AddReplaceSelectionAsync(
                new ArchiveReplaceSelection(
                    archive.ArchiveReference,
                    ArchiveReplaceScopeKind.Account,
                    account.AccountReference));
            Assert.True(await MakePlanReadyAsync(targetState));
            int before = (await targetEnvironment.Archives.GetAllAsync(default)).Count;

            await new CreatePlayerArchive(targetEnvironment.Archives)
                .ExecuteAsync("Replace plan expiry probe", default);

            ArchiveTransferApplyOutcome outcome = await targetState.ApplyAsync();
            Assert.False(outcome.Succeeded);
            Assert.Contains("预览已过期", outcome.Message);
            Assert.Equal(before + 1,
                (await targetEnvironment.Archives.GetAllAsync(default)).Count);
        }
        finally
        {
            if (targetState is not null) await targetState.ReleaseAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    [Fact]
    public async Task ReceiptIsIdempotentForRepeatedReplaceApply()
    {
        string root = TempDirectory();
        ArchiveTransferState? sourceState = null;
        IArchiveRestoreStagedPackage? staged = null;
        try
        {
            await using var sourceEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "source"), "WinUI");
            await sourceEnvironment.InitializeAsync();
            sourceState = CreateState(sourceEnvironment, root);
            await sourceState.EnterAsync();
            string exportPath = Path.Combine(root, "source.fca");
            Assert.True((await sourceState.ExportAsync(exportPath, Generator())).Succeeded);

            await using var targetEnvironment =
                new Ui1PreviewEnvironment(Path.Combine(root, "target"), "WinUI");
            await targetEnvironment.InitializeAsync();
            ServiceArchiveTransferSource source = CreateTransferSource(targetEnvironment, root);
            await using (var stream = new FileStream(
                exportPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                staged = await source.StageAsync(stream);
            }

            ArchiveCatalogArchive archive = staged.Catalog.Archives
                .Single(item => item.Name == "01 合成档案");
            ArchiveCatalogAccount account = archive.Accounts[0];
            PlayerArchive targetArchive = Assert.Single(
                await targetEnvironment.Archives.GetAllAsync(default),
                item => item.Name == archive.Name);
            GameAccount targetAccount = Assert.Single(
                await targetEnvironment.Accounts.GetByArchiveIdAsync(targetArchive.Id),
                item => item.Uid == account.LegacyUid);
            ArchiveReplaceSelection selection = new(
                archive.ArchiveReference,
                ArchiveReplaceScopeKind.Account,
                account.AccountReference);
            ArchiveRestorePlan firstPlan = await source.PlanAsync(
                new ArchiveRestorePlanRequest(
                    staged,
                    Mode: ArchiveRestoreMode.Replace,
                    ReplaceSelections: [selection]));
            ArchiveRestoreConfirmation[] confirmations = firstPlan.Confirmations
                .Select(item => new ArchiveRestoreConfirmation(
                    item.Kind,
                    item.Reference,
                    item.MarkerKey,
                    item.MarkerVersion))
                .ToArray();
            ArchiveRestorePlan readyPlan = await source.PlanAsync(
                new ArchiveRestorePlanRequest(
                    staged,
                    Confirmations: confirmations,
                    Mode: ArchiveRestoreMode.Replace,
                    ReplaceSelections: [selection]));
            Assert.True(readyPlan.CanApply);

            OperationId operationId = OperationId.New();
            DateTimeOffset now = DateTimeOffset.Now;
            var request = new ArchiveRestoreApplyRequest(
                staged,
                readyPlan,
                operationId,
                now,
                now,
                "idempotency probe");
            ArchiveRestoreApplyResult first = await source.ApplyAsync(request);
            Assert.Equal(ChangeExecutionStatus.Applied, first.Status);
            var firstReceipt = first.Receipt;
            int countAfterFirst = await CountRecordsAsync(
                targetEnvironment, targetAccount.Id);
            ArchiveRestoreApplyResult second = await source.ApplyAsync(request);
            Assert.True(second.Status is ChangeExecutionStatus.AlreadyCommitted or
                ChangeExecutionStatus.NoOp);
            Assert.Equal(firstReceipt?.ReceiptId, second.Receipt?.ReceiptId);
            Assert.Equal(
                countAfterFirst,
                await CountRecordsAsync(targetEnvironment, targetAccount.Id));
        }
        finally
        {
            if (staged is not null) await staged.DisposeAsync();
            if (sourceState is not null) await sourceState.ReleaseAsync();
            await DeleteDirectoryAsync(root);
        }
    }

    private static async Task<int> CountRecordsAsync(
        Ui1PreviewEnvironment environment,
        Guid accountId) =>
        await environment.Records.CountAsync(accountId);

    private static ServiceArchiveTransferSource CreateTransferSource(
        Ui1PreviewEnvironment environment,
        string root)
    {
        var limits = new ArchivePortableLimits();
        var reader = new ArchivePackageReader(limits);
        var writer = new ArchivePackageWriter(limits);
        var snapshots = new SqliteArchiveExportSnapshotFactory(
            environment.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(Path.Combine(root, "snapshots")));
        var stager = new SqliteArchiveRestoreInputStager(
            reader,
            new ArchiveRestoreStorageOptions(Path.Combine(root, "staging")),
            limits);
        var planner = new PlanArchiveRestore(
            new SqliteArchiveRestoreTargetSnapshotFactory(snapshots),
            TimeProvider.System);
        var workspace = new TestWorkspace(Path.Combine(root, "temporary"));
        var services = new ArchiveTransferServiceSet(
            new ArchiveLogicalExporter(snapshots, writer, reader),
            stager,
            planner,
            environment.Changes,
            workspace);
        return new ServiceArchiveTransferSource(
            new GetPlayerArchives(environment.Archives),
            environment.Accounts,
            services);
    }

    private static async Task<bool> MakePlanReadyAsync(ArchiveTransferState state)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            ArchiveRestorePlan? plan = state.Snapshot.Plan;
            if (plan is null) return false;
            if (plan.CanApply) return true;
            if (plan.Confirmations.Any(item => !item.Satisfied))
            {
                await state.AcceptPendingConfirmationsAsync();
                continue;
            }

            ArchiveRestoreArchiveMapping? unresolvedArchive = plan.Archives.FirstOrDefault(
                item => item.Kind == ArchiveRestoreArchiveMappingKind.RequiresSelection);
            if (unresolvedArchive is not null)
            {
                await state.ResolveArchiveAsync(
                    unresolvedArchive.SourceArchiveReference, null, null);
                continue;
            }

            ArchiveRestoreAccountMapping? unresolvedAccount = plan.Accounts.FirstOrDefault(
                item => item.Kind == ArchiveRestoreAccountMappingKind.RequiresSelection);
            if (unresolvedAccount is not null)
            {
                await state.ResolveAccountAsync(
                    unresolvedAccount.SourceAccountReference, null);
                continue;
            }

            return false;
        }

        return false;
    }

    private static async Task<int> CountAllAccountsAsync(Ui1PreviewEnvironment environment)
    {
        int total = 0;
        foreach (PlayerArchive archive in await environment.Archives.GetAllAsync(default))
        {
            total += (await environment.Accounts.GetByArchiveIdAsync(archive.Id)).Count;
        }

        return total;
    }

    private static async Task<int> CountAllRecordsAsync(Ui1PreviewEnvironment environment)
    {
        int total = 0;
        foreach (PlayerArchive archive in await environment.Archives.GetAllAsync(default))
        {
            foreach (GameAccount account in
                await environment.Accounts.GetByArchiveIdAsync(archive.Id))
            {
                total += await environment.Records.CountAsync(account.Id);
            }
        }

        return total;
    }

    private static ArchiveTransferState CreateState(
        Ui1PreviewEnvironment environment,
        string root)
    {
        var limits = new ArchivePortableLimits();
        var reader = new ArchivePackageReader(limits);
        var writer = new ArchivePackageWriter(limits);
        var snapshots = new SqliteArchiveExportSnapshotFactory(
            environment.Database,
            TimeProvider.System,
            new ArchiveSnapshotStorageOptions(Path.Combine(root, "snapshots")));
        var stager = new SqliteArchiveRestoreInputStager(
            reader,
            new ArchiveRestoreStorageOptions(Path.Combine(root, "staging")),
            limits);
        var planner = new PlanArchiveRestore(
            new SqliteArchiveRestoreTargetSnapshotFactory(snapshots),
            TimeProvider.System);
        var workspace = new TestWorkspace(Path.Combine(root, "temporary"));
        var services = new ArchiveTransferServiceSet(
            new ArchiveLogicalExporter(snapshots, writer, reader),
            stager,
            planner,
            environment.Changes,
            workspace);
        var source = new ServiceArchiveTransferSource(
            new GetPlayerArchives(environment.Archives),
            environment.Accounts,
            services);
        return new ArchiveTransferState(source, workspace, TimeProvider.System);
    }

    private static ArchiveGeneratorInfo Generator() =>
        new("FurinaChronicle", "0.10.0", "UI-4", "test", "WinUI");

    private static string TempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleUi4Transfer",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FilteredPackage(IArchiveRestoreStagedPackage inner)
        : IArchiveRestoreStagedPackage
    {
        public string PackageFingerprint => inner.PackageFingerprint;

        public ArchivePackageSummary Summary { get; } = inner.Summary with
        {
            Manifest = inner.Summary.Manifest with
            {
                Datasets = inner.Summary.Manifest.Datasets
                    .Where(item => !item.DatasetReference.Contains(
                        "challenge-history", StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            }
        };

        public ArchiveCatalog Catalog => inner.Catalog;
        public ArchivePortableSettings Settings => inner.Settings;
        public DateTimeOffset GeneratedAt => inner.GeneratedAt;
        public ArchiveExportScope Scope => inner.Scope;
        public long ContainerLength => inner.ContainerLength;
        public long StagingLength => inner.StagingLength;

        public IAsyncEnumerable<GachaRecord> ReadGachaRecordsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            inner.ReadGachaRecordsAsync(accountReference, cancellationToken);

        public IAsyncEnumerable<GachaRevisionItem> ReadGachaRevisionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            inner.ReadGachaRevisionsAsync(accountReference, cancellationToken);

        public IAsyncEnumerable<ItemStateObservation> ReadItemObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            inner.ReadItemObservationsAsync(accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionState> ReadManualAcquisitionsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            inner.ReadManualAcquisitionsAsync(accountReference, cancellationToken);

        public IAsyncEnumerable<ManualAcquisitionRevisionItem>
            ReadManualAcquisitionRevisionsAsync(
                Guid accountReference,
                CancellationToken cancellationToken = default) =>
            inner.ReadManualAcquisitionRevisionsAsync(accountReference, cancellationToken);

        public IAsyncEnumerable<ChallengeObservation> ReadChallengeObservationsAsync(
            Guid accountReference,
            CancellationToken cancellationToken = default) =>
            inner.ReadChallengeObservationsAsync(accountReference, cancellationToken);

        public Task<IReadOnlyDictionary<string, ArchiveIndexedFact<GachaRecord>>>
            FindGachaRecordsAsync(
                Guid accountReference,
                IReadOnlyCollection<string> externalRecordIds,
                CancellationToken cancellationToken = default) =>
            inner.FindGachaRecordsAsync(accountReference, externalRecordIds, cancellationToken);

        public Task<ArchiveIndexedFact<GachaRevisionItem>?> FindGachaRevisionAsync(
            Guid accountReference,
            Guid revisionId,
            CancellationToken cancellationToken = default) =>
            inner.FindGachaRevisionAsync(accountReference, revisionId, cancellationToken);

        public Task<ArchiveIndexedFact<ItemStateObservation>?> FindItemObservationAsync(
            Guid accountReference,
            ItemObservationId observationId,
            CancellationToken cancellationToken = default) =>
            inner.FindItemObservationAsync(accountReference, observationId, cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionState>?>
            FindManualAcquisitionAsync(
                Guid accountReference,
                ManualAcquisitionId acquisitionId,
                CancellationToken cancellationToken = default) =>
            inner.FindManualAcquisitionAsync(accountReference, acquisitionId, cancellationToken);

        public Task<ArchiveIndexedFact<ManualAcquisitionRevisionItem>?>
            FindManualAcquisitionRevisionAsync(
                Guid accountReference,
                Guid revisionId,
                CancellationToken cancellationToken = default) =>
            inner.FindManualAcquisitionRevisionAsync(
                accountReference, revisionId, cancellationToken);

        public Task<ArchiveIndexedFact<ChallengeObservation>?>
            FindChallengeObservationAsync(
                Guid accountReference,
                ChallengeObservationId observationId,
                CancellationToken cancellationToken = default) =>
            inner.FindChallengeObservationAsync(accountReference, observationId, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class TestWorkspace(string temporaryDirectory) : IArchiveTransferWorkspace
    {
        public string TemporaryDirectory { get; } = temporaryDirectory;

        public Task PublishAsync(
            string temporaryPath,
            string destinationPath,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(destinationPath) ?? TemporaryDirectory);
            File.Copy(temporaryPath, destinationPath, overwrite: true);
            File.Delete(temporaryPath);
            return Task.CompletedTask;
        }

        public void DeleteTemporary(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Core.Records;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Infrastructure.Gacha.Portable;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.History;

namespace FurinaChronicle.Tests.Infrastructure.Diagnostics;

// End-to-end checks of the real Portable export/import pipeline and record operations against an
// isolated SQLite database. No network, credentials or production paths are used.
public sealed class Ui1PortableHistoryIntegrationTests
{
    [Fact]
    public async Task PortableRoundTripRespectsConflictsReintroducesTombsAndReexportsConsistently()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive archive = await ArchiveAsync(environment, "01 合成档案");
            GameAccount accountA = await AccountAsync(environment, archive.Id, "100000001");
            GameAccount accountB = await AccountAsync(environment, archive.Id, "100000002");
            GameAccount accountC = await AccountAsync(environment, archive.Id, "100000003");
            string firstPackage = Path.Combine(environment.TemporaryDirectory, "roundtrip-1.zip");

            GachaPortableWriteResult exported = await environment.PortableExport.ExportAsync(
                firstPackage,
                archive.Id,
                [accountA.Id, accountB.Id, accountC.Id]);
            Assert.Equal(3, exported.AccountCount);
            Assert.Equal(68, exported.RecordCount);

            var recordOperations = new GachaRecordOperationCoordinator(
                environment.GachaHistory,
                environment.Changes);
            await ApplyCorrectionAsync(recordOperations, accountA.Id, "100001", "本地纠正物品");
            await ApplyOperationAsync(
                recordOperations,
                new GachaFactReference(accountA.Id, "100002"),
                GachaRecordOperationKind.Delete);
            await ApplyOperationAsync(
                recordOperations,
                new GachaFactReference(accountA.Id, "100003"),
                GachaRecordOperationKind.IrreversibleDelete);
            Assert.Equal(59, (await environment.Records.GetRecentAsync(accountA.Id, 100)).Count);

            GachaPortableImportCoordinator importer = CreateImporter(environment);
            GachaImportSource source = GachaImportSource.FromFile(firstPackage);

            GachaPortableImportPrepareResult addOnly = await importer.PrepareAsync(
                source,
                GachaPortableImportTarget.CurrentArchive(archive.Id),
                GachaImportMode.Incremental);
            Assert.True(addOnly.Succeeded, addOnly.FailureMessage);
            await using (GachaPortableImportSession session = addOnly.Session!)
            {
                Assert.False(session.Plan.CanApply);
                Assert.Equal(1, session.Plan.Preview.ConflictCount);
                GachaPortableApplyOutcome refused = await importer.ApplyAsync(session);
                Assert.Equal(GachaPortableApplyOutcomeKind.Conflict, refused.Kind);
                Assert.Contains("仅补充模式不会覆盖", refused.Message);
            }

            Assert.Equal(59, (await environment.Records.GetRecentAsync(accountA.Id, 100)).Count);

            GachaPortableImportPrepareResult full = await importer.PrepareAsync(
                source,
                GachaPortableImportTarget.CurrentArchive(archive.Id),
                GachaImportMode.Full);
            Assert.True(full.Succeeded, full.FailureMessage);
            await using (GachaPortableImportSession session = full.Session!)
            {
                Assert.True(session.Plan.CanApply);
                Assert.Contains("全量导入：覆盖同记录", session.ConfirmationText);

                GachaPortableApplyOutcome suppressed = await importer.ApplyAsync(session);

                Assert.Equal(GachaPortableApplyOutcomeKind.RequiresReintroduction, suppressed.Kind);
                TombstoneReintroductionWarning warning = Assert.Single(suppressed.ReintroductionWarnings!);
                Assert.Equal("100003", warning.Reference.ExternalRecordId);
                // A suppressed apply must not have committed a partial import.
                Assert.Equal(59, (await environment.Records.GetRecentAsync(accountA.Id, 100)).Count);

                TombstoneReintroductionConfirmation[] confirmations =
                [
                    new(warning.TombstoneKey, warning.TombstoneVersion, warning.Reference)
                ];
                GachaPortableApplyOutcome applied = await importer.ApplyAsync(
                    session,
                    confirmations,
                    cleanupConfirmationId: null);

                Assert.True(applied.IsSuccess, applied.Message);
                Assert.Equal(2, applied.Result!.InsertedRecordCount);
                Assert.Equal(1, applied.Result.UpdatedRecordCount);
            }

            IReadOnlyList<GachaRecord> restored = await environment.Records.GetRecentAsync(accountA.Id, 100);
            Assert.Equal(61, restored.Count);
            Assert.Equal(
                "合成物品 0-0-01",
                Assert.Single(restored, record => record.ExternalRecordId == "100001").ItemName);
            Assert.Contains(restored, record => record.ExternalRecordId == "100002");
            Assert.Contains(restored, record => record.ExternalRecordId == "100003");
            Assert.Empty(await environment.Changes.FindActiveTombstonesAsync(
                [new GachaFactReference(accountA.Id, "100003")]));

            string secondPackage = Path.Combine(environment.TemporaryDirectory, "roundtrip-2.zip");
            GachaPortableWriteResult reexported = await environment.PortableExport.ExportAsync(
                secondPackage,
                archive.Id,
                [accountA.Id, accountB.Id, accountC.Id]);
            Assert.Equal(exported.AccountCount, reexported.AccountCount);
            Assert.Equal(exported.RecordCount, reexported.RecordCount);

            GachaPortablePackage before = await ReadPackageAsync(firstPackage);
            GachaPortablePackage after = await ReadPackageAsync(secondPackage);
            Assert.Equal(
                SortAccounts(before).Select(account => account.RoleIdentity.NaturalIdentity),
                SortAccounts(after).Select(account => account.RoleIdentity.NaturalIdentity));
            foreach (GachaPortableAccount beforeAccount in SortAccounts(before))
            {
                GachaPortableAccount afterAccount = Assert.Single(
                    after.Accounts,
                    account => account.RoleIdentity.NaturalIdentity ==
                        beforeAccount.RoleIdentity.NaturalIdentity);
                Assert.Equal(
                    beforeAccount.Records.Select(FactFingerprint).OrderBy(value => value, StringComparer.Ordinal),
                    afterAccount.Records.Select(FactFingerprint).OrderBy(value => value, StringComparer.Ordinal));
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task PortableImportCanCreateANewArchiveFromTheSourceName()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive sourceArchive = await ArchiveAsync(environment, "01 合成档案");
            GameAccount[] accounts =
                [.. await environment.Accounts.GetByArchiveIdAsync(sourceArchive.Id, default)];
            string packagePath = Path.Combine(environment.TemporaryDirectory, "new-archive.zip");
            var exported = await environment.PortableExport.ExportAsync(
                packagePath,
                sourceArchive.Id,
                accounts.Select(account => account.Id).ToArray());
            Assert.Equal(68, exported.RecordCount);

            GachaPortableImportCoordinator importer = CreateImporter(environment);
            GachaPortableImportPrepareResult prepared = await importer.PrepareAsync(
                GachaImportSource.FromFile(packagePath),
                GachaPortableImportTarget.NewArchive,
                GachaImportMode.Incremental);
            Assert.True(prepared.Succeeded, prepared.FailureMessage);
            await using GachaPortableImportSession session = prepared.Session!;
            Assert.Equal(GachaPortableArchivePlanKind.CreateNew, session.Plan.Archive.Kind);
            Assert.Contains("新建档案", session.ConfirmationText);

            GachaPortableApplyOutcome applied = await importer.ApplyAsync(session);

            Assert.True(applied.IsSuccess, applied.Message);
            Assert.NotNull(applied.Result!.TargetArchiveId);
            Assert.Equal(68, applied.Result.InsertedRecordCount);
            PlayerArchive created = Assert.Single(
                await environment.Archives.GetAllAsync(default),
                archive => archive.Id == applied.Result.TargetArchiveId);
            Assert.Contains("01 合成档案", created.Name);
            Assert.NotEqual(sourceArchive.Id, created.Id);
            IReadOnlyList<GameAccount> createdAccounts =
                await environment.Accounts.GetByArchiveIdAsync(created.Id, default);
            Assert.Equal(3, createdAccounts.Count);
            int createdRecords = 0;
            foreach (GameAccount account in createdAccounts)
            {
                createdRecords += (await environment.Records.GetRecentAsync(account.Id, 200)).Count;
            }

            Assert.Equal(68, createdRecords);
            int originalRecords = 0;
            foreach (GameAccount account in accounts)
            {
                originalRecords += (await environment.Records.GetRecentAsync(account.Id, 200)).Count;
            }

            Assert.Equal(68, originalRecords);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RecordOperationsProduceRevisionsTombstonesAndUndoEligibility()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive archive = await ArchiveAsync(environment, "01 合成档案");
            GameAccount account = await AccountAsync(environment, archive.Id, "100000001");
            var coordinator = new GachaRecordOperationCoordinator(
                environment.GachaHistory,
                environment.Changes);
            var reference = new GachaFactReference(account.Id, "100001");

            GachaRecordOperationPlan correction = (await coordinator.PlanAsync(
                reference,
                GachaRecordOperationKind.Correct,
                new GachaCorrectionInput(
                    "纠正后的物品",
                    "demo1",
                    5,
                    new DateTimeOffset(2026, 10, 1, 12, 1, 0, TimeSpan.FromHours(8))))).Plan!;
            GachaRecordOperationOutcome corrected = await coordinator.ExecuteAsync(correction);
            Assert.True(corrected.IsSuccess, corrected.Message);
            GachaFactState correctedFact = (await environment.Changes.GetCurrentAsync(reference))!;
            Assert.Equal("纠正后的物品", correctedFact.Record.ItemName);
            Assert.Equal(DataOrigin.UserEntered, correctedFact.Record.Provenance.Origin);
            IReadOnlyList<GachaRevisionItem> revisions =
                await environment.Changes.GetRecordRevisionsAsync(reference);
            Assert.True(revisions.Count >= 2);
            Assert.Equal(EntityChangeKind.Update, revisions[^1].ChangeKind);
            Assert.Equal("合成物品 0-0-01", revisions[^1].Before!.ItemName);
            IReadOnlyList<OperationHistoryItem> history =
                await environment.Changes.GetHistoryAsync(archive.Id, 5);
            Assert.Contains(history, item => item.IsUndoEligible);

            var deletedReference = new GachaFactReference(account.Id, "100002");
            GachaRecordOperationPlan delete = (await coordinator.PlanAsync(
                deletedReference,
                GachaRecordOperationKind.Delete)).Plan!;
            Assert.True((await coordinator.ExecuteAsync(delete)).IsSuccess);
            Assert.Null(await environment.Changes.GetCurrentAsync(deletedReference));
            IReadOnlyList<GachaRevisionItem> deleteRevisions =
                await environment.Changes.GetRecordRevisionsAsync(deletedReference);
            Assert.Equal(EntityChangeKind.Delete, deleteRevisions[^1].ChangeKind);
            history = await environment.Changes.GetHistoryAsync(archive.Id, 5);
            Assert.Contains(history, item => item.IsUndoEligible);

            var permanentReference = new GachaFactReference(account.Id, "100003");
            GachaRecordOperationPlan permanent = (await coordinator.PlanAsync(
                permanentReference,
                GachaRecordOperationKind.IrreversibleDelete)).Plan!;
            Assert.True(permanent.RequiresExplicitConfirmation);
            Assert.True((await coordinator.ExecuteAsync(permanent)).IsSuccess);
            Assert.Null(await environment.Changes.GetCurrentAsync(permanentReference));
            Assert.Single(await environment.Changes.FindActiveTombstonesAsync([permanentReference]));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GachaPortableImportCoordinator CreateImporter(Ui1PreviewEnvironment environment) =>
        new(
            environment.PortableInputStager,
            environment.PortableIndexFactory,
            new ServiceGachaPortableImportPlanner(environment.PortablePlanner),
            environment.PortableApplier,
            TimeProvider.System);

    private static async Task ApplyCorrectionAsync(
        GachaRecordOperationCoordinator coordinator,
        Guid accountId,
        string recordId,
        string itemName)
    {
        GachaRecordOperationPlanResult planned = await coordinator.PlanAsync(
            new GachaFactReference(accountId, recordId),
            GachaRecordOperationKind.Correct,
            new GachaCorrectionInput(
                itemName,
                "demo1",
                5,
                new DateTimeOffset(2026, 10, 1, 12, 1, 0, TimeSpan.FromHours(8))));
        Assert.True(planned.Succeeded, planned.FailureMessage);
        GachaRecordOperationOutcome outcome = await coordinator.ExecuteAsync(planned.Plan!);
        Assert.True(outcome.IsSuccess, outcome.Message);
    }

    private static async Task ApplyOperationAsync(
        GachaRecordOperationCoordinator coordinator,
        GachaFactReference reference,
        GachaRecordOperationKind kind)
    {
        GachaRecordOperationPlanResult planned = await coordinator.PlanAsync(reference, kind);
        Assert.True(planned.Succeeded, planned.FailureMessage);
        GachaRecordOperationOutcome outcome = await coordinator.ExecuteAsync(planned.Plan!);
        Assert.True(outcome.IsSuccess, outcome.Message);
    }

    private static string FactFingerprint(GachaRecord record) =>
        $"{record.ExternalRecordId}|{record.ItemName}|{record.ItemId}|{record.ItemType}|" +
        $"{record.RankType}|{record.GachaType}|{record.UigfGachaType}|{record.Count}|" +
        $"{record.Time:O}|{record.Provenance.Origin}";

    private static IReadOnlyList<GachaPortableAccount> SortAccounts(GachaPortablePackage package) =>
        package.Accounts
            .OrderBy(account => account.RoleIdentity.NaturalIdentity.Uid, StringComparer.Ordinal)
            .ToArray();

    private static async Task<GachaPortablePackage> ReadPackageAsync(string path)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        GachaPortableReadResult result = await new GachaPortablePackageReader().ReadAsync(stream);
        return result.Package;
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));

    private static async Task<PlayerArchive> ArchiveAsync(
        Ui1PreviewEnvironment environment,
        string name) =>
        Assert.Single(
            await environment.Archives.GetAllAsync(default),
            archive => archive.Name == name);

    private static async Task<GameAccount> AccountAsync(
        Ui1PreviewEnvironment environment,
        Guid archiveId,
        string uid) =>
        Assert.Single(
            await environment.Accounts.GetByArchiveIdAsync(archiveId, default),
            account => account.Uid == uid);
}

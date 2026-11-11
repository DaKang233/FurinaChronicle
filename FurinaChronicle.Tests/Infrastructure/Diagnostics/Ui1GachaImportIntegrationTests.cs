// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Gacha;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Importing;

namespace FurinaChronicle.Tests.Infrastructure.Diagnostics;

// Exercises the real Presentation coordinator, the real compatible UIGF reader and the real
// atomic import use case against one isolated SQLite database.
public sealed class Ui1GachaImportIntegrationTests
{
    [Fact]
    public async Task CoordinatorImportsIdempotentlyAndFullModeReplacesWithRevisions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            GachaImportCoordinator coordinator = CreateCoordinator(environment);
            PlayerArchive archive = await GetArchiveAsync(environment, "01 合成档案");
            GameAccount target = await GetAccountAsync(environment, archive.Id, "100000001");
            PlayerArchive copyArchive = await GetArchiveAsync(environment, "02 同 UID 独立副本");
            GameAccount copy = await GetAccountAsync(environment, copyArchive.Id, "100000001");
            int copyCountBefore = (await environment.Records.GetRecentAsync(copy.Id, 100)).Count;

            string incrementalPath = WriteFile(
                directory,
                "incremental.json",
                Uigf42Json(
                    target.Uid,
                    Record("990001", "合成物品 新增1", "2026-11-01 12:00:00"),
                    Record("990002", "合成物品 新增2", "2026-11-01 12:01:00")));
            GachaImportSource source = GachaImportSource.FromFile(incrementalPath);
            GachaImportSourceReadResult read = await coordinator.ReadSourceAsync(source);

            Assert.True(read.Succeeded, read.FailureMessage);
            Assert.Equal("v4.2", read.Summary!.FormatVersion);
            Assert.Equal("UIGF 4.2", read.Summary.FormatLabel);
            Assert.Equal(2, read.Summary.TotalRecordCount);
            Assert.Contains("UIGF 4.2", read.Summary.FormatLabel);

            GachaImportPlanResult planned = await coordinator.PlanAsync(
                read.Summary,
                new(GachaImportTargetScope.ArchiveAccounts, archive.Id),
                GachaImportMode.Incremental);
            Assert.True(planned.Succeeded, planned.FailureMessage);
            GachaImportPlan plan = planned.Plan!;
            Assert.True(plan.CanExecute);
            Assert.Equal(2, plan.ImportableRecordCount);
            Assert.Contains("01 合成档案", plan.ConfirmationText);
            GachaImportSourceAccountPreview preview = Assert.Single(
                plan.Accounts,
                account => account.Uid == target.Uid);
            Assert.True(preview.ExistingAccount);
            Assert.False(preview.WillCreateAccount);

            GachaImportExecution first = await coordinator.ExecuteAsync(plan);

            Assert.True(first.IsSuccess, first.Message);
            GachaImportResult firstResult = first.Result!;
            Assert.Equal(2, firstResult.TotalCount);
            Assert.Equal(2, firstResult.ImportedCount);
            Assert.Equal(0, firstResult.DuplicateCount);
            Assert.Equal(0, firstResult.InvalidCount);
            Assert.Equal(0, firstResult.IgnoredCount);
            Assert.Equal(0, firstResult.CreatedAccountCount);
            Assert.Equal(0, firstResult.UpdatedCount);
            Assert.Equal(63, (await environment.Records.GetRecentAsync(target.Id, 100)).Count);

            // Same operation again is a duplicate-only NoOp, not a new fact.
            GachaImportExecution second = await coordinator.ExecuteAsync(plan);

            Assert.True(second.IsSuccess);
            Assert.Equal(0, second.Result!.ImportedCount);
            Assert.Equal(2, second.Result.DuplicateCount);
            Assert.Equal(0, second.Result.UpdatedCount);
            Assert.Equal(63, (await environment.Records.GetRecentAsync(target.Id, 100)).Count);

            // Full mode overwrites the source-provided stable record and keeps the rest.
            string overwritePath = WriteFile(
                directory,
                "overwrite.json",
                Uigf42Json(
                    target.Uid,
                    Record("100001", "合成物品 覆盖验证", "2026-10-01 12:01:00"),
                    Record("990001", "合成物品 新增1", "2026-11-01 12:00:00")));
            GachaImportSourceReadResult overwriteRead = await coordinator.ReadSourceAsync(
                GachaImportSource.FromFile(overwritePath));
            Assert.True(overwriteRead.Succeeded, overwriteRead.FailureMessage);
            GachaImportPlanResult fullPlan = await coordinator.PlanAsync(
                overwriteRead.Summary!,
                new(GachaImportTargetScope.ArchiveAccounts, archive.Id),
                GachaImportMode.Full);
            Assert.True(fullPlan.Plan!.CanExecute);
            Assert.Contains("全量导入：覆盖同记录", fullPlan.Plan.ConfirmationText);

            GachaImportExecution overwrite = await coordinator.ExecuteAsync(fullPlan.Plan!);

            Assert.True(overwrite.IsSuccess, overwrite.Message);
            Assert.Equal(0, overwrite.Result!.ImportedCount);
            Assert.Equal(1, overwrite.Result.UpdatedCount);
            Assert.Equal(1, overwrite.Result.DuplicateCount);
            IReadOnlyList<GachaRecord> stored = await environment.Records.GetRecentAsync(target.Id, 100);
            Assert.Equal(63, stored.Count);
            Assert.Contains(stored, record => record.ExternalRecordId == "990002");
            Assert.Equal(
                "合成物品 覆盖验证",
                Assert.Single(stored, record => record.ExternalRecordId == "100001").ItemName);

            IReadOnlyList<GachaRevisionItem> revisions = await environment.Changes
                .GetRecordRevisionsAsync(new GachaFactReference(target.Id, "100001"));
            Assert.True(revisions.Count >= 2);
            Assert.Equal(EntityChangeKind.Update, revisions[^1].ChangeKind);
            Assert.Equal("合成物品 覆盖验证", revisions[^1].After!.ItemName);

            // The same UID in another archive stays a fully separate copy.
            Assert.Equal(
                copyCountBefore,
                (await environment.Records.GetRecentAsync(copy.Id, 100)).Count);
            GachaImportPlanResult copyPlan = await coordinator.PlanAsync(
                read.Summary,
                new(GachaImportTargetScope.CurrentAccount, copyArchive.Id) { GameAccountId = copy.Id },
                GachaImportMode.Incremental);
            Assert.True(copyPlan.Plan!.CanExecute);
            GachaImportExecution copyImport = await coordinator.ExecuteAsync(copyPlan.Plan!);
            Assert.True(copyImport.IsSuccess, copyImport.Message);
            Assert.Equal(copyCountBefore + 2, (await environment.Records.GetRecentAsync(copy.Id, 100)).Count);
            Assert.Equal(63, (await environment.Records.GetRecentAsync(target.Id, 100)).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InvalidLegacyAndMismatchedFilesBehaveWithoutUnintendedWrites()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            GachaImportCoordinator coordinator = CreateCoordinator(environment);
            PlayerArchive archive = await GetArchiveAsync(environment, "01 合成档案");
            GameAccount target = await GetAccountAsync(environment, archive.Id, "100000001");
            GameAccount empty = await GetAccountAsync(environment, archive.Id, "100000003");

            // Malformed JSON and SRGF are rejected before any reader or write.
            foreach ((string content, string expected) in new[]
            {
                ("""{ "info": { "version": "v4.2", "export_timestamp": """, "JSON"),
                ("""{"info":{"srgf_version":"v1.0","export_timestamp":1762500000},"list":[]}""", "SRGF"),
                ("""{"info":{"version":"v9.9"}}""", "不支持的 UIGF 版本")
            })
            {
                GachaImportSourceReadResult failed = await coordinator.ReadSourceAsync(
                    GachaImportSource.FromFile(WriteFile(directory, "invalid.json", content)));
                Assert.False(failed.Succeeded);
                Assert.Contains(expected, failed.FailureMessage);
                Assert.Equal(61, (await environment.Records.GetRecentAsync(target.Id, 100)).Count);
            }

            // A real legacy v3.0 file is upgraded by the compatible reader and imported.
            string legacyPath = WriteFile(
                directory,
                "legacy.json",
                LegacyUigf30Json(
                    empty.Uid,
                    Record("990101", "合成物品 旧版1", "2026-11-04 11:00:00"),
                    Record("990102", "合成物品 旧版2", "2026-11-04 11:01:00")));
            GachaImportSourceReadResult legacyRead = await coordinator.ReadSourceAsync(
                GachaImportSource.FromFile(legacyPath));
            Assert.True(legacyRead.Succeeded, legacyRead.FailureMessage);
            Assert.Equal("v3.0", legacyRead.Summary!.FormatVersion);
            Assert.Equal("UIGF 3.0（兼容读取）", legacyRead.Summary.FormatLabel);

            GachaImportPlanResult legacyPlan = await coordinator.PlanAsync(
                legacyRead.Summary,
                new(GachaImportTargetScope.CurrentAccount, archive.Id) { GameAccountId = empty.Id },
                GachaImportMode.Incremental);
            Assert.True(legacyPlan.Plan!.CanExecute, legacyPlan.Plan.BlockMessage);
            Assert.Contains("与目标账号身份一致", legacyPlan.Plan.ConfirmationText);

            GachaImportExecution legacy = await coordinator.ExecuteAsync(legacyPlan.Plan!);

            Assert.True(legacy.IsSuccess, legacy.Message);
            Assert.Equal(2, legacy.Result!.ImportedCount);
            Assert.Equal(2, (await environment.Records.GetRecentAsync(empty.Id, 100)).Count);

            // A UID mismatch against the selected account is blocked before any write.
            GachaImportSourceReadResult otherRead = await coordinator.ReadSourceAsync(
                GachaImportSource.FromFile(WriteFile(
                    directory,
                    "other-uid.json",
                    Uigf42Json("100000002", Record("990201", "合成物品 其他账号", "2026-11-06 16:00:00")))));
            Assert.True(otherRead.Succeeded, otherRead.FailureMessage);
            GachaImportPlanResult mismatch = await coordinator.PlanAsync(
                otherRead.Summary!,
                new(GachaImportTargetScope.CurrentAccount, archive.Id) { GameAccountId = target.Id },
                GachaImportMode.Incremental);

            Assert.False(mismatch.Plan!.CanExecute);
            Assert.Contains("UID", mismatch.Plan.BlockMessage);
            Assert.Contains("未写入任何数据", mismatch.Plan.BlockMessage);
            Assert.Equal(61, (await environment.Records.GetRecentAsync(target.Id, 100)).Count);
            Assert.Equal(2, (await environment.Records.GetRecentAsync(empty.Id, 100)).Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static GachaImportCoordinator CreateCoordinator(Ui1PreviewEnvironment environment) =>
        new(
            environment.GachaImportReader,
            new ServiceGachaUigfImporter(environment.GachaImporter),
            new ServiceGachaImportPlanningSource(
                new GetPlayerArchives(environment.Archives),
                new GetGameAccounts(environment.Archives, environment.Accounts)));

    private static async Task<PlayerArchive> GetArchiveAsync(
        Ui1PreviewEnvironment environment,
        string name) =>
        Assert.Single(await environment.Archives.GetAllAsync(default), archive => archive.Name == name);

    private static async Task<GameAccount> GetAccountAsync(
        Ui1PreviewEnvironment environment,
        Guid archiveId,
        string uid) =>
        Assert.Single(await environment.Accounts.GetByArchiveIdAsync(archiveId, default),
            account => account.Uid == uid);

    private static string WriteFile(string directory, string name, string content)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string Uigf42Json(string uid, params string[] records) =>
        $$"""
        {
          "info": {
            "export_timestamp": 1762500000,
            "export_app": "SyntheticUi3Tests",
            "export_app_version": "1.0.0",
            "version": "v4.2"
          },
          "hk4e": [{
            "uid": "{{uid}}",
            "timezone": 8,
            "lang": "zh-cn",
            "list": [{{string.Join(",", records)}}]
          }]
        }
        """;

    private static string LegacyUigf30Json(string uid, params string[] records) =>
        $$"""
        {
          "info": {
            "uigf_version": "v3.0",
            "uid": "{{uid}}",
            "lang": "zh-cn",
            "export_timestamp": 1762500100,
            "export_app": "SyntheticUi3Tests",
            "export_app_version": "1.0.0",
            "region_time_zone": 8
          },
          "list": [{{string.Join(",", records)}}]
        }
        """;

    private static string Record(string id, string name, string time) =>
        $$"""
        {
          "uigf_gacha_type": "301",
          "gacha_type": "301",
          "item_id": "demo-{{id}}",
          "count": "1",
          "time": "{{time}}",
          "name": "{{name}}",
          "item_type": "角色",
          "rank_type": "5",
          "id": "{{id}}"
        }
        """;
}

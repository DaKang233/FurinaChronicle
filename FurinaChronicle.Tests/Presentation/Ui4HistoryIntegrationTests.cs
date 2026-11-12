// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.History;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;
using FurinaChronicle.Services.Gacha.History;

namespace FurinaChronicle.Tests.Presentation;

// Real SQLite coverage for the UI-4 operation history page: eligibility, UndoLimit, revision
// before/after and the actual undo write, against the isolated preview environment only.
public sealed class Ui4HistoryIntegrationTests
{
    [Fact]
    public async Task HistoryUndoLimitAndRevisionDetailsUseProductionServices()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            PlayerArchive archive = Assert.Single(
                await environment.Archives.GetAllAsync(default),
                item => item.Name == "01 合成档案");
            IReadOnlyList<GameAccount> accounts =
                await environment.Accounts.GetByArchiveIdAsync(archive.Id);
            GachaRecord original = Assert.Single(
                (await new GetGachaRecordPage(environment.Records).ExecuteAsync(
                    new GachaRecordQuery([accounts[0].Id], Limit: 1),
                    1,
                    1)).Records);
            var history = new ManageGachaHistory(environment.Changes);
            GachaAtomicChangeResult correction = await history.CorrectAsync(
                OperationId.New(),
                new GachaFactReference(original.GameAccountId, original.ExternalRecordId),
                new GachaCorrectionInput("UI-4 撤销测试", original.ItemId, 5, original.Time));
            Assert.Equal(ChangeExecutionStatus.Applied, correction.Status);

            var state = new ArchiveHistoryState(BuildSource(environment));
            await state.EnterAsync();
            ArchiveHistoryEntry entry = Assert.Single(
                state.Snapshot.Entries,
                item => item.OperationKind == DataChangeOperationKind.Correction);
            Assert.True(entry.IsUndoEligible);
            Assert.Equal("可撤销", entry.UndoState);

            await state.SelectOperationAsync(entry.ChangeSetId);
            ArchiveHistoryRevision revision = Assert.Single(state.Snapshot.Revisions);
            Assert.Equal("抽卡记录", revision.Kind);
            Assert.Contains(original.ItemName ?? original.ItemId ?? "?", revision.Before);
            Assert.Contains("UI-4 撤销测试", revision.After);

            ArchiveHistoryActionResult undo = await state.UndoLatestAsync();
            Assert.True(undo.Succeeded);

            GachaRecord restored = Assert.Single(
                await environment.Records.GetByExternalRecordIdsAsync(
                    original.GameAccountId,
                    [original.ExternalRecordId]));
            Assert.Equal(original.ItemName, restored.ItemName);
            Assert.Equal(original.RankType, restored.RankType);

            ArchiveHistoryActionResult disabled = await state.SetUndoLimitAsync(0);
            Assert.True(disabled.Succeeded);
            Assert.Equal(0, state.Snapshot.UndoLimit);
            Assert.Null(state.Snapshot.LatestUndoCandidate);

            ArchiveHistoryActionResult enabled = await state.SetUndoLimitAsync(3);
            Assert.True(enabled.Succeeded);
            Assert.Equal(3, state.Snapshot.UndoLimit);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceArchiveHistorySource BuildSource(Ui1PreviewEnvironment environment) =>
        new(
            new GetPlayerArchives(environment.Archives),
            environment.Changes,
            environment.Changes,
            environment.Changes,
            TimeProvider.System);

    private static string TempDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "FurinaChronicleUi4History",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

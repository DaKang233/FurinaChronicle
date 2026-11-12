// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Infrastructure.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha;

namespace FurinaChronicle.Tests.Presentation;

// Real-SQLite checks for the two management page models, using the isolated UI-1 preview database.
public sealed class Ui4ManagementIntegrationTests
{
    [Fact]
    public async Task OverviewAggregatesTheSeededIsolatedDatabase()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            var state = new OverviewState(CreateOverviewSource(environment));

            await state.EnterAsync();

            Assert.Equal(3, state.Snapshot.ArchiveCount);
            Assert.Equal(4, state.Snapshot.AccountCount);
            Assert.Equal(0, state.Snapshot.UnresolvedCount);
            Assert.Equal(71, state.Snapshot.TotalGachaRecords);
            Assert.Equal(3, state.Snapshot.Archives.Count);
            Assert.Equal(5, state.Snapshot.RecentActivities.Count);
            Assert.Contains(state.Snapshot.CoverageNotes, note => note.Contains("待 UI-5"));
            Assert.Contains(
                state.Snapshot.Archives,
                archive => archive.Name == "01 合成档案" && archive.RecordCount == 68);
            Assert.Contains(
                state.Snapshot.Archives,
                archive => archive.Name == "02 同 UID 独立副本" && archive.RecordCount == 3);
            Assert.Equal(
                state.Snapshot.RecentActivities
                    .OrderByDescending(activity => activity.OccurredAt)
                    .Select(activity => activity.OccurredAt),
                state.Snapshot.RecentActivities.Select(activity => activity.OccurredAt));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ArchiveAndAccountManagementRunsAgainstRealSqlite()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            ArchiveManagementState state = CreateManagementState(environment);
            await state.EnterAsync();
            Assert.Equal(3, state.Snapshot.ArchiveCount);

            await state.CreateArchiveAsync("UI-4 测试档案");

            Assert.Equal(4, state.Snapshot.ArchiveCount);
            Assert.Equal("UI-4 测试档案", state.Snapshot.SelectedArchive!.Name);
            Assert.Contains("已创建档案", state.Snapshot.Status);
            Guid archiveId = state.Snapshot.SelectedArchiveId!.Value;

            await state.CreateArchiveAsync("UI-4 测试档案");
            Assert.Contains("已存在完全同名", state.Snapshot.Error);
            Assert.Equal(4, state.Snapshot.ArchiveCount);

            await state.RenameArchiveAsync("UI-4 改名档案");
            Assert.Equal("UI-4 改名档案", state.Snapshot.SelectedArchive!.Name);

            await state.AddAccountAsync("100000009", GameServerRegion.ChinaOfficial, "UI4 账号");
            Assert.Single(state.Snapshot.Accounts);
            GameAccount account = state.Snapshot.SelectedAccount!;
            Assert.Equal("100000009", account.Uid);
            Assert.Equal("UI4 账号", account.DisplayName);
            Assert.Contains(
                "身份已确认",
                ManagementTexts.IdentityStatus(account, state.DiagnosticFor(account)));

            await state.UpdateAccountAsync("100000009", GameServerRegion.ChinaOfficial, "UI4 改名账号");
            Assert.Equal("UI4 改名账号", state.Snapshot.SelectedAccount!.DisplayName);

            var placeholder = new GameAccount(
                Guid.NewGuid(),
                archiveId,
                "12345",
                GameServerRegion.Unknown,
                DisplayName: null,
                IsPlaceholder: true,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                RoleIdentity: null);
            await environment.Accounts.AddAsync(placeholder);
            await state.EnterAsync();
            Assert.True(state.Snapshot.UnresolvedCount >= 1);
            UnresolvedGameAccountDiagnostic diagnostic = state.DiagnosticFor(placeholder)!;
            Assert.Equal(UnresolvedGameAccountReason.PlaceholderAccount, diagnostic.Reason);
            Assert.Contains(
                "身份待确认",
                ManagementTexts.IdentityStatus(placeholder, diagnostic));

            await state.SelectAccountAsync(account.Id);
            await state.DeleteAccountAsync();
            Assert.DoesNotContain(
                state.Snapshot.Accounts,
                candidate => candidate.Id == account.Id);

            await state.SelectArchiveAsync(archiveId);
            await state.DeleteArchiveAsync();
            Assert.Equal(3, state.Snapshot.ArchiveCount);
            Assert.Contains("已永久删除档案", state.Snapshot.Status);
            Assert.DoesNotContain(
                state.Snapshot.Archives,
                candidate => candidate.Id == archiveId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task DeletingSelectedArchiveRepairsSelectionWithExplicitNote()
    {
        string directory = TempDirectory();
        try
        {
            await using var environment = new Ui1PreviewEnvironment(directory, "WinUI");
            await environment.InitializeAsync();
            ArchiveManagementState state = CreateManagementState(environment);
            await state.EnterAsync();
            PlayerArchive copyArchive = state.Snapshot.Archives.Single(
                archive => archive.Name == "02 同 UID 独立副本");

            await state.SelectArchiveAsync(copyArchive.Id);
            Assert.Equal(copyArchive.Id, state.Snapshot.SelectedArchiveId);
            Assert.Contains("确定要永久删除所选档案吗", state.BuildDeleteArchiveWarning());
            Assert.Contains("此操作不可撤销", state.BuildDeleteArchiveWarning());

            await state.DeleteArchiveAsync();

            Assert.NotEqual(copyArchive.Id, state.Snapshot.SelectedArchiveId);
            Assert.Contains("原选择档案已不存在，已回退到第一个可用档案", state.Snapshot.Status);
            Assert.Equal(2, state.Snapshot.ArchiveCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ServiceOverviewSource CreateOverviewSource(Ui1PreviewEnvironment environment) =>
        new(
            new GetPlayerArchives(environment.Archives),
            new GetGameAccounts(environment.Archives, environment.Accounts),
            new GetUnresolvedGameAccounts(environment.Archives, environment.Accounts),
            new GetGachaRecordPage(environment.Records));

    private static ArchiveManagementState CreateManagementState(Ui1PreviewEnvironment environment) =>
        new(new ServiceArchiveManagementSource(
            new GetPlayerArchives(environment.Archives),
            new GetGameAccounts(environment.Archives, environment.Accounts),
            new GetUnresolvedGameAccounts(environment.Archives, environment.Accounts),
            new CreatePlayerArchive(environment.Archives),
            new RenamePlayerArchive(environment.Archives),
            new DeletePlayerArchive(environment.Changes),
            new AddGameAccount(environment.Archives, environment.Accounts),
            new UpdateGameAccount(environment.Accounts),
            new DeleteGameAccount(environment.Changes)));

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "FurinaChronicleTests", Guid.NewGuid().ToString("N"));
}

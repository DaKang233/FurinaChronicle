// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Tests.Presentation;

public sealed class ArchiveManagementStateTests
{
    private static readonly DateTimeOffset Time = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EnterSelectsFirstArchiveAndAccountAndReportsCounts()
    {
        var source = new FakeSource();
        PlayerArchive first = source.AddArchive("档案 A");
        source.AddAccount(first.Id, "100000001", "A 账号");
        var state = new ArchiveManagementState(source);

        await state.EnterAsync();

        Assert.False(state.Snapshot.IsBusy);
        Assert.Equal(first.Id, state.Snapshot.SelectedArchiveId);
        Assert.Equal("档案 A", state.Snapshot.SelectedArchive!.Name);
        Assert.Single(state.Snapshot.Accounts);
        Assert.Contains("已载入 1 个档案", state.Snapshot.Status);
    }

    [Fact]
    public async Task MissingArchiveFallsBackWithExplicitMessage()
    {
        var source = new FakeSource();
        PlayerArchive first = source.AddArchive("档案 A");
        PlayerArchive second = source.AddArchive("档案 B");
        source.AddAccount(first.Id, "100000001", "A 账号");
        source.AddAccount(second.Id, "100000002", "B 账号");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();
        await state.SelectArchiveAsync(second.Id);

        source.Archives.RemoveAll(archive => archive.Id == second.Id);
        await state.EnterAsync();

        Assert.Equal(first.Id, state.Snapshot.SelectedArchiveId);
        Assert.Contains("原选择档案已不存在，已回退到第一个可用档案", state.Snapshot.Status);
    }

    [Fact]
    public async Task MissingAccountFallsBackWithExplicitMessage()
    {
        var source = new FakeSource();
        PlayerArchive archive = source.AddArchive("档案 A");
        GameAccount first = source.AddAccount(archive.Id, "100000001", "A 账号");
        GameAccount second = source.AddAccount(archive.Id, "100000002", "B 账号");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();
        await state.SelectAccountAsync(second.Id);

        source.Accounts[archive.Id].RemoveAll(account => account.Id == second.Id);
        await state.EnterAsync();

        Assert.Equal(first.Id, state.Snapshot.SelectedAccountId);
        Assert.Contains("原选择账号已不存在", state.Snapshot.Status);
    }

    [Fact]
    public async Task EmptyDatabaseHasExplicitEmptyState()
    {
        var source = new FakeSource();
        var state = new ArchiveManagementState(source);

        await state.EnterAsync();

        Assert.Empty(state.Snapshot.Archives);
        Assert.Empty(state.Snapshot.Accounts);
        Assert.Null(state.Snapshot.SelectedArchiveId);
        Assert.Contains("尚未创建档案", state.Snapshot.Status);
    }

    [Fact]
    public async Task CreateArchivePassesNameUnchangedAndSelectsIt()
    {
        var source = new FakeSource();
        var state = new ArchiveManagementState(source);

        await state.CreateArchiveAsync("  精确 名称  ");

        Assert.Equal("  精确 名称  ", source.LastCreateName);
        Assert.Equal(source.Archives.Single().Id, state.Snapshot.SelectedArchiveId);
        Assert.Contains("已创建档案", state.Snapshot.Status);
    }

    [Fact]
    public async Task DuplicateArchiveNameSurfacesServiceMessageWithoutMutatingState()
    {
        var source = new FakeSource();
        source.AddArchive("档案 A");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();
        source.CreateException = new InvalidOperationException("已存在完全同名的玩家档案。");

        await state.CreateArchiveAsync("档案 A");

        Assert.Contains("已存在完全同名的玩家档案", state.Snapshot.Error);
        Assert.Equal("档案 A", state.Snapshot.SelectedArchive!.Name);
    }

    [Fact]
    public async Task DeleteWarningsIncludeMauiTextAndCascadeScope()
    {
        var source = new FakeSource();
        PlayerArchive archive = source.AddArchive("档案 A");
        source.AddAccount(archive.Id, "100000001", "A 账号");
        source.AddAccount(archive.Id, "100000002", "B 账号");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();

        Assert.Contains("确定要永久删除所选档案吗", state.BuildDeleteArchiveWarning());
        Assert.Contains("2 个账号副本", state.BuildDeleteArchiveWarning());
        Assert.Contains("此操作不可撤销", state.BuildDeleteArchiveWarning());
        Assert.Contains("确定要永久删除所选游戏账号吗", state.BuildDeleteAccountWarning());
        Assert.Contains("其他档案", state.BuildDeleteAccountWarning());
    }

    [Fact]
    public async Task UnresolvedAccountsExposePendingIdentityState()
    {
        var source = new FakeSource();
        PlayerArchive archive = source.AddArchive("档案 A");
        GameAccount account = source.AddAccount(archive.Id, "100000001", "A 账号");
        source.Unresolved.Add(new UnresolvedGameAccountDiagnostic(
            account.Id,
            archive.Id,
            archive.Name,
            account.Uid,
            account.ServerRegion,
            UnresolvedGameAccountReason.PlaceholderAccount));
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();

        UnresolvedGameAccountDiagnostic diagnostic = state.DiagnosticFor(account)!;
        Assert.Equal(UnresolvedGameAccountReason.PlaceholderAccount, diagnostic.Reason);
        Assert.Contains(
            "身份待确认",
            ManagementTexts.IdentityStatus(account, diagnostic));
        Assert.Contains("占位账号", ManagementTexts.IdentityStatus(account, diagnostic));
        Assert.Equal(1, state.Snapshot.UnresolvedCount);
    }

    [Fact]
    public async Task DeleteArchiveLeavesNoStaleSelection()
    {
        var source = new FakeSource();
        PlayerArchive first = source.AddArchive("档案 A");
        PlayerArchive second = source.AddArchive("档案 B");
        var state = new ArchiveManagementState(source);
        await state.SelectArchiveAsync(second.Id);

        await state.DeleteArchiveAsync();

        Assert.Equal(first.Id, state.Snapshot.SelectedArchiveId);
        Assert.Contains("原选择档案已不存在，已回退到第一个可用档案", state.Snapshot.Status);
        Assert.Contains("已永久删除档案", state.Snapshot.Status);
    }

    [Fact]
    public async Task AccountOperationsPassRawValuesAndRefreshSelection()
    {
        var source = new FakeSource();
        PlayerArchive archive = source.AddArchive("档案 A");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();

        await state.AddAccountAsync(" 100000009 ", GameServerRegion.ChinaOfficial, " 备注 ");
        Assert.Equal(" 100000009 ", source.LastAddedUid);
        Assert.Equal(" 备注 ", source.LastAddedDisplayName);
        Assert.NotNull(state.Snapshot.SelectedAccountId);

        await state.UpdateAccountAsync("100000009", GameServerRegion.ChinaOfficial, "新备注");
        Assert.Equal("新备注", source.LastUpdatedDisplayName);
        Assert.Contains("已更新账号", state.Snapshot.Status);

        Guid accountId = state.Snapshot.SelectedAccountId!.Value;
        await state.DeleteAccountAsync();
        Assert.Empty(state.Snapshot.Accounts);
        Assert.Contains("原选择账号已不存在，此档案暂无账号", state.Snapshot.Status);
        Assert.DoesNotContain(source.Accounts.GetValueOrDefault(archive.Id) ?? [],
            account => account.Id == accountId);
    }

    [Fact]
    public async Task UnsafeExceptionMessageIsNotShown()
    {
        var source = new FakeSource();
        source.AddArchive("档案 A");
        var state = new ArchiveManagementState(source);
        await state.EnterAsync();
        source.CreateException = new InvalidOperationException(@"C:\secret\db3 SQLite failure");

        await state.CreateArchiveAsync("新档案");

        Assert.DoesNotContain("secret", state.Snapshot.Error);
        Assert.Contains("操作未完成", state.Snapshot.Error);
    }

    private sealed class FakeSource : IArchiveManagementSource
    {
        public List<PlayerArchive> Archives { get; } = [];
        public Dictionary<Guid, List<GameAccount>> Accounts { get; } = [];
        public List<UnresolvedGameAccountDiagnostic> Unresolved { get; } = [];
        public Exception? CreateException { get; set; }
        public string? LastCreateName { get; private set; }
        public string? LastAddedUid { get; private set; }
        public string? LastAddedDisplayName { get; private set; }
        public string? LastUpdatedDisplayName { get; private set; }

        public PlayerArchive AddArchive(string name)
        {
            var archive = new PlayerArchive(Guid.NewGuid(), name, Time, Time);
            Archives.Add(archive);
            Accounts[archive.Id] = [];
            return archive;
        }

        public GameAccount AddAccount(Guid archiveId, string uid, string? displayName)
        {
            GameRoleNaturalIdentity identity = GenshinGameRoleIdentity.Create(
                uid,
                GameServerRegion.ChinaOfficial);
            var account = new GameAccount(
                Guid.NewGuid(),
                archiveId,
                uid,
                GameServerRegion.ChinaOfficial,
                displayName,
                IsPlaceholder: false,
                Time,
                Time,
                new GameRoleIdentity(GameRoleIdentityId.FromNaturalIdentity(identity), identity));
            Accounts[archiveId].Add(account);
            return account;
        }

        public Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PlayerArchive>>(Archives.ToArray());

        public Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
            Guid playerArchiveId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<GameAccount>>(
                Accounts.TryGetValue(playerArchiveId, out List<GameAccount>? value)
                    ? value.ToArray()
                    : []);

        public Task<IReadOnlyList<UnresolvedGameAccountDiagnostic>> GetUnresolvedAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UnresolvedGameAccountDiagnostic>>(Unresolved.ToArray());

        public Task<PlayerArchive> CreateArchiveAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            if (CreateException is not null) return Task.FromException<PlayerArchive>(CreateException);
            LastCreateName = name;
            return Task.FromResult(AddArchive(name));
        }

        public Task<PlayerArchive> RenameArchiveAsync(
            Guid archiveId,
            string name,
            CancellationToken cancellationToken = default)
        {
            PlayerArchive archive = Archives.Single(item => item.Id == archiveId);
            PlayerArchive renamed = archive with { Name = name };
            Archives[Archives.IndexOf(archive)] = renamed;
            return Task.FromResult(renamed);
        }

        public Task DeleteArchiveAsync(
            Guid archiveId,
            CancellationToken cancellationToken = default)
        {
            Archives.RemoveAll(item => item.Id == archiveId);
            Accounts.Remove(archiveId);
            Unresolved.RemoveAll(item => item.PlayerArchiveId == archiveId);
            return Task.CompletedTask;
        }

        public Task<GameAccount> AddAccountAsync(
            Guid playerArchiveId,
            string uid,
            GameServerRegion serverRegion,
            string? displayName,
            CancellationToken cancellationToken = default)
        {
            LastAddedUid = uid;
            LastAddedDisplayName = displayName;
            return Task.FromResult(AddAccount(playerArchiveId, uid.Trim(), displayName));
        }

        public Task<GameAccount> UpdateAccountAsync(
            Guid gameAccountId,
            string uid,
            GameServerRegion serverRegion,
            string? displayName,
            CancellationToken cancellationToken = default)
        {
            LastUpdatedDisplayName = displayName;
            foreach (List<GameAccount> list in Accounts.Values)
            {
                int index = list.FindIndex(account => account.Id == gameAccountId);
                if (index < 0) continue;
                GameAccount updated = list[index] with { DisplayName = displayName };
                list[index] = updated;
                return Task.FromResult(updated);
            }

            return Task.FromException<GameAccount>(new KeyNotFoundException("账号不存在。"));
        }

        public Task DeleteAccountAsync(
            Guid gameAccountId,
            CancellationToken cancellationToken = default)
        {
            foreach (List<GameAccount> list in Accounts.Values)
            {
                list.RemoveAll(account => account.Id == gameAccountId);
            }

            return Task.CompletedTask;
        }
    }
}

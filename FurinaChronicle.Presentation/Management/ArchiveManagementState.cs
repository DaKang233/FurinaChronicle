// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;

namespace FurinaChronicle.Presentation.Management;

// Page-local archive/account management state. Selection is kept only as stable IDs inside this
// page instance; entering the page always re-queries the database and repairs stale selections.
public interface IArchiveManagementSource
{
    Task<IReadOnlyList<PlayerArchive>> GetArchivesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GameAccount>> GetAccountsAsync(
        Guid playerArchiveId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnresolvedGameAccountDiagnostic>> GetUnresolvedAsync(
        CancellationToken cancellationToken = default);

    Task<PlayerArchive> CreateArchiveAsync(string name, CancellationToken cancellationToken = default);

    Task<PlayerArchive> RenameArchiveAsync(
        Guid archiveId,
        string name,
        CancellationToken cancellationToken = default);

    Task DeleteArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default);

    Task<GameAccount> AddAccountAsync(
        Guid playerArchiveId,
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default);

    Task<GameAccount> UpdateAccountAsync(
        Guid gameAccountId,
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default);

    Task DeleteAccountAsync(Guid gameAccountId, CancellationToken cancellationToken = default);
}

public sealed record ArchiveManagementSnapshot
{
    public IReadOnlyList<PlayerArchive> Archives { get; init; } = [];
    public IReadOnlyList<GameAccount> Accounts { get; init; } = [];
    public IReadOnlyList<UnresolvedGameAccountDiagnostic> Unresolved { get; init; } = [];
    public Guid? SelectedArchiveId { get; init; }
    public Guid? SelectedAccountId { get; init; }
    public bool IsBusy { get; init; }
    public string Status { get; init; } = "尚未载入";
    public string? Error { get; init; }

    public PlayerArchive? SelectedArchive =>
        Archives.FirstOrDefault(archive => archive.Id == SelectedArchiveId);

    public GameAccount? SelectedAccount =>
        Accounts.FirstOrDefault(account => account.Id == SelectedAccountId);

    public int ArchiveCount => Archives.Count;

    public int AccountCount => Accounts.Count;

    public int UnresolvedCount => Unresolved.Count;

    public bool IsSelectedArchiveUnresolved =>
        SelectedArchiveId is Guid archiveId &&
        Unresolved.Any(item => item.PlayerArchiveId == archiveId);
}

public sealed class ArchiveManagementState(IArchiveManagementSource source)
{
    private Guid? preferredArchiveId;
    private Guid? preferredAccountId;

    public ArchiveManagementSnapshot Snapshot { get; private set; } = new();

    public Task EnterAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(null, cancellationToken);

    public Task SelectArchiveAsync(Guid archiveId, CancellationToken cancellationToken = default)
    {
        preferredArchiveId = archiveId;
        preferredAccountId = null;
        return LoadAsync(null, cancellationToken);
    }

    public Task SelectAccountAsync(Guid? gameAccountId, CancellationToken cancellationToken = default)
    {
        preferredAccountId = gameAccountId;
        return LoadAsync(null, cancellationToken);
    }

    public async Task CreateArchiveAsync(string name, CancellationToken cancellationToken = default)
    {
        try
        {
            PlayerArchive created = await source.CreateArchiveAsync(name, cancellationToken);
            preferredArchiveId = created.Id;
            preferredAccountId = null;
            await LoadAsync($"已创建档案“{created.Name}”。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public async Task RenameArchiveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            Fail("请先选择要重命名的档案。");
            return;
        }

        try
        {
            PlayerArchive renamed = await source.RenameArchiveAsync(archiveId, name, cancellationToken);
            await LoadAsync($"已重命名档案为“{renamed.Name}”。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public async Task DeleteArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            Fail("请先选择要删除的档案。");
            return;
        }

        try
        {
            await source.DeleteArchiveAsync(archiveId, cancellationToken);
            // Keep the deleted ID as the preferred one so reload reports the fallback explicitly.
            preferredArchiveId = archiveId;
            await LoadAsync("已永久删除档案及其本地数据。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public async Task AddAccountAsync(
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedArchiveId is not Guid archiveId)
        {
            Fail("请先选择要添加账号的档案。");
            return;
        }

        try
        {
            GameAccount created = await source.AddAccountAsync(
                archiveId,
                uid,
                serverRegion,
                displayName,
                cancellationToken);
            preferredAccountId = created.Id;
            await LoadAsync($"已添加账号 UID {created.Uid}。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public async Task UpdateAccountAsync(
        string uid,
        GameServerRegion serverRegion,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedAccountId is not Guid accountId)
        {
            Fail("请先选择要编辑的游戏账号。");
            return;
        }

        try
        {
            GameAccount updated = await source.UpdateAccountAsync(
                accountId,
                uid,
                serverRegion,
                displayName,
                cancellationToken);
            await LoadAsync($"已更新账号 UID {updated.Uid}。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public async Task DeleteAccountAsync(CancellationToken cancellationToken = default)
    {
        if (Snapshot.SelectedAccountId is not Guid accountId)
        {
            Fail("请先选择要删除的游戏账号。");
            return;
        }

        try
        {
            await source.DeleteAccountAsync(accountId, cancellationToken);
            preferredAccountId = accountId;
            await LoadAsync("已永久删除游戏账号及其本地数据。", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Fail(ManagementTexts.DescribeOperationFailure(exception));
        }
    }

    public string BuildDeleteArchiveWarning() =>
        ManagementTexts.DeleteArchiveConfirmation + Environment.NewLine + Environment.NewLine +
        ManagementTexts.ArchiveCascadeWarning(Snapshot.Accounts.Count);

    public string BuildDeleteAccountWarning() =>
        ManagementTexts.DeleteAccountConfirmation + Environment.NewLine + Environment.NewLine +
        ManagementTexts.AccountCascadeWarning();

    public UnresolvedGameAccountDiagnostic? DiagnosticFor(GameAccount account) =>
        Snapshot.Unresolved.FirstOrDefault(item => item.GameAccountId == account.Id);

    private async Task LoadAsync(string? operationStatus, CancellationToken cancellationToken)
    {
        Set(Snapshot with { IsBusy = true, Status = "正在读取本地档案…", Error = null });
        try
        {
            IReadOnlyList<PlayerArchive> archives = await source.GetArchivesAsync(cancellationToken);
            List<string> notes = [];
            Guid? archiveId = preferredArchiveId is Guid preferredArchive &&
                archives.Any(archive => archive.Id == preferredArchive)
                ? preferredArchive
                : archives.FirstOrDefault()?.Id;
            if (preferredArchiveId is not null && archiveId != preferredArchiveId)
            {
                notes.Add(archives.Count == 0
                    ? "原选择档案已不存在，请创建档案。"
                    : "原选择档案已不存在，已回退到第一个可用档案。");
            }

            IReadOnlyList<GameAccount> accounts = archiveId is Guid selectedArchive
                ? await source.GetAccountsAsync(selectedArchive, cancellationToken)
                : [];
            Guid? accountId = preferredAccountId is Guid preferredAccount &&
                accounts.Any(account => account.Id == preferredAccount)
                ? preferredAccount
                : accounts.FirstOrDefault()?.Id;
            if (preferredAccountId is not null && accountId != preferredAccountId)
            {
                notes.Add(accounts.Count == 0
                    ? "原选择账号已不存在，此档案暂无账号。"
                    : "原选择账号已不存在，已回退到该档案的第一个账号。");
            }

            IReadOnlyList<UnresolvedGameAccountDiagnostic> unresolved =
                await source.GetUnresolvedAsync(cancellationToken);
            preferredArchiveId = archiveId;
            preferredAccountId = accountId;
            string status = operationStatus ??
                (archives.Count == 0
                    ? "尚未创建档案。请先创建档案，再添加游戏账号。"
                    : $"已载入 {archives.Count} 个档案；当前范围 {accounts.Count} 个账号。");
            if (notes.Count > 0)
            {
                status = string.Join(Environment.NewLine, notes) + Environment.NewLine + status;
            }

            Set(new ArchiveManagementSnapshot
            {
                Archives = archives,
                Accounts = accounts,
                Unresolved = unresolved,
                SelectedArchiveId = archiveId,
                SelectedAccountId = accountId,
                Status = status
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
                Status = "档案与账号读取失败；本地数据未改变。",
                Error = ManagementTexts.DescribeOperationFailure(exception)
            });
        }
    }

    private void Fail(string message)
    {
        Set(Snapshot with { IsBusy = false, Error = message });
    }

    private void Set(ArchiveManagementSnapshot snapshot) => Snapshot = snapshot;
}

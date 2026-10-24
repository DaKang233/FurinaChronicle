// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public partial class ArchivePageViewModel(
    GetPlayerArchives getPlayerArchives,
    GetGameAccounts getGameAccounts,
    CreatePlayerArchive createPlayerArchive,
    RenamePlayerArchive renamePlayerArchive,
    DeletePlayerArchive deletePlayerArchive,
    AddGameAccount addGameAccount,
    UpdateGameAccount updateGameAccount,
    DeleteGameAccount deleteGameAccount)
    : ObservableObject
{
    public ObservableCollection<PlayerArchive> Archives { get; } = [];

    public ObservableCollection<GameAccount> Accounts { get; } = [];

    public IReadOnlyList<GameServerRegion> ServerRegions { get; } =
        Enum.GetValues<GameServerRegion>()
            .Where(region => region != GameServerRegion.Unknown)
            .ToArray();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ReloadCommand))]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    public partial bool IsBusy { get; set; }

    public bool IsNotBusy => !IsBusy;

    [ObservableProperty]
    public partial PlayerArchive? SelectedArchive { get; set; }

    [ObservableProperty]
    public partial GameAccount? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial string NewAccountUid { get; set; } = string.Empty;

    [ObservableProperty]
    public partial GameServerRegion SelectedServerRegion { get; set; } =
        GameServerRegion.Unknown;

    [ObservableProperty]
    public partial string? NewAccountDisplayName { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "正在加载档案。";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public Task InitializeAsync() => ReloadAsync();

    [RelayCommand(CanExecute = nameof(CanRun))]
    public async Task ReloadAsync()
    {
        Guid? archiveId = SelectedArchive?.Id;
        Guid? accountId = SelectedAccount?.Id;

        await ExecuteBusyAsync(async () =>
        {
            await LoadArchivesCoreAsync(archiveId);
            await LoadAccountsCoreAsync(accountId);
            StatusMessage = SelectedArchive is null
                ? "当前没有档案。"
                : $"已加载档案“{SelectedArchive.Name}”。";
        });
    }

    public async Task SelectArchiveAsync(PlayerArchive? archive)
    {
        await ExecuteBusyAsync(async () =>
        {
            SelectedArchive = archive;
            await LoadAccountsCoreAsync();
            StatusMessage = archive is null
                ? "请先选择档案。"
                : $"已选择档案“{archive.Name}”。";
        });
    }

    public void SelectAccount(GameAccount? account)
    {
        if (account is not null &&
            account.PlayerArchiveId != SelectedArchive?.Id)
        {
            ErrorMessage = "所选账号不属于当前档案。";
            return;
        }

        SelectedAccount = account;
        ErrorMessage = null;
        StatusMessage = account is null
            ? "当前档案尚未选择账号。"
            : $"已选择账号 {account.Uid}。";
    }

    public async Task CreateArchiveAsync(string name)
    {
        await ExecuteBusyAsync(async () =>
        {
            PlayerArchive archive =
                await createPlayerArchive.ExecuteAsync(name);
            await LoadArchivesCoreAsync(archive.Id);
            await LoadAccountsCoreAsync();
            StatusMessage = $"已创建档案“{archive.Name}”。";
        });
    }

    public async Task RenameSelectedArchiveAsync(string name)
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedArchive is null)
            {
                throw new InvalidOperationException("请先选择要重命名的档案。");
            }

            PlayerArchive archive = await renamePlayerArchive.ExecuteAsync(
                SelectedArchive.Id,
                name);
            await LoadArchivesCoreAsync(archive.Id);
            await LoadAccountsCoreAsync(SelectedAccount?.Id);
            StatusMessage = $"已将档案重命名为“{archive.Name}”。";
        });
    }

    public async Task DeleteSelectedArchiveAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedArchive is null)
            {
                throw new InvalidOperationException("请先选择要删除的档案。");
            }

            string archiveName = SelectedArchive.Name;
            await deletePlayerArchive.ExecuteAsync(SelectedArchive.Id);
            SelectedArchive = null;
            SelectedAccount = null;
            await LoadArchivesCoreAsync();
            await LoadAccountsCoreAsync();
            StatusMessage = $"已删除档案“{archiveName}”。";
        });
    }

    public async Task CreateAccountAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedArchive is null)
            {
                throw new InvalidOperationException("请先选择档案。");
            }

            string displayName = string.IsNullOrWhiteSpace(NewAccountDisplayName)
                ? NewAccountUid
                : $"{NewAccountUid} ({NewAccountDisplayName.Trim()})";
            GameAccount account = await addGameAccount.ExecuteAsync(
                SelectedArchive.Id,
                NewAccountUid,
                SelectedServerRegion,
                displayName);

            await LoadAccountsCoreAsync(account.Id);
            NewAccountUid = string.Empty;
            SelectedServerRegion = GameServerRegion.Unknown;
            NewAccountDisplayName = null;
            StatusMessage = $"已创建账号 {account.Uid}。";
        });
    }

    public async Task EditSelectedAccountAsync(
        string displayName,
        string uid)
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedAccount is null)
            {
                throw new InvalidOperationException("请先选择要编辑的账号。");
            }

            string previousUid = SelectedAccount.Uid;
            string finalUid = uid == string.Empty ? previousUid : uid;
            string finalName = string.IsNullOrWhiteSpace(displayName)
                ? finalUid
                : $"{finalUid} ({displayName.Trim()})";
            GameServerRegion region = GameServerRegionResolver.Resolve(finalUid);
            GameAccount account = await updateGameAccount.ExecuteAsync(
                SelectedAccount.Id,
                finalUid,
                region,
                finalName);

            await LoadAccountsCoreAsync(account.Id);
            StatusMessage = $"已编辑账号 {previousUid}。";
        });
    }

    public async Task DeleteSelectedAccountAsync()
    {
        await ExecuteBusyAsync(async () =>
        {
            if (SelectedAccount is null)
            {
                throw new InvalidOperationException("请先选择要删除的账号。");
            }

            string uid = SelectedAccount.Uid;
            await deleteGameAccount.ExecuteAsync(SelectedAccount.Id);
            SelectedAccount = null;
            await LoadAccountsCoreAsync();
            StatusMessage = $"已删除账号 {uid}。";
        });
    }

    partial void OnNewAccountUidChanged(string value)
    {
        SelectedServerRegion = GameServerRegionResolver.Resolve(value);
    }

    private bool CanRun() => !IsBusy;

    private async Task LoadArchivesCoreAsync(Guid? selectedArchiveId = null)
    {
        IReadOnlyList<PlayerArchive> archives =
            await getPlayerArchives.ExecuteAsync();

        Archives.Clear();
        foreach (PlayerArchive archive in archives)
        {
            Archives.Add(archive);
        }

        SelectedArchive = selectedArchiveId is null
            ? Archives.FirstOrDefault()
            : Archives.FirstOrDefault(archive => archive.Id == selectedArchiveId) ??
              Archives.FirstOrDefault();
    }

    private async Task LoadAccountsCoreAsync(Guid? selectedAccountId = null)
    {
        Accounts.Clear();
        SelectedAccount = null;
        if (SelectedArchive is null)
        {
            return;
        }

        IReadOnlyList<GameAccount> accounts =
            await getGameAccounts.ExecuteAsync(SelectedArchive.Id);
        foreach (GameAccount account in accounts)
        {
            Accounts.Add(account);
        }

        SelectedAccount = selectedAccountId is null
            ? Accounts.FirstOrDefault()
            : Accounts.FirstOrDefault(account => account.Id == selectedAccountId) ??
              Accounts.FirstOrDefault();
    }

    private async Task ExecuteBusyAsync(Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await operation();
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "操作已取消。";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}

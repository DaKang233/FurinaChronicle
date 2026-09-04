using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App.ViewModels;

public partial class UserPageViewModel(
    IPassportAccountStore accountStore,
    IPassportSelectionStore selectionStore,
    IMiHoYoAccountProfileClient profileClient)
    : ObservableObject
{
    private bool initialized;
    private int selectionVersion;

    public ObservableCollection<PassportAccountListItem> Accounts { get; } = [];

    public ObservableCollection<PassportGameRole> Roles { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedAccount))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedAccount))]
    public partial PassportAccountListItem? SelectedAccount { get; set; }

    [ObservableProperty]
    public partial PassportGameRole? SelectedRole { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    [NotifyPropertyChangedFor(nameof(CanDeleteSelectedAccount))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "尚未登录通行证账号。";

    [ObservableProperty]
    public partial string? ErrorMessage { get; set; }

    public bool IsNotBusy => !IsBusy;

    public bool HasSelectedAccount => SelectedAccount is not null;

    public bool CanDeleteSelectedAccount => HasSelectedAccount && IsNotBusy;

    public async Task InitializeAsync()
    {
        if (initialized)
        {
            return;
        }

        await ReloadAsync();
        initialized = true;
    }

    public async Task ReloadAsync(Guid? preferredAccountId = null)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            PassportSelection? savedSelection = await selectionStore.LoadAsync();
            Guid? targetId = preferredAccountId
                ?? SelectedAccount?.Id
                ?? savedSelection?.PassportAccountId;
            IReadOnlyList<PassportAccount> storedAccounts =
                await accountStore.GetAllAsync();

            Accounts.Clear();
            foreach (PassportAccount account in storedAccounts)
            {
                Accounts.Add(new PassportAccountListItem(account));
            }

            PassportAccountListItem? target = targetId is Guid id
                ? Accounts.FirstOrDefault(item => item.Id == id)
                : null;
            target ??= Accounts.FirstOrDefault();
            if (target is null)
            {
                SelectedAccount = null;
                SelectedRole = null;
                Roles.Clear();
                await selectionStore.ClearAsync();
                StatusMessage = "尚未登录通行证账号。";
                return;
            }

            await SelectAccountCoreAsync(
                target,
                savedSelection?.PassportAccountId == target.Id
                    ? savedSelection.GameUid
                    : null);
            _ = LoadRemainingProfilesAsync(target.Id);
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

    public async Task SelectAccountAsync(PassportAccountListItem? account)
    {
        if (account is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            await SelectAccountCoreAsync(account, preferredGameUid: null);
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

    public async Task SelectRoleAsync(PassportGameRole? role)
    {
        PassportAccountListItem? account = SelectedAccount;
        if (account is null || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            SelectedRole = role;
            await selectionStore.SaveAsync(new PassportSelection(
                account.Id,
                role?.Uid));
        }
        catch (Exception exception)
        {
            ErrorMessage = $"角色选择保存失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedAccount is null || IsBusy)
        {
            return;
        }

        bool deleted = false;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            Guid deletedId = SelectedAccount.Id;
            await accountStore.DeleteAsync(deletedId);
            SelectedAccount = null;
            SelectedRole = null;
            Roles.Clear();
            deleted = true;
        }
        catch (Exception exception)
        {
            ErrorMessage = $"账号删除失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        if (deleted)
        {
            await ReloadAsync();
        }
    }

    private async Task SelectAccountCoreAsync(
        PassportAccountListItem account,
        string? preferredGameUid)
    {
        int version = Interlocked.Increment(ref selectionVersion);
        SelectedAccount = account;
        SelectedRole = null;
        Roles.Clear();
        StatusMessage = $"正在加载 {account.DisplayName}…";
        string? selectionUid = preferredGameUid;

        try
        {
            PassportAccountProfile profile =
                await profileClient.GetAsync(account.Account);
            if (version != selectionVersion)
            {
                return;
            }

            account.DisplayName = profile.DisplayName;
            account.AvatarUrl = profile.AvatarUrl;
            foreach (PassportGameRole role in profile.Roles
				.Where(role => role.GameBiz is "hk4e_cn" or "hk4e_global"))
            {
                Roles.Add(role);
            }

            SelectedRole = !string.IsNullOrWhiteSpace(preferredGameUid)
                ? Roles.FirstOrDefault(role => role.Uid == preferredGameUid)
                : null;
            SelectedRole ??= Roles.FirstOrDefault();
            selectionUid = SelectedRole?.Uid;
            StatusMessage = Roles.Count == 0
                ? "当前通行证账号没有绑定原神角色。"
                : $"已选择 {account.DisplayName}。";
        }
        catch (Exception exception)
        {
            if (version != selectionVersion)
            {
                return;
            }

            ErrorMessage = $"账号资料加载失败：{exception.Message}";
            StatusMessage = $"已选择 AID {account.Aid}，资料暂不可用。";
        }

        await selectionStore.SaveAsync(new PassportSelection(
            account.Id,
            selectionUid));
    }

    private async Task LoadRemainingProfilesAsync(Guid selectedId)
    {
        foreach (PassportAccountListItem item in Accounts.Where(item => item.Id != selectedId))
        {
            try
            {
                PassportAccountProfile profile = await profileClient.GetAsync(item.Account);
                item.DisplayName = profile.DisplayName;
                item.AvatarUrl = profile.AvatarUrl;
            }
            catch
            {
                // Each row keeps its AID fallback; one profile failure does not
                // prevent selecting or deleting other stored accounts.
            }
        }
    }
}

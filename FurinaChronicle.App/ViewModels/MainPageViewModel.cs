using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.App.Demo;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Wishes;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Wishes;
using FurinaChronicle.Services.Wishes.Importing;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public partial class MainPageViewModel(
	GetRecentWishRecords getRecentWishRecords,
	ImportWishRecords importWishRecords,
	CreatePlayerArchive createPlayerArchive,
	GetPlayerArchives getPlayerArchives,
	GetGameAccounts getGameAccounts,
	AddGameAccount addGameAccount,
	ArchiveSelectionService archiveSelectionService,
	UpdateGameAccount updateGameAccount,
	RenamePlayerArchive renamePlayerArchive,
	DeleteGameAccount deleteGameAccount,
	DeletePlayerArchive deletePlayerArchive)
	: ObservableObject
{
	private bool initialized;

	public ObservableCollection<PlayerArchive> Archives { get; } = [];

	public ObservableCollection<GameAccount> Accounts { get; } = [];

	public ObservableCollection<WishRecord> WishRecords { get; } = [];

	public IReadOnlyList<GameServerRegion> ServerRegions { get; } = Enum.GetValues<GameServerRegion>().Where(region => region != GameServerRegion.Unknown).ToArray();

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ReloadArchivesCommand))]
	[NotifyCanExecuteChangedFor(nameof(ReloadSelectedArchiveCommand))]
	[NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
	[NotifyCanExecuteChangedFor(nameof(ImportIntoArchiveCommand))]
	[NotifyCanExecuteChangedFor(nameof(ImportIntoSelectedAccountCommand))]
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
	public partial GameServerRegion SelectedServerRegion { get; set; } = GameServerRegion.Unknown;

	[ObservableProperty]
	public partial string? NewAccountDisplayName { get; set; }

	[ObservableProperty]
	public partial string? ErrorMessage { get; set; }

	[ObservableProperty]
	public partial string? ImportSummary { get; set; }

	[ObservableProperty]
	public partial string StatusMessage { get; set; } = "请先选择档案。";

	public async Task InitializeAsync()
	{
		if (initialized)
		{
			return;
		}

		await ExecuteBusyAsync(async () =>
		{
			await LoadArchivesCoreAsync();
			initialized = true;
		});
	}

	public async Task CreateArchiveAsync(string name)
	{
		await ExecuteBusyAsync(async () =>
		{
			PlayerArchive archive =
				await createPlayerArchive.ExecuteAsync(name);

			await LoadArchivesCoreAsync(archive.Id);
			await LoadSelectedArchiveCoreAsync();
			StatusMessage = $"已创建并选择档案:{archive.Name}。";
		});
	}

	public async Task SelectArchiveAsync(PlayerArchive? archive)
	{
		await ExecuteBusyAsync(async () =>
		{
			SelectedArchive = archive;
			await LoadSelectedArchiveCoreAsync();
		});
	}

	public async Task SelectAccountAsync(GameAccount? account)
	{
		await ExecuteBusyAsync(async () =>
		{
			if (account is null)
			{
				SelectedAccount = null;
				WishRecords.Clear();
				StatusMessage = "当前档案尚未选择账号。";
				return;
			}

			if (SelectedArchive is null || account.PlayerArchiveId != SelectedArchive.Id)
			{
				throw new InvalidOperationException("所选账号不属于当前档案。");
			}

			SelectedAccount = account;
			await archiveSelectionService.SelectAsync(account.Id);
			await ReloadRecordsCoreAsync();
			StatusMessage = $"已选择账号 {account.Uid}。";
		});
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task ReloadArchivesAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			Guid? selectedArchiveId = SelectedArchive?.Id;
			await LoadArchivesCoreAsync(selectedArchiveId);

			if (SelectedArchive is not null)
			{
				await LoadSelectedArchiveCoreAsync();
			}
		});
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task ReloadSelectedArchiveAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null)
			{
				throw new InvalidOperationException("请先选择要重新加载的档案。");
			}

			await LoadSelectedArchiveCoreAsync();
		});
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task CreateAccountAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null)
			{
				throw new InvalidOperationException("请先选择要添加账号的档案。");
			}
			string name;
			if (NewAccountDisplayName is null) name = NewAccountUid;
			else name = $"{NewAccountUid} ({NewAccountDisplayName})";

			GameAccount account = await addGameAccount.ExecuteAsync(
				SelectedArchive.Id,
				NewAccountUid,
				SelectedServerRegion,
				name);

			await RefreshAccountsCoreAsync();
			SelectedAccount =
				Accounts.First(candidate => candidate.Id == account.Id);
			await archiveSelectionService.SelectAsync(account.Id);
			await ReloadRecordsCoreAsync();

			NewAccountUid = string.Empty;
			SelectedServerRegion = GameServerRegion.Unknown;
			NewAccountDisplayName = null;
			StatusMessage = $"已创建账号 {account.Uid}。";
		});
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task ImportIntoArchiveAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			PlayerArchive archive = await EnsureImportArchiveAsync();
			await RefreshAccountsCoreAsync();

			GameAccount? account = Accounts.FirstOrDefault(
				candidate => candidate.Uid == SampleWishData.Uid);

			if (account is null)
			{
				GameServerRegion region =
					GameServerRegionResolver.Resolve(SampleWishData.Uid);
				if (region == GameServerRegion.Unknown)
				{
					throw new InvalidOperationException("无法根据导入 UID 推断服务器区域。");
				}

				account = await addGameAccount.ExecuteAsync(
					archive.Id,
					SampleWishData.Uid,
					region,
					SampleWishData.Uid + " (内置示例账号)");
			}

			await ImportIntoAccountCoreAsync(account);
			await RefreshAccountsCoreAsync();
			SelectedAccount = Accounts.First(candidate => candidate.Id == account.Id);
			await ReloadRecordsCoreAsync();
			StatusMessage = $"档案 {archive.Name} 已完成导入。";
		});
	}

	[RelayCommand(CanExecute = nameof(CanRun))]
	private async Task ImportIntoSelectedAccountAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedAccount is null)
			{
				throw new InvalidOperationException("请先选择要导入的账号。");
			}

			if (!string.Equals(SelectedAccount.Uid, SampleWishData.Uid, StringComparison.Ordinal))
			{
				ImportSummary =
					$"内置数据 UID {SampleWishData.Uid} " +
					$"与当前账号 UID {SelectedAccount.Uid} 不一致，" +
					$"已忽略 {SampleWishData.SourceRecordCount} 条记录。";
				return;
			}

			await ImportIntoAccountCoreAsync(SelectedAccount);
			await ReloadRecordsCoreAsync();
		});
	}

	public async Task EditSelectedAccountAsync(string displayName, string uid)
	{
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedAccount is null) throw new InvalidOperationException("请先选择要编辑的账号。");
			var previousUid = SelectedAccount.Uid;
			var finalUid = uid == string.Empty ? previousUid : uid;
			string finalName = $"{finalUid} ({displayName})";
			if (string.IsNullOrEmpty(displayName)) finalName = finalUid;
			GameServerRegion gameServerRegion = GameServerRegionResolver.Resolve(finalUid);
			GameAccount updated = await updateGameAccount.ExecuteAsync(
				SelectedAccount.Id,
				finalUid,
				gameServerRegion,
				finalName);

			await RefreshAccountsCoreAsync();

			StatusMessage = $"账号“{previousUid}”已编辑。";
		});
	}

	public async Task RenameSelectedArchiveAsync(string name)
	{
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null) throw new InvalidOperationException("请先选择要重命名的档案。");
			PlayerArchive updated = await renamePlayerArchive.ExecuteAsync(
				SelectedArchive.Id,
				name);

			await LoadArchivesCoreAsync(updated.Id);
			StatusMessage = $"已将档案重命名为“{updated.Name}”。";
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

			Guid deletedArchiveId = SelectedArchive.Id;
			string deletedArchiveName = SelectedArchive.Name;

			await deletePlayerArchive.ExecuteAsync(deletedArchiveId);

			SelectedArchive = null;
			SelectedAccount = null;
			Accounts.Clear();
			WishRecords.Clear();

			await LoadArchivesCoreAsync();
			SelectedArchive = Archives.FirstOrDefault();

			if (SelectedArchive is not null)
			{
				await LoadSelectedArchiveCoreAsync();
			}

			StatusMessage =
				$"已删除档案“{deletedArchiveName}”。";
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
			Guid deletedAccountId = SelectedAccount.Id;
			string deletedAccountUid = SelectedAccount.Uid;
			await deleteGameAccount.ExecuteAsync(deletedAccountId);
			SelectedAccount = null;
			WishRecords.Clear();
			await RefreshAccountsCoreAsync();
			SelectedAccount = Accounts.FirstOrDefault();

			if (SelectedAccount is not null)
			{
				await archiveSelectionService.SelectAsync(SelectedAccount.Id);

				await ReloadRecordsCoreAsync();
			}
			StatusMessage = $"已删除账号 {deletedAccountUid}。";
		});
	}

	partial void OnNewAccountUidChanged(string value)
	{
		SelectedServerRegion = GameServerRegionResolver.Resolve(value);
	}

	private bool CanRun() => !IsBusy;

	private async Task<PlayerArchive> EnsureImportArchiveAsync()
	{
		if (SelectedArchive is not null)
		{
			return SelectedArchive;
		}

		PlayerArchive archive = await createPlayerArchive.ExecuteAsync(GetNextUnnamedArchiveName());
		await LoadArchivesCoreAsync(archive.Id);
		return archive;
	}

	private string GetNextUnnamedArchiveName()
	{
		int suffix = 1;
		while (Archives.Any(archive => archive.Name == $"未命名档案{suffix}"))
		{
			suffix++;
		}

		return $"未命名档案{suffix}";
	}

	private async Task ImportIntoAccountCoreAsync(GameAccount account)
	{
		await archiveSelectionService.SelectAsync(account.Id);
		SelectedAccount = account;

		await using Stream stream = SampleWishData.OpenStream();
		WishImportResult result = await importWishRecords.ExecuteAsync(
			stream,
			account.Id);

		ImportSummary =
			$"共读取 {result.TotalCount} 条，" +
			$"导入 {result.ImportedCount} 条，" +
			$"重复 {result.DuplicateCount} 条，" +
			$"无效 {result.InvalidCount} 条。";
	}

	private async Task LoadArchivesCoreAsync(Guid? selectedArchiveId = null)
	{
		IReadOnlyList<PlayerArchive> archives =
			await getPlayerArchives.ExecuteAsync();

		Archives.Clear();
		foreach (PlayerArchive archive in archives)
		{
			Archives.Add(archive);
		}

		Guid? targetId = selectedArchiveId ?? SelectedArchive?.Id;
		SelectedArchive = targetId is null
			? null
			: Archives.FirstOrDefault(archive => archive.Id == targetId);

		if (SelectedArchive is null)
		{
			Accounts.Clear();
			SelectedAccount = null;
			WishRecords.Clear();
			StatusMessage = "请先选择档案。";
		}
	}

	private async Task LoadSelectedArchiveCoreAsync()
	{
		Accounts.Clear();
		SelectedAccount = null;
		WishRecords.Clear();

		if (SelectedArchive is null)
		{
			StatusMessage = "请先选择档案。";
			return;
		}

		await RefreshAccountsCoreAsync();

		ArchiveSelection? selection =
			await archiveSelectionService.GetForArchiveAsync(
				SelectedArchive.Id);

		if (selection is null)
		{
			StatusMessage =
				$"档案“{SelectedArchive.Name}”尚无账号。";
			return;
		}

		SelectedAccount = Accounts.FirstOrDefault(
			account => account.Id == selection.GameAccountId);

		if (SelectedAccount is not null)
		{
			await ReloadRecordsCoreAsync();
			StatusMessage =
				$"已加载档案“{SelectedArchive.Name}”，" +
				$"恢复账号 {SelectedAccount.Uid}。";
		}
	}

	private async Task RefreshAccountsCoreAsync()
	{
		if (SelectedArchive is null)
		{
			Accounts.Clear();
			SelectedAccount = null;
			return;
		}

		Guid? selectedAccountId = SelectedAccount?.Id;
		IReadOnlyList<GameAccount> accounts =
			await getGameAccounts.ExecuteAsync(SelectedArchive.Id);

		Accounts.Clear();
		foreach (GameAccount account in accounts)
		{
			Accounts.Add(account);
		}

		SelectedAccount = selectedAccountId is null
			? null
			: Accounts.FirstOrDefault(
				account => account.Id == selectedAccountId);
	}

	private async Task ReloadRecordsCoreAsync()
	{
		WishRecords.Clear();

		if (SelectedAccount is null)
		{
			return;
		}

		IReadOnlyList<WishRecord> records =
			await getRecentWishRecords.ExecuteAsync(
				SelectedAccount.Id,
				count: 20);

		foreach (WishRecord record in records)
		{
			WishRecords.Add(record);
		}
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

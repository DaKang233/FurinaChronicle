using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Wishes;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public partial class MainPageViewModel(
	GetWishRecordPage getWishRecordPage,
	ImportUigfGachaRecords importUigfGachaRecords,
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

	public ObservableCollection<WishRecordDisplayItem> WishRecords { get; } = [];

	public IReadOnlyList<GameServerRegion> ServerRegions { get; } = Enum.GetValues<GameServerRegion>().Where(region => region != GameServerRegion.Unknown).ToArray();

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ReloadArchivesCommand))]
	[NotifyCanExecuteChangedFor(nameof(ReloadSelectedArchiveCommand))]
	[NotifyCanExecuteChangedFor(nameof(CreateAccountCommand))]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(IsNotBusy))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
	public partial bool IsBusy { get; set; }
	public bool IsNotBusy => !IsBusy;

	[ObservableProperty]
	public partial PlayerArchive? SelectedArchive { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
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
	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(PageSummary))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
	public partial int CurrentPage { get; set; } = 1;

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(PageSummary))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
	public partial int TotalPages { get; set; } = 1;

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(PageSummary))]
	public partial int TotalRecordCount { get; set; }

	public string PageSummary =>
		$"第 {CurrentPage} / {TotalPages} 页，共 {TotalRecordCount} 条";

	public bool CanGoToPreviousPage =>
		!IsBusy && SelectedAccount is not null && CurrentPage > 1;

	public bool CanGoToNextPage =>
		!IsBusy && SelectedAccount is not null && CurrentPage < TotalPages;


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
				ResetPagination();
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

	public async Task ImportUigfIntoArchiveAsync(
		Stream source,
		string fileName)
	{
		ArgumentNullException.ThrowIfNull(source);

		await ExecuteBusyAsync(async () =>
		{
			PlayerArchive archive = await EnsureImportArchiveAsync();
			GachaImportResult result =
				await importUigfGachaRecords.ExecuteAsync(
					source,
					archive.Id);

			await RefreshAccountsCoreAsync();
			SelectedAccount ??= Accounts.FirstOrDefault();

			if (SelectedAccount is not null)
			{
				await archiveSelectionService.SelectAsync(
					SelectedAccount.Id);
				await ReloadRecordsCoreAsync(pageNumber: 1);
			}

			ImportSummary = FormatImportSummary(
				fileName,
				result);
			StatusMessage =
				$"已将 UIGF 文件导入档案“{archive.Name}”。";
		});
	}

	public async Task ImportUigfIntoSelectedAccountAsync(
		Stream source,
		string fileName)
	{
		ArgumentNullException.ThrowIfNull(source);

		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null ||
				SelectedAccount is null)
			{
				throw new InvalidOperationException(
					"请先选择要导入的档案和账号。");
			}

			Guid archiveId = SelectedArchive.Id;
			Guid accountId = SelectedAccount.Id;

			GachaImportResult result =
				await importUigfGachaRecords.ExecuteAsync(
					source,
					archiveId,
					accountId);

			await ReloadRecordsCoreAsync(pageNumber: 1);
			ImportSummary = FormatImportSummary(
				fileName,
				result);
			StatusMessage =
				$"已将 UIGF 文件导入账号 {SelectedAccount.Uid}。";
		});
	}

	private static string FormatImportSummary(
		string fileName,
		GachaImportResult result)
	{
		return $"{fileName}：共读取 {result.TotalCount} 条，" +
			$"导入 {result.ImportedCount} 条，" +
			$"重复 {result.DuplicateCount} 条，" +
			$"无效 {result.InvalidCount} 条，" +
			$"忽略 {result.IgnoredCount} 条，" +
			$"新建账号 {result.CreatedAccountCount} 个。";
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
			ResetPagination();

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
			ResetPagination();
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
			ResetPagination();
			StatusMessage = "请先选择档案。";
		}
	}

	private async Task LoadSelectedArchiveCoreAsync()
	{
		Accounts.Clear();
		SelectedAccount = null;
		WishRecords.Clear();
		ResetPagination();

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
			WishRecords.Clear();
			ResetPagination();
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

	[RelayCommand(CanExecute = nameof(CanMoveToPreviousPage))]
	private async Task PreviousPageAsync()
	{
		await ExecuteBusyAsync(() =>
			ReloadRecordsCoreAsync(CurrentPage - 1));
	}

	[RelayCommand(CanExecute = nameof(CanMoveToNextPage))]
	private async Task NextPageAsync()
	{
		await ExecuteBusyAsync(() =>
			ReloadRecordsCoreAsync(CurrentPage + 1));
	}

	private bool CanMoveToPreviousPage() =>
		CanGoToPreviousPage;

	private bool CanMoveToNextPage() =>
		CanGoToNextPage;

	private async Task ReloadRecordsCoreAsync(
		int pageNumber = 1)
	{
		WishRecords.Clear();

		if (SelectedAccount is null)
		{
			ResetPagination();
			return;
		}

		WishRecordPage page =
			await getWishRecordPage.ExecuteAsync(
				SelectedAccount.Id,
				pageNumber,
				pageSize: 50);

		CurrentPage = page.PageNumber;
		TotalPages = page.TotalPages;
		TotalRecordCount = page.TotalCount;

		foreach (var record in page.Records)
		{
			WishRecords.Add(
				WishRecordDisplayItem.FromDomain(record));
		}
	}

	private void ResetPagination()
	{
		CurrentPage = 1;
		TotalPages = 1;
		TotalRecordCount = 0;
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

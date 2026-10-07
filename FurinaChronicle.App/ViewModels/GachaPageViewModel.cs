// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.App.Exporting;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Services.Gacha;
using System.Collections.ObjectModel;

namespace FurinaChronicle.App.ViewModels;

public partial class GachaPageViewModel(
	GetGachaRecordPage getGachaRecordPage,
	GachaAnalysisViewModel analysis,
	ImportUigfGachaRecords importUigfGachaRecords,
	ExportUigfV42GachaRecords exportUigfV42GachaRecords,
	ExportGachaTable exportGachaTable,
	CreatePlayerArchive createPlayerArchive,
	GetPlayerArchives getPlayerArchives,
	GetGameAccounts getGameAccounts,
	AddGameAccount addGameAccount,
	ArchiveSelectionService archiveSelectionService,
	DeleteGameAccount deleteGameAccount,
	DeletePlayerArchive deletePlayerArchive,
	RefreshGachaRecords refreshGachaRecords,
	IPassportSelectionStore passportSelectionStore,
	TeyvatHelperUigfImportSource teyvatHelperUigfImportSource)
	: ObservableObject
{
	public GachaAnalysisViewModel Analysis { get; } = analysis;

	public ObservableCollection<PlayerArchive> Archives { get; } = [];

	public ObservableCollection<GameAccount> Accounts { get; } = [];

	public ObservableCollection<GachaRecordDisplayItem> GachaRecords { get; } = [];

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(ReloadArchivesCommand))]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(IsNotBusy))]
	[NotifyPropertyChangedFor(nameof(CanAutomaticallyImportFromTeyvatHelper))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
	public partial bool IsBusy { get; set; }
	public bool IsNotBusy => !IsBusy;

	[ObservableProperty]
	public partial string? SelectedPassportRoleUid { get; set; }

	[ObservableProperty]
	[NotifyPropertyChangedFor(nameof(CanAutomaticallyImportFromTeyvatHelper))]
	public partial bool HasAutomaticTeyvatHelperImportContext { get; set; }

	public bool CanAutomaticallyImportFromTeyvatHelper =>
		IsNotBusy && HasAutomaticTeyvatHelperImportContext;

	[ObservableProperty]
	public partial PlayerArchive? SelectedArchive { get; set; }

	[ObservableProperty]
	[NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
	[NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
	[NotifyPropertyChangedFor(nameof(CanGoToPreviousPage))]
	[NotifyPropertyChangedFor(nameof(CanGoToNextPage))]
	public partial GameAccount? SelectedAccount { get; set; }

	[ObservableProperty]
	public partial string? ErrorMessage { get; set; }

	[ObservableProperty]
	public partial string? ImportSummary { get; set; }

	[ObservableProperty]
	public partial string? ExportSummary { get; set; }

	[ObservableProperty]
	public partial string? RefreshSummary { get; set; }

	[ObservableProperty]
	public partial bool IsFullRefresh { get; set; }

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
		Guid? selectedArchiveId = SelectedArchive?.Id;
		Guid? selectedAccountId = SelectedAccount?.Id;
		await ExecuteBusyAsync(async () =>
		{
			if (selectedArchiveId is null)
			{
				PlayerArchive? currentArchive =
					await archiveSelectionService.GetCurrentArchiveAsync();
				selectedArchiveId = currentArchive?.Id;
			}

			await LoadArchivesCoreAsync(selectedArchiveId);
			if (SelectedArchive is null && selectedArchiveId is not null)
			{
				PlayerArchive? repairedArchive =
					await archiveSelectionService.GetCurrentArchiveAsync();
				await LoadArchivesCoreAsync(repairedArchive?.Id);
			}

			if (SelectedArchive is not null)
			{
				await LoadSelectedArchiveCoreAsync(selectedAccountId);
			}
			await RefreshPassportSelectionCoreAsync();
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
				GachaRecords.Clear();
				ResetPagination();
				await Analysis.SetContextAsync(
					SelectedArchive,
					Accounts,
					SelectedAccount);
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
			Guid? selectedAccountId = SelectedAccount?.Id;
			await LoadArchivesCoreAsync(selectedArchiveId);

			if (SelectedArchive is not null)
			{
				await LoadSelectedArchiveCoreAsync(selectedAccountId);
			}
		});
	}

	public async Task ImportUigfIntoArchiveAsync(
		Stream source,
		string fileName)
	{
		ArgumentNullException.ThrowIfNull(source);

		await ExecuteBusyAsync(() =>
			ImportUigfIntoArchiveCoreAsync(source, fileName));
	}

	public async Task ImportFromTeyvatHelperAutomaticallyAsync()
	{
		await ExecuteBusyAsync(async () =>
		{
			TeyvatHelperUigfDownload download =
				await teyvatHelperUigfImportSource
					.DownloadForSelectedRoleAsync();
			await using var source = new MemoryStream(
				download.Content,
				writable: false);
			await ImportUigfIntoArchiveCoreAsync(
				source,
				download.FileName);
		});
	}

	public async Task ImportFromTeyvatHelperManuallyAsync(
		string uid,
		string gachaUrl)
	{
		await ExecuteBusyAsync(async () =>
		{
			TeyvatHelperUigfDownload download =
				await teyvatHelperUigfImportSource.DownloadManuallyAsync(
					uid,
					gachaUrl);
			await using var source = new MemoryStream(
				download.Content,
				writable: false);
			await ImportUigfIntoArchiveCoreAsync(
				source,
				download.FileName);
		});
	}

	private async Task ImportUigfIntoArchiveCoreAsync(
		Stream source,
		string fileName)
	{
		bool createdArchiveForImport = SelectedArchive is null;
		PlayerArchive archive = await EnsureImportArchiveAsync();
		GachaImportResult result;
		try
		{
			result = await importUigfGachaRecords.ExecuteAsync(
				source,
				archive.Id);
		}
		catch
		{
			if (createdArchiveForImport)
			{
				await RemoveImportArchiveAsync(archive.Id);
			}

			throw;
		}

		ImportSummary = FormatImportSummary(
			fileName,
			result);

		if (createdArchiveForImport && result.ImportedCount == 0)
		{
			await RemoveImportArchiveAsync(archive.Id);
			StatusMessage =
				"UIGF 文件没有可导入的有效记录，未创建档案。";
			return;
		}

		await RefreshAccountsCoreAsync();
		SelectedAccount ??= Accounts.FirstOrDefault();

		if (SelectedAccount is not null)
		{
			await archiveSelectionService.SelectAsync(
				SelectedAccount.Id);
			await ReloadRecordsCoreAsync(pageNumber: 1);
		}

		StatusMessage =
			$"已将 UIGF 文件导入档案“{archive.Name}”。";
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

	public async Task<GachaExportFile?> CreateUigfExportAsync(
		IReadOnlyCollection<Guid> gameAccountIds,
		UigfV42ExportOptions options)
	{
		GachaExportFile? file = null;
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null)
			{
				throw new InvalidOperationException(
					"请先选择要导出的档案。");
			}

			var content = new MemoryStream();
			try
			{
				GachaExportResult result =
					await exportUigfV42GachaRecords.ExecuteAsync(
						content,
						SelectedArchive.Id,
						gameAccountIds,
						options);
				content.Position = 0;
				file = new GachaExportFile(
					$"FurinaChronicle_UIGF_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
					content,
					result);
				ExportSummary =
					$"已生成 UIGF 4.2：{result.AccountCount} 个账号，" +
					$"{result.RecordCount} 条记录。";
				StatusMessage = "UIGF 导出文件已生成，请选择保存位置。";
			}
			catch
			{
				content.Dispose();
				throw;
			}
		});
		return file;
	}

	public async Task<GachaExportFile?> CreateTableExportAsync(
		IReadOnlyCollection<Guid> gameAccountIds,
		GachaTableExportOptions options)
	{
		GachaExportFile? file = null;
		await ExecuteBusyAsync(async () =>
		{
			if (SelectedArchive is null)
			{
				throw new InvalidOperationException(
					"请先选择要导出的档案。");
			}

			var content = new MemoryStream();
			try
			{
				GachaExportResult result =
					await exportGachaTable.ExecuteAsync(
						content,
						SelectedArchive.Id,
						gameAccountIds,
						options);
				content.Position = 0;
				string extension =
					options.Format == GachaTableFormat.Csv
						? "csv"
						: "xlsx";
				file = new GachaExportFile(
					$"FurinaChronicle_Gacha_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.{extension}",
					content,
					result);
				ExportSummary =
					$"已生成 {extension.ToUpperInvariant()} 表格：" +
					$"{result.AccountCount} 个账号，" +
					$"{result.RecordCount} 条记录。";
				StatusMessage = "表格导出文件已生成，请选择保存位置。";
			}
			catch
			{
				content.Dispose();
				throw;
			}
		});
		return file;
	}

	public void NotifyExportSaved(string? filePath)
	{
		StatusMessage = string.IsNullOrWhiteSpace(filePath)
			? "导出文件已保存。"
			: $"导出文件已保存到 {filePath}。";
	}

	public async Task RefreshGachaAsync(
		GachaRefreshSource source,
		string? manualUrl = null,
		string? gameInstallationPath = null)
	{
		await ExecuteBusyAsync(async () =>
		{
			Guid? passportAccountId = null;
			PassportSelection? passportSelection = null;
			if (source == GachaRefreshSource.SToken)
			{
				passportSelection =
					await passportSelectionStore.LoadAsync();
				if (passportSelection is null ||
					string.IsNullOrWhiteSpace(passportSelection.GameUid))
				{
					throw new InvalidOperationException(
						"请先在用户页选择米哈游通行证账号和原神角色。");
				}
				passportAccountId = passportSelection.PassportAccountId;
			}

			bool createdArchive = false;
			Guid? createdAccountId = null;
			bool refreshRecordsCommitted = false;
			try
			{
				GameAccount targetAccount = SelectedAccount ??
					await EnsureRefreshAccountAsync(
						source,
						passportSelection,
						manualUrl,
						gameInstallationPath,
						created => createdArchive = created,
						created => createdAccountId = created);

				if (passportSelection is not null &&
					!string.Equals(
						passportSelection.GameUid,
						targetAccount.Uid,
						StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						$"用户页当前角色 UID {passportSelection.GameUid} 与抽卡页账号 UID {targetAccount.Uid} 不一致。");
				}

				GachaRefreshResult result = await refreshGachaRecords.ExecuteAsync(
					new GachaRefreshRequest(
						targetAccount,
						source,
						IsFullRefresh
							? GachaRefreshMode.Full
							: GachaRefreshMode.Incremental,
						passportAccountId,
						manualUrl,
						gameInstallationPath));
				refreshRecordsCommitted = true;

				await ReloadRecordsCoreAsync(pageNumber: 1);
				RefreshSummary =
					$"刷新完成：获取 {result.FetchedCount} 条，" +
					$"新增 {result.InsertedCount} 条，覆盖校正 {result.UpdatedCount} 条，" +
					$"保留重复 {result.DuplicateCount} 条，" +
					$"请求 {result.PageCount} 页。";
				StatusMessage = result.InsertedCount == 0 && result.UpdatedCount == 0
					? "没有发现新的抽卡记录。"
					: $"已新增 {result.InsertedCount} 条、覆盖校正 {result.UpdatedCount} 条抽卡记录。";
				if (createdArchive || createdAccountId is not null)
				{
					StatusMessage += $" 已自动创建并选择账号 {targetAccount.Uid}。";
				}
			}
			catch
			{
				if (!refreshRecordsCommitted)
				{
					await RollBackAutomaticRefreshTargetAsync(
						createdArchive,
						createdAccountId);
				}

				throw;
			}
		});
	}

	private async Task<GameAccount> EnsureRefreshAccountAsync(
		GachaRefreshSource source,
		PassportSelection? passportSelection,
		string? manualUrl,
		string? gameInstallationPath,
		Action<bool> setCreatedArchive,
		Action<Guid?> setCreatedAccountId)
	{
		GachaRefreshIdentity identity;
		if (source == GachaRefreshSource.SToken)
		{
			string uid = passportSelection?.GameUid
				?? throw new InvalidOperationException(
					"当前米哈游通行证账号没有选择原神角色。");
			GameServerRegion region = GameServerRegionResolver.Resolve(uid);
			if (region == GameServerRegion.Unknown)
			{
				throw new InvalidOperationException($"无法识别角色 UID {uid} 的服务器。");
			}
			identity = new GachaRefreshIdentity(uid, region);
		}
		else
		{
			identity = await refreshGachaRecords.DiscoverIdentityAsync(
				new GachaRefreshDiscoveryRequest(
					source,
					manualUrl,
					gameInstallationPath,
					GameServerRegion.Unknown));
		}

		bool createdArchive = SelectedArchive is null;
		PlayerArchive archive = await EnsureImportArchiveAsync();
		setCreatedArchive(createdArchive);
		IReadOnlyList<GameAccount> existingAccounts =
			await getGameAccounts.ExecuteAsync(archive.Id);
		GameAccount? account = existingAccounts.FirstOrDefault(candidate =>
			string.Equals(candidate.Uid, identity.Uid, StringComparison.Ordinal));
		if (account is null)
		{
			account = await addGameAccount.ExecuteAsync(
				archive.Id,
				identity.Uid,
				identity.ServerRegion,
				identity.Uid);
			setCreatedAccountId(account.Id);
		}

		await RefreshAccountsCoreAsync();
		SelectedAccount = Accounts.First(candidate => candidate.Id == account.Id);
		await archiveSelectionService.SelectAsync(SelectedAccount.Id);
		return SelectedAccount;
	}

	private async Task RollBackAutomaticRefreshTargetAsync(
		bool createdArchive,
		Guid? createdAccountId)
	{
		try
		{
			if (createdArchive && SelectedArchive is not null)
			{
				await RemoveImportArchiveAsync(SelectedArchive.Id);
				return;
			}

			if (createdAccountId is Guid accountId)
			{
				await deleteGameAccount.ExecuteAsync(accountId);
				SelectedAccount = null;
				await RefreshAccountsCoreAsync();
				SelectedAccount = Accounts.FirstOrDefault();
				if (SelectedAccount is not null)
				{
					await archiveSelectionService.SelectAsync(SelectedAccount.Id);
				}
			}
		}
		catch
		{
			// Preserve the original refresh error; cleanup can be retried by the user.
		}
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

	private bool CanRun() => !IsBusy;

	private async Task RefreshPassportSelectionCoreAsync()
	{
		TeyvatHelperImportAvailability availability =
			await teyvatHelperUigfImportSource.GetAvailabilityAsync();
		SelectedPassportRoleUid = availability.SelectedRoleUid;
		HasAutomaticTeyvatHelperImportContext =
			availability.CanAutomaticallyImport;
	}

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

	private async Task RemoveImportArchiveAsync(Guid archiveId)
	{
		await deletePlayerArchive.ExecuteAsync(archiveId);

		if (SelectedArchive?.Id == archiveId)
		{
			SelectedArchive = null;
		}

		SelectedAccount = null;
		Accounts.Clear();
		GachaRecords.Clear();
		ResetPagination();
		await LoadArchivesCoreAsync();
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
			GachaRecords.Clear();
			ResetPagination();
			await Analysis.SetContextAsync(null, [], null);
			StatusMessage = "请先选择档案。";
		}
	}

	private async Task LoadSelectedArchiveCoreAsync(
		Guid? preferredAccountId = null)
	{
		Accounts.Clear();
		SelectedAccount = null;
		GachaRecords.Clear();
		ResetPagination();

		if (SelectedArchive is null)
		{
			await Analysis.SetContextAsync(null, [], null);
			StatusMessage = "请先选择档案。";
			return;
		}

		await RefreshAccountsCoreAsync();
		SelectedAccount = preferredAccountId is null
			? null
			: Accounts.FirstOrDefault(
				account => account.Id == preferredAccountId);

		if (SelectedAccount is not null)
		{
			await ReloadRecordsCoreAsync();
			StatusMessage =
				$"已重新加载档案“{SelectedArchive.Name}”，" +
				$"保留账号 {SelectedAccount.Uid}。";
			return;
		}

		ArchiveSelection? selection =
			await archiveSelectionService.GetForArchiveAsync(
				SelectedArchive.Id);

		if (selection is null)
		{
			await Analysis.SetContextAsync(
				SelectedArchive,
				Accounts,
				SelectedAccount);
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
			GachaRecords.Clear();
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
		GachaRecords.Clear();
		await Analysis.SetContextAsync(
			SelectedArchive,
			Accounts,
			SelectedAccount);

		if (SelectedAccount is null)
		{
			ResetPagination();
			return;
		}

		GachaRecordPage page =
			await getGachaRecordPage.ExecuteAsync(
				SelectedAccount.Id,
				pageNumber,
				pageSize: 50);

		CurrentPage = page.PageNumber;
		TotalPages = page.TotalPages;
		TotalRecordCount = page.TotalCount;

		foreach (var record in page.Records)
		{
			GachaRecords.Add(
				GachaRecordDisplayItem.FromDomain(record));
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

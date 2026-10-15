// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FurinaChronicle.App.Exporting;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.Gacha;
using FurinaChronicle.Core.Gacha.Metadata;
using FurinaChronicle.Services.Archives;
using FurinaChronicle.Services.Gacha.Abstractions;
using FurinaChronicle.Services.Gacha.Exporting;
using FurinaChronicle.Services.Gacha.Importing;
using FurinaChronicle.Services.Gacha.Refreshing;
using FurinaChronicle.Services.Gacha.Portable;
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
	GetPlayerArchives getPlayerArchives,
	GetGameAccounts getGameAccounts,
	AddGameAccount addGameAccount,
	ArchiveSelectionService archiveSelectionService,
	RefreshGachaRecords refreshGachaRecords,
	IPassportSelectionStore passportSelectionStore,
	TeyvatHelperUigfImportSource teyvatHelperUigfImportSource,
	IGachaPortableInputStager portableInputStager,
	IGachaPortablePackageReader portablePackageReader,
	PlanGachaPortableImport planPortableImport,
	IGachaPortableImportApplier portableImportApplier,
	IGachaPortableExportFileService portableExportFileService,
	IGachaItemMetadataProvider refreshMetadataProvider,
	IGachaItemIconCache refreshIconCache)
	: ObservableObject
{
	private readonly Dictionary<string, GachaRefreshItemDisplayItem>
		refreshItemsByKey = new(StringComparer.Ordinal);

	public GachaAnalysisViewModel Analysis { get; } = analysis;

	public ObservableCollection<PlayerArchive> Archives { get; } = [];

	public ObservableCollection<GameAccount> Accounts { get; } = [];

	public ObservableCollection<GachaRecordDisplayItem> GachaRecords { get; } = [];

	public ObservableCollection<GachaRefreshItemDisplayItem> RefreshItems { get; } = [];

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
	public partial bool IsRefreshRunning { get; set; }

	[ObservableProperty]
	public partial string RefreshProgressTitle { get; set; } = "正在准备抽卡记录刷新";

	[ObservableProperty]
	public partial string RefreshCurrentPool { get; set; } = "正在准备";

	[ObservableProperty]
	public partial string RefreshProgressDetail { get; set; } = "正在验证刷新条件…";

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
	public partial string? PortableSummary { get; set; }

	[ObservableProperty]
	public partial string? RefreshSummary { get; set; }

	public async Task<GachaPortableImportSession> PreparePortableImportAsync(
		Stream source,
		Guid? targetArchiveId,
		bool createNewArchive,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		IGachaPortableStagedInput staged =
			await portableInputStager.StageAsync(source, cancellationToken);
		try
		{
			await using Stream stagedStream =
				await staged.OpenReadAsync(cancellationToken);
			GachaPortableReadResult read =
				await portablePackageReader.ReadAsync(
					stagedStream,
					cancellationToken);
			GachaPortableImportPlan plan =
				await planPortableImport.ExecuteAsync(
					new GachaPortableImportPlanRequest(
						read.Package,
						targetArchiveId,
						createNewArchive,
						Mode: IsFullImportMode
							? GachaPortableApplyMode.ReplaceExisting
							: GachaPortableApplyMode.AddOnly),
					cancellationToken);
			PortableSummary = FormatPortablePlan(plan, staged.Length);
			return new GachaPortableImportSession(
				staged,
				read.Package,
				plan,
				DateTimeOffset.Now);
		}
		catch
		{
			await staged.DisposeAsync();
			throw;
		}
	}

	public async Task ReplanPortableImportAsync(
		GachaPortableImportSession session,
		Guid? targetArchiveId,
		bool createNewArchive,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		session.Plan = await planPortableImport.ExecuteAsync(
			new GachaPortableImportPlanRequest(
				session.Package,
				targetArchiveId,
				createNewArchive,
				Mode: IsFullImportMode
					? GachaPortableApplyMode.ReplaceExisting
					: GachaPortableApplyMode.AddOnly),
			cancellationToken);
		PortableSummary = FormatPortablePlan(
			session.Plan,
			session.SourceLength);
	}

	public Task<GachaPortableApplyResult> ApplyPortableImportAsync(
		GachaPortableImportSession session,
		IReadOnlyList<
			FurinaChronicle.Services.Gacha.History.TombstoneReintroductionConfirmation>?
			reintroductionConfirmations = null,
		Guid? cleanupConfirmationId = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(session);
		return portableImportApplier.ApplyAsync(
			new GachaPortableApplyRequest(
				session.Package,
				session.Plan,
				session.OperationId,
				session.ReceivedAt,
				session.ReceiptBatchId,
				reintroductionConfirmations,
				cleanupConfirmationId),
			cancellationToken);
	}

	public async Task<GachaPortableExportArtifact>
		CreatePortableArchiveExportAsync(
			CancellationToken cancellationToken = default)
	{
		PlayerArchive selectedArchive = SelectedArchive ??
			throw new InvalidOperationException("请先选择要导出的档案。");
		if (Accounts.Count == 0)
		{
			throw new InvalidOperationException("所选档案没有可导出的游戏账号。");
		}
		string directory = Path.Combine(
			FileSystem.CacheDirectory,
			"gacha-portable-export");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(
			directory,
			$"furina-gacha-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");
		try
		{
			GachaPortableWriteResult result =
				await portableExportFileService.ExportAsync(
					path,
					selectedArchive.Id,
					Accounts.Select(account => account.Id).ToArray(),
					cancellationToken);
			PortableSummary =
				$"Portable 导出已生成：{result.AccountCount} 个账号、" +
				$"{result.RecordCount} 条记录；等待选择保存位置。";
			return new GachaPortableExportArtifact(path, result);
		}
		catch
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
			throw;
		}
	}

	private static string FormatPortablePlan(
		GachaPortableImportPlan plan,
		long sourceLength)
	{
		string target = plan.Archive.Kind switch
		{
			GachaPortableArchivePlanKind.MapExisting =>
				$"映射到“{plan.Archive.ProposedName}”",
			GachaPortableArchivePlanKind.CreateNew =>
				$"新建“{plan.Archive.ProposedName}”",
			_ => "需要手动选择目标档案"
		};
		string mode = plan.Mode == GachaPortableApplyMode.ReplaceExisting
			? "全量覆盖"
			: "仅补充";
		return $"Portable 预览（{sourceLength} 字节，{mode}）：{target}；" +
			$"新建账号 {plan.Preview.AccountCreateCount}，" +
			$"复用账号 {plan.Preview.AccountReuseCount}，" +
			$"新增 {plan.Preview.AddCount}，覆盖 {plan.Preview.ReplaceCount}，" +
			$"保留 {plan.Preview.SkipCount}，" +
			$"冲突 {plan.Preview.ConflictCount}，别名 {plan.Preview.AliasCount}。";
	}

	[ObservableProperty]
	public partial bool IsFullImportMode { get; set; }

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
		PlayerArchive archive = SelectedArchive ?? PrepareUnnamedArchive();
		GachaImportResult result =
			createdArchiveForImport
				? await importUigfGachaRecords.ExecuteAsync(
					source,
					archive,
					CurrentImportMode)
				: await importUigfGachaRecords.ExecuteAsync(
					source,
					archive.Id,
					targetGameAccountId: null,
					mode: CurrentImportMode);

		ImportSummary = FormatImportSummary(
			fileName,
			result);

		if (createdArchiveForImport && result.ImportedCount == 0)
		{
			StatusMessage =
				"UIGF 文件没有可导入的有效记录；未创建空档案或账号。";
			return;
		}
		if (createdArchiveForImport)
		{
			await LoadArchivesCoreAsync(archive.Id);
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
					accountId,
					CurrentImportMode);

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
		if (IsBusy)
		{
			return;
		}

		BeginRefreshProgress(source);
		try
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

				RefreshTargetPlan targetPlan = SelectedAccount is not null
					? new RefreshTargetPlan(SelectedAccount, null, null)
					: await PrepareRefreshTargetAsync(
							source,
							passportSelection,
							manualUrl,
							gameInstallationPath);
				GameAccount targetAccount = targetPlan.Account;

				if (passportSelection is not null &&
					!string.Equals(
						passportSelection.GameUid,
						targetAccount.Uid,
						StringComparison.Ordinal))
				{
					throw new InvalidOperationException(
						$"用户页当前角色 UID {passportSelection.GameUid} 与抽卡页账号 UID {targetAccount.Uid} 不一致。");
				}

				var progress = new Progress<GachaRefreshProgress>(
					ApplyRefreshProgress);
				GachaRefreshResult result = await refreshGachaRecords.ExecuteAsync(
					new GachaRefreshRequest(
						targetAccount,
						source,
						CurrentImportMode,
						passportAccountId,
						manualUrl,
						gameInstallationPath,
						targetPlan.ArchiveToCreate,
						targetPlan.AccountToCreate),
					progress);
				bool parentsCreated =
					targetPlan.AccountToCreate is not null &&
					result.InsertedCount > 0;
				if (parentsCreated)
				{
					await LoadArchivesCoreAsync(targetAccount.PlayerArchiveId);
					await RefreshAccountsCoreAsync();
					SelectedAccount = Accounts.First(
						candidate => candidate.Id == targetAccount.Id);
					await archiveSelectionService.SelectAsync(
						SelectedAccount.Id);
				}
				if (SelectedAccount is not null)
				{
					await ReloadRecordsCoreAsync(pageNumber: 1);
				}
				RefreshSummary =
					$"刷新完成：获取 {result.FetchedCount} 条，" +
					$"新增 {result.InsertedCount} 条，覆盖校正 {result.UpdatedCount} 条，" +
					$"保留重复 {result.DuplicateCount} 条，" +
					$"永久删除抑制 {result.SuppressedCount} 条，" +
					$"请求 {result.PageCount} 页。";
				StatusMessage = result.InsertedCount == 0 && result.UpdatedCount == 0
					? "没有发现新的抽卡记录。"
					: $"已新增 {result.InsertedCount} 条、覆盖校正 {result.UpdatedCount} 条抽卡记录。";
				if (parentsCreated)
				{
					StatusMessage += $" 已自动创建并选择账号 {targetAccount.Uid}。";
				}
				else if (targetPlan.AccountToCreate is not null)
				{
					StatusMessage += " 未产生可保存的新记录，因此未创建空档案或账号。";
				}
				if (result.SuppressedCount > 0)
				{
					StatusMessage +=
						$" 已明确抑制 {result.SuppressedCount} 条本地永久删除记录，未静默恢复。";
				}
			});
		}
		finally
		{
			RefreshProgressDetail = ErrorMessage is null
				? $"刷新完成，共获取 {RefreshItems.Sum(item => item.Count)} 个物品。"
				: $"刷新失败：{ErrorMessage}";
			IsRefreshRunning = false;
		}
	}

	private void BeginRefreshProgress(GachaRefreshSource source)
	{
		refreshItemsByKey.Clear();
		RefreshItems.Clear();
		RefreshProgressTitle = source switch
		{
			GachaRefreshSource.SToken => "正在通过 SToken 获取抽卡记录",
			GachaRefreshSource.WindowsWebCache => "正在通过网页缓存获取抽卡记录",
			GachaRefreshSource.ManualUrl => "正在通过 URL 获取抽卡记录",
			_ => "正在获取抽卡记录"
		};
		RefreshCurrentPool = "正在准备";
		RefreshProgressDetail = "正在验证账号、档案和刷新来源…";
		IsRefreshRunning = true;
	}

	private void ApplyRefreshProgress(GachaRefreshProgress progress)
	{
		RefreshCurrentPool = $"正在获取{FormatGachaPool(progress.GachaType)}";
		RefreshProgressDetail = progress.Stage ==
			GachaRefreshProgressStage.RequestingPage
				? $"正在请求第 {progress.Page} 页 · 已获取 {progress.FetchedCount} 条"
				: $"已完成第 {progress.Page} 页 · 已获取 {progress.FetchedCount} 条";
		foreach (GachaRemoteRecord record in progress.Records)
		{
			AddRefreshItem(record);
		}
	}

	private void AddRefreshItem(GachaRemoteRecord record)
	{
		string itemName = string.IsNullOrWhiteSpace(record.ItemName)
			? "未知物品"
			: record.ItemName.Trim();
		string key = !string.IsNullOrWhiteSpace(record.ItemId)
			? $"id:{record.ItemId.Trim()}"
			: $"name:{itemName}:{record.RankType?.ToString() ?? "?"}";
		int count = Math.Max(1, record.Count);
		if (refreshItemsByKey.TryGetValue(
			key,
			out GachaRefreshItemDisplayItem? existing))
		{
			existing.Count += count;
			return;
		}

		var item = new GachaRefreshItemDisplayItem(
			key,
			itemName,
			record.RankType,
			count);
		refreshItemsByKey.Add(key, item);
		RefreshItems.Add(item);
		_ = LoadRefreshItemIconAsync(item, record.ItemId);
	}

	private async Task LoadRefreshItemIconAsync(
		GachaRefreshItemDisplayItem item,
		string? itemId)
	{
		if (string.IsNullOrWhiteSpace(itemId))
		{
			return;
		}

		try
		{
			GachaItemMetadata? metadata =
				await refreshMetadataProvider.FindByIdAsync(
					GachaGame.GenshinImpact,
					itemId);
			string? path = await refreshIconCache.GetOrRefreshAsync(
				GachaGame.GenshinImpact,
				itemId,
				metadata?.IconUrl);
			if (!string.IsNullOrWhiteSpace(path))
			{
				MainThread.BeginInvokeOnMainThread(() =>
					item.IconSource = ImageSource.FromFile(path));
			}
		}
		catch (Exception)
		{
			// Icon loading is best-effort and must not fail the refresh.
		}
	}

	private static string FormatGachaPool(string gachaType) => gachaType switch
	{
		"301" or "400" => "角色活动祈愿",
		"302" => "武器活动祈愿",
		"500" => "集录祈愿",
		"100" => "新手祈愿",
		"200" => "常驻祈愿",
		_ => $"卡池 {gachaType}"
	};

	private async Task<RefreshTargetPlan> PrepareRefreshTargetAsync(
		GachaRefreshSource source,
		PassportSelection? passportSelection,
		string? manualUrl,
		string? gameInstallationPath)
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

		bool archiveIsProposed = SelectedArchive is null;
		PlayerArchive archive = SelectedArchive ?? PrepareUnnamedArchive();
		IReadOnlyList<GameAccount> existingAccounts = archiveIsProposed
			? []
			: await getGameAccounts.ExecuteAsync(archive.Id);
		GameAccount? account = existingAccounts.FirstOrDefault(candidate =>
			string.Equals(candidate.Uid, identity.Uid, StringComparison.Ordinal));
		GameAccount? accountToCreate = null;
		if (account is null)
		{
			account = await addGameAccount.PrepareAsync(
				archive,
				identity.Uid,
				identity.ServerRegion,
				identity.Uid,
				archiveIsProposed);
			accountToCreate = account;
		}

		return new RefreshTargetPlan(
			account,
			archiveIsProposed ? archive : null,
			accountToCreate);
	}

	private static string FormatImportSummary(
		string fileName,
		GachaImportResult result)
	{
		return $"{fileName}：共读取 {result.TotalCount} 条，" +
			$"导入 {result.ImportedCount} 条，" +
			$"覆盖校正 {result.UpdatedCount} 条，" +
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

	private string GetNextUnnamedArchiveName()
	{
		int suffix = 1;
		while (Archives.Any(archive => archive.Name == $"未命名档案{suffix}"))
		{
			suffix++;
		}

		return $"未命名档案{suffix}";
	}

	private GachaImportMode CurrentImportMode =>
		IsFullImportMode
			? GachaImportMode.Full
			: GachaImportMode.Incremental;

	private PlayerArchive PrepareUnnamedArchive()
	{
		DateTimeOffset now = DateTimeOffset.UtcNow;
		return new PlayerArchive(
			Guid.NewGuid(),
			GetNextUnnamedArchiveName(),
			now,
			now);
	}

	private sealed record RefreshTargetPlan(
		GameAccount Account,
		PlayerArchive? ArchiveToCreate,
		GameAccount? AccountToCreate);

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

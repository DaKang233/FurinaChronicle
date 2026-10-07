// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Exporting;
using FurinaChronicle.App.UI.Gacha;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Core.History;
using FurinaChronicle.Services.Gacha.History;
using FurinaChronicle.Services.Gacha.Portable;
using FurinaChronicle.Services.Gacha.Refreshing;
using System.ComponentModel;

namespace FurinaChronicle.App.UI.Pages;

public partial class GachaPage : ContentPage
{
    private const double MinimumAnalysisViewportHeight = 360;
    private const double MaximumAnalysisViewportHeight = 900;
    private const double AnalysisViewportHeightRatio = 0.65;

    private bool? loadedArchiveScope;
    private bool portableOperationRunning;

    private GachaPageViewModel ViewModel =>
        (GachaPageViewModel)BindingContext;

    public GachaPage(
        GachaPageViewModel viewModel,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.Analysis.PropertyChanged += OnAnalysisPropertyChanged;
        LoadScopePage(viewModel.Analysis.IsArchiveMode);
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        LoadScopePage(ViewModel.Analysis.IsArchiveMode);
        await ViewModel.InitializeAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (height <= 0)
        {
            return;
        }

        double viewportHeight = Math.Clamp(
            height * AnalysisViewportHeightRatio,
            MinimumAnalysisViewportHeight,
            MaximumAnalysisViewportHeight);
        if (Math.Abs(GachaScopePageHost.HeightRequest - viewportHeight) >= 1)
        {
            // The fixed viewport keeps nested CollectionView controls bounded
            // while the outer ScrollView handles the whole page.
            GachaScopePageHost.HeightRequest = viewportHeight;
        }
    }

    private void OnAnalysisPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GachaAnalysisViewModel.IsArchiveMode))
        {
            LoadScopePage(ViewModel.Analysis.IsArchiveMode);
        }
    }

    private void LoadScopePage(bool archiveMode)
    {
        if (loadedArchiveScope == archiveMode &&
            GachaScopePageHost.Content is not null)
        {
            return;
        }

        ReleaseScopePage();
        ContentView page = archiveMode
            ? new ArchiveGachaView()
            : new AccountGachaView();
        page.BindingContext = ViewModel.Analysis;
        GachaScopePageHost.Content = page;
        loadedArchiveScope = archiveMode;
    }

    private void ReleaseScopePage()
    {
        if (GachaScopePageHost.Content is not ContentView oldPage)
        {
            return;
        }

        oldPage.BindingContext = null;
        GachaScopePageHost.Content = null;
        loadedArchiveScope = null;
    }

    private async void OnArchiveSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is not Picker picker)
        {
            return;
        }

        await ViewModel.SelectArchiveAsync(
            picker.SelectedItem as PlayerArchive);
    }

    private async void OnAccountSelectionChanged(
        object? sender,
        EventArgs e)
    {
        if (sender is not Picker picker)
        {
            return;
        }

        await ViewModel.SelectAccountAsync(
            picker.SelectedItem as GameAccount);
    }

    private async void OnSTokenRefreshClicked(object? sender, EventArgs e)
    {
        await ViewModel.RefreshGachaAsync(GachaRefreshSource.SToken);
    }

    private async void OnWebCacheRefreshClicked(object? sender, EventArgs e)
    {
        string? path = await DisplayPromptAsync(
            "网页缓存刷新",
            "请输入原神安装目录或 YuanShen.exe 路径。此功能仅 Windows 可用。",
            "刷新",
            "取消",
            placeholder: @"C:\Games\Genshin Impact game");
        if (!string.IsNullOrWhiteSpace(path))
        {
            await ViewModel.RefreshGachaAsync(
                GachaRefreshSource.WindowsWebCache,
                gameInstallationPath: path);
        }
    }

    private async void OnManualUrlRefreshClicked(object? sender, EventArgs e)
    {
        string? url = await DisplayPromptAsync(
            "输入抽卡记录 URL",
            "请输入包含 authkey 的祈愿页面或 getGachaLog URL。",
            "刷新",
            "取消",
            placeholder: "https://...");
        if (!string.IsNullOrWhiteSpace(url))
        {
            await ViewModel.RefreshGachaAsync(
                GachaRefreshSource.ManualUrl,
                manualUrl: url);
        }
    }

    private async void OnImportPortableClicked(object? sender, EventArgs e)
    {
        if (portableOperationRunning)
        {
            return;
        }
        portableOperationRunning = true;
        try
        {
            FileResult? file = await FilePicker.Default.PickAsync(
                new PickOptions
                {
                    PickerTitle = "选择 Furina Gacha Portable ZIP"
                });
            if (file is null)
            {
                return;
            }

            const string automatic = "按档案名称自动匹配";
            const string selected = "导入到当前所选档案";
            const string create = "按来源名称新建档案";
            string[] choices = ViewModel.SelectedArchive is null
                ? [automatic, create]
                : [automatic, selected, create];
            string? targetMode = await DisplayActionSheetAsync(
                "选择 Portable 导入目标",
                "取消",
                destruction: null,
                choices);
            if (targetMode is null or "取消")
            {
                return;
            }
            Guid? targetArchiveId = targetMode == selected
                ? ViewModel.SelectedArchive?.Id
                : null;
            bool createNew = targetMode == create;
            await using Stream source = await file.OpenReadAsync();
            await using GachaPortableImportSession session =
                await ViewModel.PreparePortableImportAsync(
                    source,
                    targetArchiveId,
                    createNew);

            if (session.Plan.Archive.Kind ==
                GachaPortableArchivePlanKind.RequiresSelection)
            {
                string candidateSummary = session.Plan.Archive.Candidates.Count == 0
                    ? "没有精确或相似名称候选。"
                    : string.Join(
                        "\n",
                        session.Plan.Archive.Candidates.Take(8).Select(
                            candidate =>
                                $"• {candidate.Name}" +
                                (candidate.IsExactMatch ? "（精确同名）" : "（名称相似）")));
                string[] mappingChoices = ViewModel.SelectedArchive is null
                    ? [create]
                    : [selected, create];
                string? mapping = await DisplayActionSheetAsync(
                    $"来源档案：{session.Package.SourceArchive.Name}\n{candidateSummary}",
                    "取消",
                    destruction: null,
                    mappingChoices);
                if (mapping is null or "取消")
                {
                    return;
                }
                await ViewModel.ReplanPortableImportAsync(
                    session,
                    mapping == selected ? ViewModel.SelectedArchive?.Id : null,
                    mapping == create);
            }

            if (!session.Plan.CanApply)
            {
                string conflicts = string.Join(
                    "\n",
                    session.Plan.Accounts
                        .SelectMany(account => account.Conflicts)
                        .Take(12)
                        .Select(conflict =>
                            $"{conflict.ExternalRecordId}: " +
                            string.Join("、", conflict.DifferentFields)));
                await DisplayAlertAsync(
                    "Portable 无法应用",
                    session.Plan.Preview.ConflictCount > 0
                        ? $"发现 {session.Plan.Preview.ConflictCount} 条不同的非空事实；AddOnly 不会覆盖。\n{conflicts}"
                        : "档案或共享身份映射仍存在冲突，请更换目标后重新预览。",
                    "确定");
                return;
            }

            bool infrastructureChanges =
                session.Plan.Archive.Kind ==
                    GachaPortableArchivePlanKind.CreateNew ||
                session.Plan.Preview.AccountCreateCount > 0 ||
                session.Plan.Preview.AliasCount > 0;
            string undoNotice = infrastructureChanges
                ? "本次会创建档案、账号或身份别名，因此整个导入不承诺可撤销。"
                : "本次仅向已有账号新增事实，可按当前撤销保留策略撤销。";
            bool confirmed = await DisplayAlertAsync(
                "确认 Portable 导入",
                $"{ViewModel.PortableSummary}\n{undoNotice}\n来源事实 provenance 会原样保留，本机接收信息另存为 Receipt。",
                "应用",
                "取消");
            if (!confirmed)
            {
                return;
            }

            IReadOnlyList<TombstoneReintroductionConfirmation>? reintroductions = null;
            Guid? cleanupConfirmation = null;
            GachaPortableApplyResult result =
                await ViewModel.ApplyPortableImportAsync(session);
            if (result.Status == ChangeExecutionStatus.Suppressed)
            {
                IReadOnlyList<TombstoneReintroductionWarning> warnings =
                    result.ReintroductionWarnings ??
                    (result.ReintroductionWarning is null
                        ? []
                        : [result.ReintroductionWarning]);
                bool restoreConfirmed = await DisplayAlertAsync(
                    "将重新引入永久删除的记录",
                    $"此包命中 {warnings.Count} 条当前档案的本地永久删除标记。只有本次明确确认才会重新引入；以后自动刷新仍不会获得通用恢复授权。是否继续？",
                    "明确重新引入",
                    "取消");
                if (!restoreConfirmed)
                {
                    return;
                }
                reintroductions = warnings.Select(warning =>
                    new TombstoneReintroductionConfirmation(
                        warning.TombstoneKey,
                        warning.TombstoneVersion,
                        warning.Reference)).ToArray();
                result = await ViewModel.ApplyPortableImportAsync(
                    session,
                    reintroductions);
            }
            if (result.Status == ChangeExecutionStatus.NeedsConfirmation &&
                result.CleanupPlan is not null)
            {
                HistoryCleanupPlan plan = result.CleanupPlan;
                bool cleanupConfirmed = await DisplayAlertAsync(
                    "需要清理较早撤销材料",
                    $"继续需要清理 {plan.Candidates.Count} 份较早撤销材料（约 {plan.ReclaimableBytes} 字节）；不会删除 Revision。",
                    "清理并继续",
                    "取消");
                if (!cleanupConfirmed)
                {
                    return;
                }
                cleanupConfirmation = plan.ConfirmationId;
                result = await ViewModel.ApplyPortableImportAsync(
                    session,
                    reintroductions,
                    cleanupConfirmation);
            }

            string resultMessage = result.Status switch
            {
                ChangeExecutionStatus.Applied =>
                    $"已导入 {result.InsertedRecordCount} 条，保留 {result.SkippedRecordCount} 条。",
                ChangeExecutionStatus.NoOp =>
                    "目标已经包含相同事实，未写入新内容。",
                ChangeExecutionStatus.AlreadyCommitted =>
                    "此操作此前已经提交，未重复写入。",
                ChangeExecutionStatus.Conflict =>
                    result.ConflictReason ?? "预览后目标状态已变化，请重新选择文件并预览。",
                _ => result.ConflictReason ?? result.Status.ToString()
            };
            await DisplayAlertAsync("Portable 导入结果", resultMessage, "确定");
            if (result.Status is ChangeExecutionStatus.Applied or
                ChangeExecutionStatus.AlreadyCommitted)
            {
                await ViewModel.InitializeAsync();
            }
        }
        catch (OperationCanceledException)
        {
            ViewModel.PortableSummary = "Portable 操作已取消，未开始新的提交。";
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Portable 导入失败", exception.Message, "确定");
        }
        finally
        {
            portableOperationRunning = false;
        }
    }

    private async void OnExportPortableClicked(object? sender, EventArgs e)
    {
        if (portableOperationRunning || !await CanBeginExportAsync())
        {
            return;
        }
        portableOperationRunning = true;
        GachaPortableExportArtifact? artifact = null;
        try
        {
            artifact = await ViewModel.CreatePortableArchiveExportAsync();
            await using var source = new FileStream(
                artifact.TemporaryPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ExportSaveResult saved =
                await PlatformExportFileSaver.Default.SaveAsync(
                    $"furina-gacha-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
                    source);
            if (saved.IsCanceled)
            {
                ViewModel.PortableSummary = "已取消选择保存位置；未报告导出成功。";
                return;
            }
            if (!saved.IsSuccessful)
            {
                throw saved.Exception ?? new IOException("无法保存 Portable 文件。");
            }
            ViewModel.PortableSummary =
                $"Portable 已保存：{artifact.Result.AccountCount} 个账号、" +
                $"{artifact.Result.RecordCount} 条记录；位置 {saved.FilePath ?? "由系统文档提供器管理"}。";
            await DisplayAlertAsync(
                "Portable 导出完成",
                ViewModel.PortableSummary,
                "确定");
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync("Portable 导出失败", exception.Message, "确定");
        }
        finally
        {
            if (artifact is not null && File.Exists(artifact.TemporaryPath))
            {
                File.Delete(artifact.TemporaryPath);
            }
            portableOperationRunning = false;
        }
    }

    private async void OnTeyvatHelperImportClicked(
        object? sender,
        EventArgs e)
    {
        const string automaticOption = "使用当前通行证角色自动导入（国服）";
        const string manualOption = "手动输入 UID 和抽卡链接";
        string? option = await DisplayActionSheetAsync(
            "从提瓦特小助手导入",
            "取消",
            destruction: null,
            automaticOption,
            manualOption);

        if (option == automaticOption)
        {
            if (!ViewModel.CanAutomaticallyImportFromTeyvatHelper)
            {
                await DisplayAlertAsync(
                    "无法自动导入",
                    "自动导入要求已登录米哈游通行证、选择国服原神角色，并且账号同时具有 SToken 和 MID。你仍可使用手动方式输入 UID 和抽卡链接。",
                    "确定");
                return;
            }

            bool confirmed = await DisplayAlertAsync(
                "隐私提示",
                $"将为角色 {ViewModel.SelectedPassportRoleUid} 立即生成新的临时抽卡链接，并把 UID 和该链接发送给第三方网站 lelaer.com。是否继续？",
                "继续",
                "取消");
            if (confirmed)
            {
                await ViewModel.ImportFromTeyvatHelperAutomaticallyAsync();
            }

            return;
        }

        if (option != manualOption)
        {
            return;
        }

        string? uid = await DisplayPromptAsync(
            title: "手动从提瓦特小助手导入",
            message: "请输入要导入的原神 UID。",
            accept: "下一步",
            cancel: "取消",
            placeholder: "原神 UID",
            maxLength: 10,
            keyboard: Keyboard.Numeric,
            initialValue: ViewModel.SelectedPassportRoleUid ?? string.Empty);
        if (string.IsNullOrWhiteSpace(uid))
        {
            return;
        }

        string? gachaUrl = await DisplayPromptAsync(
            title: "手动从提瓦特小助手导入",
            message: "请输入包含 authkey 的有效抽卡记录链接。",
            accept: "下一步",
            cancel: "取消",
            placeholder: "https://...");
        if (string.IsNullOrWhiteSpace(gachaUrl))
        {
            return;
        }

        bool manualConfirmed = await DisplayAlertAsync(
            "隐私提示",
            "将把输入的 UID 和抽卡链接发送给第三方网站 lelaer.com。是否继续？",
            "导入",
            "取消");
        if (manualConfirmed)
        {
            await ViewModel.ImportFromTeyvatHelperManuallyAsync(
                uid,
                gachaUrl);
        }
    }

    private async void OnImportIntoArchiveClicked(
        object? sender,
        EventArgs e)
    {
        try
        {
            await PickAndImportUigfAsync(
                importIntoSelectedAccount: false);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "无法打开文件",
                exception.Message,
                "确定");
        }
    }

    private async void OnImportIntoSelectedAccountClicked(
        object? sender,
        EventArgs e)
    {
        if (ViewModel.SelectedArchive is null ||
            ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync(
                "无法导入",
                "请先选择档案和游戏账号。",
                "确定");
            return;
        }

        try
        {
            await PickAndImportUigfAsync(
                importIntoSelectedAccount: true);
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "无法打开文件",
                exception.Message,
                "确定");
        }
    }

    private async Task PickAndImportUigfAsync(
        bool importIntoSelectedAccount)
    {
        var jsonFileTypes = new FilePickerFileType(
            new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".json"],
                [DevicePlatform.Android] =
                    ["application/json", "text/json", "application/octet-stream"]
            });

        FileResult? file = await FilePicker.Default.PickAsync(
            new PickOptions
            {
                PickerTitle = "选择 UIGF JSON 文件",
                FileTypes = jsonFileTypes
            });

        if (file is null)
        {
            return;
        }

        await using Stream stream = await file.OpenReadAsync();
        if (importIntoSelectedAccount)
        {
            await ViewModel.ImportUigfIntoSelectedAccountAsync(
                stream,
                file.FileName);
        }
        else
        {
            await ViewModel.ImportUigfIntoArchiveAsync(
                stream,
                file.FileName);
        }
    }

    private async void OnExportArchiveUigfClicked(
        object? sender,
        EventArgs e)
    {
        await ExportUigfAsync(lockedAccountId: null);
    }

    private async void OnExportAccountUigfClicked(
        object? sender,
        EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync(
                "无法导出",
                "请先选择账号。",
                "确定");
            return;
        }

        await ExportUigfAsync(ViewModel.SelectedAccount?.Id);
    }

    private async void OnExportArchiveTableClicked(
        object? sender,
        EventArgs e)
    {
        await ExportTableAsync(lockedAccountId: null);
    }

    private async void OnExportAccountTableClicked(
        object? sender,
        EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync(
                "无法导出",
                "请先选择账号。",
                "确定");
            return;
        }

        await ExportTableAsync(ViewModel.SelectedAccount?.Id);
    }

    private async Task ExportUigfAsync(Guid? lockedAccountId)
    {
        if (!await CanBeginExportAsync())
        {
            return;
        }

        try
        {
            var optionsPage = new UigfExportOptionsPage(
                ViewModel.Accounts.ToArray(),
                lockedAccountId);
            await Navigation.PushModalAsync(
                new NavigationPage(optionsPage));
            UigfExportDialogResult? selection =
                await optionsPage.WaitForResultAsync();
            if (selection is null)
            {
                return;
            }

            using GachaExportFile? file =
                await ViewModel.CreateUigfExportAsync(
                    selection.GameAccountIds,
                    selection.Options);
            if (file is null)
            {
                return;
            }

            await SaveExportFileAsync(file);
        }
        catch (OperationCanceledException)
        {
            // The user canceled the system save picker.
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "导出失败",
                exception.Message,
                "确定");
        }
    }

    private async Task ExportTableAsync(Guid? lockedAccountId)
    {
        if (!await CanBeginExportAsync())
        {
            return;
        }

        try
        {
            var optionsPage = new TableExportOptionsPage(
                ViewModel.Accounts.ToArray(),
                lockedAccountId);
            await Navigation.PushModalAsync(
                new NavigationPage(optionsPage));
            TableExportDialogResult? selection =
                await optionsPage.WaitForResultAsync();
            if (selection is null)
            {
                return;
            }

            using GachaExportFile? file =
                await ViewModel.CreateTableExportAsync(
                    selection.GameAccountIds,
                    selection.Options);
            if (file is null)
            {
                return;
            }

            await SaveExportFileAsync(file);
        }
        catch (OperationCanceledException)
        {
            // The user canceled the system save picker.
        }
        catch (Exception exception)
        {
            await DisplayAlertAsync(
                "导出失败",
                exception.Message,
                "确定");
        }
    }

    private async Task<bool> CanBeginExportAsync()
    {
        if (ViewModel.SelectedArchive is null)
        {
            await DisplayAlertAsync(
                "无法导出",
                "请先选择档案。",
                "确定");
            return false;
        }

        if (ViewModel.Accounts.Count == 0)
        {
            await DisplayAlertAsync(
                "无法导出",
                "当前档案没有可导出的账号。",
                "确定");
            return false;
        }

        return true;
    }

    private async Task SaveExportFileAsync(GachaExportFile file)
    {
        ExportSaveResult result =
            await PlatformExportFileSaver.Default.SaveAsync(
                file.FileName,
                file.Content);
        if (result.IsCanceled)
        {
            return;
        }
        if (!result.IsSuccessful)
        {
            throw result.Exception ??
                new IOException("无法保存导出文件。");
        }

        ViewModel.NotifyExportSaved(result.FilePath);
        await DisplayAlertAsync(
            "导出完成",
            $"已导出 {file.Result.AccountCount} 个账号、" +
            $"{file.Result.RecordCount} 条记录。\n{result.FilePath}",
            "确定");
    }
}

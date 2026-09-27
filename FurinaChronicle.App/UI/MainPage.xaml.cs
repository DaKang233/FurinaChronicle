using FurinaChronicle.App.Exporting;
using FurinaChronicle.App.UI.Gacha;
using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;
using FurinaChronicle.Services.Gacha.Refreshing;
using System.ComponentModel;

namespace FurinaChronicle.App;

public partial class MainPage : ContentPage
{
    private GachaAnalysisSection? loadedAnalysisSection;

    private MainPageViewModel ViewModel =>
        (MainPageViewModel)BindingContext;

    public MainPage(
        MainPageViewModel viewModel,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.Analysis.PropertyChanged += OnAnalysisPropertyChanged;
        LoadAnalysisPage(viewModel.Analysis.CurrentSection);
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        LoadAnalysisPage(ViewModel.Analysis.CurrentSection);
        await ViewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        AnalysisPageHost.Content = null;
        loadedAnalysisSection = null;
        base.OnDisappearing();
    }

    private void OnAnalysisPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GachaAnalysisViewModel.CurrentSection))
        {
            LoadAnalysisPage(ViewModel.Analysis.CurrentSection);
        }
    }

    private void LoadAnalysisPage(GachaAnalysisSection section)
    {
        if (loadedAnalysisSection == section &&
            AnalysisPageHost.Content is not null)
        {
            return;
        }

        ContentView page = section switch
        {
            GachaAnalysisSection.Overview => new OverviewView(),
            GachaAnalysisSection.Details => new DetailView(),
            GachaAnalysisSection.History => new HistoryView(),
            GachaAnalysisSection.Calendar => new CalendarView(),
            GachaAnalysisSection.Items =>
                new FurinaChronicle.App.UI.Gacha.ItemsView(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(section),
                section,
                null)
        };
        page.BindingContext = ViewModel.Analysis;
        AnalysisPageHost.Content = page;
        loadedAnalysisSection = section;
    }

    private async void OnArchiveModeToggled(
        object? sender,
        ToggledEventArgs e)
    {
        await ViewModel.Analysis.SetArchiveModeAsync(e.Value);
    }

    private async void OnCreateArchiveClicked(
        object? sender,
        EventArgs e)
    {
        string? name = await DisplayPromptAsync(
            title: "创建档案",
            message: "请输入档案名称",
            accept: "创建",
            cancel: "取消",
            placeholder: "例如：我的原神档案",
            maxLength: 50,
            keyboard: Keyboard.Text);

        if (name is null)
        {
            return;
        }

        await ViewModel.CreateArchiveAsync(name);
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

    private async void OnEditAccountClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync("错误", "请先选择账号。", "确定");
            return;
        }
        string name = await DisplayPromptAsync(
            title: "编辑账号",
            message: "请输入名称",
            accept: "重命名",
            cancel: "取消",
            placeholder: "在这里输入名称",
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (name is null) return;
        string uid = await DisplayPromptAsync(
            title: "编辑账号",
            message: "请输入账号的 UID。留空并提交表示不更改。UID 应为符合游戏账号规则的 9~10 位数字。",
            accept: "更改",
            cancel: "取消",
            placeholder: "在这里输入 UID",
            maxLength: 50,
            keyboard: Keyboard.Numeric);
        if (uid is null) return;
        if (uid != string.Empty && !GameUidValidation.IsValidUid(uid))
        {
            await DisplayAlertAsync(
                title: "无效的 UID",
                message: "请输入有效的 UID。有效的 UID 是长度为 9~10 个的数字，并且符合游戏账号规则。",
                cancel: "确定");
            return;
        }
        await ViewModel.EditSelectedAccountAsync(name, uid);
    }

    private async void OnRenameArchiveClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedArchive is null)
        {
            await DisplayAlertAsync("错误", "请先选择档案。", "确定");
            return;
        }
        string name = await DisplayPromptAsync(
            title: "重命名档案",
            message: "请输入名称",
            accept: "重命名",
            cancel: "取消",
            placeholder: "在这里输入名称",
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (name is null) return;
        await ViewModel.RenameSelectedArchiveAsync(name);
    }

    private async void OnDeleteAccountClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync(
            title: "删除账号",
            message: "确定要删除选中的账号吗？该账号的全部祈愿记录也会被永久删除。",
            accept: "删除",
            cancel: "取消");
        if (!confirm) return;
        await ViewModel.DeleteSelectedAccountAsync();
    }

    private async void OnDeleteArchiveClicked(object? sender, EventArgs e)
    {
        bool confirm = await DisplayAlertAsync(
            title: "删除档案",
            message: "确定要删除选中的档案吗？档案内的全部账号和祈愿记录也会被永久删除。",
            accept: "删除",
            cancel: "取消");
        if (!confirm) return;
        await ViewModel.DeleteSelectedArchiveAsync();
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

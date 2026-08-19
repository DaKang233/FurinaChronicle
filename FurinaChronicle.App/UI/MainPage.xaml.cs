using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.App;

public partial class MainPage : ContentPage
{
    private MainPageViewModel ViewModel =>
        (MainPageViewModel)BindingContext;

    public MainPage(MainPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.InitializeAsync();
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

    private async void OnRenameAccountClicked(object? sender, EventArgs e)
    {
        string name = await DisplayPromptAsync(
            title: "重命名账号",
            message: "请输入名称",
            accept: "重命名",
            cancel: "取消",
            placeholder: "在这里输入名称",
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (name is null) return;
        await ViewModel.RenameSelectedAccountAsync(name);
    }

    private async void OnRenameArchiveClicked(object? sender, EventArgs e)
    {
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
}


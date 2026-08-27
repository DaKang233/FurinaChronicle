using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class UserPage : ContentPage
{
    private UserPageViewModel ViewModel =>
        (UserPageViewModel)BindingContext;

    private readonly IServiceProvider serviceProvider;

    public UserPage(
        UserPageViewModel viewModel,
        IServiceProvider serviceProvider)
    {
        InitializeComponent();
        BindingContext = viewModel;
        this.serviceProvider = serviceProvider;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.InitializeAsync();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (OperatingSystem.IsAndroid())
        {
            var page = serviceProvider.GetRequiredService<LoginMethodPage>();
            await Navigation.PushAsync(page);
            return;
        }

        string? method = await DisplayActionSheetAsync(
            "选择登录方式",
            "取消",
            null,
            "账号密码登录",
            "米游社 APP 扫码",
            "手机验证码登录",
            "手动输入 Cookie");
        await OpenLoginPageAsync(method);
    }

    public async Task OpenLoginPageAsync(string? method)
    {
        ContentPage? page = method switch
        {
            "账号密码登录" => serviceProvider.GetRequiredService<PasswordLoginPage>(),
            "米游社 APP 扫码" => serviceProvider.GetRequiredService<QrLoginPage>(),
            "手机验证码登录" => serviceProvider.GetRequiredService<MobileCaptchaLoginPage>(),
            "手动输入 Cookie" => serviceProvider.GetRequiredService<ManualCookieLoginPage>(),
            _ => null
        };
        if (page is null)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            await Navigation.PushModalAsync(
                new NavigationPage(page));
        }
        else
        {
            await Navigation.PushAsync(page);
        }
    }

    private async void OnDeleteClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "删除通行证账号",
            $"确定删除 {ViewModel.SelectedAccount.DisplayName}？不会删除本地抽卡档案。",
            "删除",
            "取消");
        if (confirmed)
        {
            await ViewModel.DeleteSelectedAsync();
        }
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await ViewModel.ReloadAsync();
    }

    private async void OnAccountSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await ViewModel.SelectAccountAsync(
                picker.SelectedItem as PassportAccountListItem);
        }
    }

    private async void OnRoleSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await ViewModel.SelectRoleAsync(
                picker.SelectedItem as PassportGameRole);
        }
    }
}

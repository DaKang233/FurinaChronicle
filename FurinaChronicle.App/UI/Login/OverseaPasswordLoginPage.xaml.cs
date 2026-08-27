using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class OverseaPasswordLoginPage : ContentPage
{
    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;
    private bool busy;

    public OverseaPasswordLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        if (busy)
        {
            return;
        }

        try
        {
            busy = true;
            LoginButton.IsEnabled = false;
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            StatusLabel.TextColor = Colors.Gray;
            StatusLabel.Text = "正在登录 HoYoLAB…";
            PassportAccount account =
                await passportAccountService.LoginWithOverseaPasswordAsync(
                    AccountEntry.Text ?? string.Empty,
                    PasswordEntry.Text ?? string.Empty);
            PasswordEntry.Text = string.Empty;
            StatusLabel.Text = "登录成功。";
            await LoginNavigation.CompleteAsync(
                this,
                userPageViewModel,
                account);
        }
        catch (Exception exception)
        {
            StatusLabel.TextColor = Colors.Red;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            busy = false;
            LoginButton.IsEnabled = true;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await LoginNavigation.GoBackAsync(this);
    }
}

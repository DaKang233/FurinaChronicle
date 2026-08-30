using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class ManualCookieLoginPage : ContentPage
{
    public ManualCookieLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
    }

    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;
    private bool busy;

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
            StatusLabel.Text = "正在验证并保存…";
            PassportAccount account =
                await passportAccountService.LoginWithManualCookieAsync(
                    CookieEditor.Text ?? string.Empty);
            CookieEditor.Text = string.Empty;
            await LoginNavigation.CompleteAsync(this, userPageViewModel, account);
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
        if (busy)
        {
            return;
        }

        await LoginNavigation.GoBackAsync(this);
    }
}

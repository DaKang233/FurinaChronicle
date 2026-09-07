using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class ManualCookieLoginPage : ContentPage
{
    public ManualCookieLoginPage(
        MainlandPassportLoginService passportLoginService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportLoginService = passportLoginService;
        this.userPageViewModel = userPageViewModel;
    }

    private readonly MainlandPassportLoginService passportLoginService;
    private readonly UserPageViewModel userPageViewModel;
    private CancellationTokenSource? pageCancellation;
    private bool busy;
    private bool dismissed;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        dismissed = false;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = new CancellationTokenSource();
    }

    protected override void OnDisappearing()
    {
        dismissed = true;
        pageCancellation?.Cancel();
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        dismissed = true;
        pageCancellation?.Cancel();
        return base.OnBackButtonPressed();
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
            StatusLabel.Text = "正在验证并保存…";
            CancellationToken token =
                pageCancellation?.Token ?? CancellationToken.None;
            PassportAccount account =
                await passportLoginService.LoginWithManualCookieAsync(
                    CookieEditor.Text ?? string.Empty,
                    token);
            token.ThrowIfCancellationRequested();
            if (dismissed)
            {
                return;
            }

            CookieEditor.Text = string.Empty;
            await LoginNavigation.CompleteAsync(this, userPageViewModel, account);
        }
        catch (OperationCanceledException)
            when (pageCancellation?.IsCancellationRequested == true)
        {
            if (!dismissed)
            {
                StatusLabel.Text = "操作已取消。";
            }
        }
        catch (Exception exception)
        {
            if (!dismissed)
            {
                StatusLabel.TextColor = Colors.Red;
                StatusLabel.Text = exception.Message;
            }
        }
        finally
        {
            busy = false;
            LoginButton.IsEnabled = !dismissed;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        dismissed = true;
        pageCancellation?.Cancel();
        await LoginNavigation.GoBackAsync(this);
    }
}

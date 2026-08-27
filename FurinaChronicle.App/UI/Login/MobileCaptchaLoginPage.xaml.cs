using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class MobileCaptchaLoginPage : ContentPage
{
    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;
    private MobileCaptchaChallenge? challenge;
    private bool busy;

    public MobileCaptchaLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            challenge = await passportAccountService.SendMobileCaptchaAsync(
                MobileEntry.Text ?? string.Empty,
                challenge?.Aigis);
            StatusLabel.Text = "验证码已发送，请查收短信。";
        });
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await RunAsync(async () =>
        {
            if (challenge is null)
            {
                throw new InvalidOperationException("请先发送短信验证码。");
            }

            PassportAccount account =
                await passportAccountService.LoginWithMobileCaptchaAsync(
                    MobileEntry.Text ?? string.Empty,
                    CaptchaEntry.Text ?? string.Empty,
                    challenge);
            await LoginNavigation.CompleteAsync(this, userPageViewModel, account);
        });
    }

    private async Task RunAsync(Func<Task> operation)
    {
        if (busy)
        {
            return;
        }

        try
        {
            busy = true;
            BusyIndicator.IsVisible = true;
            BusyIndicator.IsRunning = true;
            StatusLabel.TextColor = Colors.Gray;
            await operation();
        }
        catch (Exception exception)
        {
            StatusLabel.TextColor = Colors.Red;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            busy = false;
            BusyIndicator.IsRunning = false;
            BusyIndicator.IsVisible = false;
        }
    }
}

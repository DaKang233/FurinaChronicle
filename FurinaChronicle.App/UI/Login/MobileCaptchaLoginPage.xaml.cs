using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class MobileCaptchaLoginPage : ContentPage
{
    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;
    private readonly MobileCaptchaCooldown captchaCooldown;
    private MobileCaptchaChallenge? challenge;
    private CancellationTokenSource? countdownCancellation;
    private bool busy;

    public MobileCaptchaLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel,
        MobileCaptchaCooldown captchaCooldown)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
        this.captchaCooldown = captchaCooldown;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartCooldownDisplay();
    }

    protected override void OnDisappearing()
    {
        countdownCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnSendClicked(object? sender, EventArgs e)
    {
        if (captchaCooldown.IsActive)
        {
            StartCooldownDisplay();
            return;
        }

        await RunAsync(async () =>
        {
            challenge = await passportAccountService.SendMobileCaptchaAsync(
                MobileEntry.Text ?? string.Empty,
                challenge?.Aigis);
            StatusLabel.Text = "验证码已发送，请查收短信。";
            captchaCooldown.Start();
            StartCooldownDisplay();
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
            UpdateSendButton();
        }
    }

    private void StartCooldownDisplay()
    {
        countdownCancellation?.Cancel();
        countdownCancellation?.Dispose();
        countdownCancellation = new CancellationTokenSource();
        _ = RunCooldownDisplayAsync(countdownCancellation.Token);
    }

    private async Task RunCooldownDisplayAsync(CancellationToken token)
    {
        try
        {
            while (captchaCooldown.IsActive && !token.IsCancellationRequested)
            {
                UpdateSendButton();
                await Task.Delay(TimeSpan.FromSeconds(1), token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        UpdateSendButton();
    }

    private void UpdateSendButton()
    {
        int remaining = captchaCooldown.RemainingSeconds;
        SendButton.Text = remaining > 0
            ? $"{remaining} 秒后重试"
            : "发送验证码";
        SendButton.IsEnabled = !busy && remaining == 0;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        countdownCancellation?.Cancel();
        await LoginNavigation.GoBackAsync(this);
    }
}

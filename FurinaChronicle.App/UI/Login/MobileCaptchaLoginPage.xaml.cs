using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class MobileCaptchaLoginPage : ContentPage
{
    private readonly MainlandPassportLoginService passportLoginService;
    private readonly UserPageViewModel userPageViewModel;
    private readonly MobileCaptchaCooldown captchaCooldown;
    private MobileCaptchaChallenge? challenge;
    private CancellationTokenSource? countdownCancellation;
    private CancellationTokenSource? pageCancellation;
    private bool busy;
    private bool dismissed;

    public MobileCaptchaLoginPage(
        MainlandPassportLoginService passportLoginService,
        UserPageViewModel userPageViewModel,
        MobileCaptchaCooldown captchaCooldown)
    {
        InitializeComponent();
        this.passportLoginService = passportLoginService;
        this.userPageViewModel = userPageViewModel;
        this.captchaCooldown = captchaCooldown;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        dismissed = false;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = new CancellationTokenSource();
        StartCooldownDisplay();
    }

    protected override void OnDisappearing()
    {
        dismissed = true;
        pageCancellation?.Cancel();
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

        await RunAsync(async token =>
        {
            challenge = await passportLoginService.SendMobileCaptchaAsync(
                MobileEntry.Text ?? string.Empty,
                challenge?.Aigis,
                token);
            token.ThrowIfCancellationRequested();
            StatusLabel.Text = "验证码已发送，请查收短信。";
            captchaCooldown.Start();
            StartCooldownDisplay();
        });
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        await RunAsync(async token =>
        {
            if (challenge is null)
            {
                throw new InvalidOperationException("请先发送短信验证码。");
            }

            PassportAccount account =
                await passportLoginService.LoginWithMobileCaptchaAsync(
                    MobileEntry.Text ?? string.Empty,
                    CaptchaEntry.Text ?? string.Empty,
                    challenge,
                    token);
            token.ThrowIfCancellationRequested();
            if (dismissed)
            {
                return;
            }

            await LoginNavigation.CompleteAsync(this, userPageViewModel, account);
        });
    }

    private async Task RunAsync(
        Func<CancellationToken, Task> operation)
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
            LoginButton.IsEnabled = false;
            StatusLabel.TextColor = Colors.Gray;
            CancellationToken token =
                pageCancellation?.Token ?? CancellationToken.None;
            await operation(token);
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
            StatusLabel.TextColor = Colors.Red;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            busy = false;
            LoginButton.IsEnabled = !dismissed;
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
        SendButton.IsEnabled = !dismissed && !busy && remaining == 0;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        dismissed = true;
        pageCancellation?.Cancel();
        countdownCancellation?.Cancel();
        await LoginNavigation.GoBackAsync(this);
    }
}

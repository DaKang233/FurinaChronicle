using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class HoYoLabPasswordLoginPage : ContentPage,
    IPassportSecurityVerificationHandler
{
    private readonly HoYoLabPassportLoginService passportLoginService;
    private readonly UserPageViewModel userPageViewModel;
    private CancellationTokenSource? pageCancellation;
    private bool busy;
    private bool dismissed;
    private bool presentingVerification;

    public HoYoLabPasswordLoginPage(
        HoYoLabPassportLoginService passportLoginService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportLoginService = passportLoginService;
        this.userPageViewModel = userPageViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (presentingVerification)
        {
            return;
        }

        dismissed = false;
        pageCancellation?.Cancel();
        pageCancellation?.Dispose();
        pageCancellation = new CancellationTokenSource();
    }

    protected override void OnDisappearing()
    {
        if (!presentingVerification)
        {
            dismissed = true;
            pageCancellation?.Cancel();
        }

        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed()
    {
        if (!presentingVerification)
        {
            dismissed = true;
            pageCancellation?.Cancel();
        }

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
            StatusLabel.Text = "正在登录 HoYoLAB…";
            CancellationToken token =
                pageCancellation?.Token ?? CancellationToken.None;
            PassportAccount account =
                await passportLoginService.LoginWithPasswordAsync(
                    AccountEntry.Text ?? string.Empty,
                    PasswordEntry.Text ?? string.Empty,
                    this,
                    token);
            token.ThrowIfCancellationRequested();
            if (dismissed)
            {
                return;
            }

            PasswordEntry.Text = string.Empty;
            StatusLabel.Text = "登录成功。";
            await LoginNavigation.CompleteAsync(
                this,
                userPageViewModel,
                account);
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

    public async Task<PassportGeetestResult?> VerifyGeetestAsync(
        PassportGeetestChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            presentingVerification = true;
            try
            {
                var page = new GeetestVerificationPage(challenge);
                await Navigation.PushModalAsync(page);
                return await page.WaitForResultAsync(cancellationToken);
            }
            finally
            {
                presentingVerification = false;
            }
        });
    }

    public async Task<string?> RequestAccountVerificationCodeAsync(
        PassportAccountVerificationChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string destinationKind =
            challenge.Method == HoYoLabVerificationMethod.Mobile
                ? "手机"
                : "邮箱";
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            presentingVerification = true;
            try
            {
                return await DisplayPromptAsync(
                    "HoYoLAB 账号安全验证",
                    string.IsNullOrWhiteSpace(challenge.Destination)
                        ? $"验证码已发送到账号绑定的{destinationKind}，请输入验证码。"
                        : $"验证码已发送到 {challenge.Destination}，请输入验证码。",
                    accept: "验证",
                    cancel: "取消",
                    placeholder: "安全验证码",
                    maxLength: 8,
                    keyboard: Keyboard.Numeric);
            }
            finally
            {
                presentingVerification = false;
            }
        });
    }
}

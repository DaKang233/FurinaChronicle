using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class OverseaPasswordLoginPage : ContentPage,
    IPassportSecurityVerificationHandler
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
                    PasswordEntry.Text ?? string.Empty,
                    this);
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

    public async Task<PassportGeetestResult?> VerifyGeetestAsync(
        PassportGeetestChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var page = new GeetestVerificationPage(challenge);
            await Navigation.PushModalAsync(page);
            return await page.WaitForResultAsync(cancellationToken);
        });
    }

    public async Task<string?> RequestAccountVerificationCodeAsync(
        PassportAccountVerificationChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await MainThread.InvokeOnMainThreadAsync(() => DisplayPromptAsync(
            "HoYoLAB 账号安全验证",
            string.IsNullOrWhiteSpace(challenge.Destination)
                ? "验证码已发送到账号绑定的邮箱或手机，请输入验证码。"
                : $"验证码已发送到 {challenge.Destination}，请输入验证码。",
            accept: "验证",
            cancel: "取消",
            placeholder: "安全验证码",
            maxLength: 8,
            keyboard: Keyboard.Numeric));
    }
}

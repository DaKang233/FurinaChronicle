using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;
using QRCoder;

namespace FurinaChronicle.App;

public partial class QrLoginPage : ContentPage
{
    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;
    private CancellationTokenSource? pollingCancellation;
    private bool started;

    public QrLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!started)
        {
            started = true;
            await StartAsync();
        }
    }

    protected override void OnDisappearing()
    {
        pollingCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await StartAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        pollingCancellation?.Cancel();
        await LoginNavigation.GoBackAsync(this);
    }

    private async Task StartAsync()
    {
        pollingCancellation?.Cancel();
        pollingCancellation?.Dispose();
        pollingCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        CancellationToken token = pollingCancellation.Token;

        try
        {
            BusyIndicator.IsRunning = true;
            StatusLabel.TextColor = Colors.Gray;
            StatusLabel.Text = "正在创建二维码…";
            PassportQrSession session =
                await passportAccountService.BeginQrLoginAsync(token);
            using var generator = new QRCodeGenerator();
            using QRCodeData data = generator.CreateQrCode(
                session.Url,
                QRCodeGenerator.ECCLevel.M);
            var qrCode = new PngByteQRCode(data);
            byte[] png = qrCode.GetGraphic(12);
            QrImage.Source = ImageSource.FromStream(() => new MemoryStream(png));
            StatusLabel.Text = "请使用米游社 APP 扫描并确认登录。";

            while (!token.IsCancellationRequested)
            {
                (PassportQrStatus status, PassportAccount? account) =
                    await passportAccountService.PollQrLoginAsync(session, token);
                if (status == PassportQrStatus.Confirmed && account is not null)
                {
                    StatusLabel.Text = "登录成功。";
                    await LoginNavigation.CompleteAsync(
                        this,
                        userPageViewModel,
                        account);
                    return;
                }

                if (status == PassportQrStatus.Expired)
                {
                    StatusLabel.Text = "二维码已过期，请重新生成。";
                    return;
                }

                StatusLabel.Text = status == PassportQrStatus.Scanned
                    ? "已扫码，请在手机上确认。"
                    : "等待扫码…";
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "扫码已取消或超时。";
        }
        catch (Exception exception)
        {
            StatusLabel.TextColor = Colors.Red;
            StatusLabel.Text = exception.Message;
        }
        finally
        {
            BusyIndicator.IsRunning = false;
        }
    }
}

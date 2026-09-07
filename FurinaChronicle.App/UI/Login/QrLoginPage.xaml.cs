using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;
using QRCoder;

namespace FurinaChronicle.App;

public partial class QrLoginPage : ContentPage
{
    private readonly MainlandPassportLoginService passportLoginService;
    private readonly UserPageViewModel userPageViewModel;
    private readonly SemaphoreSlim restartGate = new(1, 1);
    private CancellationTokenSource? pollingCancellation;
    private Task? activePolling;
    private bool dismissed;

    public QrLoginPage(
        MainlandPassportLoginService passportLoginService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportLoginService = passportLoginService;
        this.userPageViewModel = userPageViewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        dismissed = false;
        await RestartAsync();
    }

    protected override void OnDisappearing()
    {
        dismissed = true;
        pollingCancellation?.Cancel();
        base.OnDisappearing();
    }

    private async void OnReloadClicked(object? sender, EventArgs e)
    {
        await RestartAsync();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        dismissed = true;
        pollingCancellation?.Cancel();
        await LoginNavigation.GoBackAsync(this);
    }

    private async Task RestartAsync()
    {
        Task? operation = null;
        await restartGate.WaitAsync();
        try
        {
            pollingCancellation?.Cancel();
            if (activePolling is not null)
            {
                await activePolling;
            }

            pollingCancellation?.Dispose();
            pollingCancellation = null;
            activePolling = null;
            if (dismissed)
            {
                return;
            }

            pollingCancellation =
                new CancellationTokenSource(TimeSpan.FromMinutes(3));
            operation = RunPollingAsync(pollingCancellation.Token);
            activePolling = operation;
        }
        finally
        {
            restartGate.Release();
        }

        await operation;
    }

    private async Task RunPollingAsync(CancellationToken token)
    {

        try
        {
            BusyIndicator.IsRunning = true;
            StatusLabel.TextColor = Colors.Gray;
            StatusLabel.Text = "正在创建二维码…";
            PassportQrSession session =
                await passportLoginService.BeginQrLoginAsync(token);
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
                    await passportLoginService.PollQrLoginAsync(session, token);
                token.ThrowIfCancellationRequested();
                if (dismissed)
                {
                    return;
                }

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
            if (!dismissed)
            {
                StatusLabel.Text = "扫码已取消或超时。";
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
            BusyIndicator.IsRunning = false;
        }
    }
}

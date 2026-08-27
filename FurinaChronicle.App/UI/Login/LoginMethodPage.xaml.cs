namespace FurinaChronicle.App;

public partial class LoginMethodPage : ContentPage
{
    private readonly IServiceProvider serviceProvider;

    public LoginMethodPage(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        this.serviceProvider = serviceProvider;
    }

    private Task OpenAsync<TPage>() where TPage : Page
    {
        return Navigation.PushAsync(
            serviceProvider.GetRequiredService<TPage>());
    }

    private async void OnPasswordClicked(object? sender, EventArgs e) =>
		await OpenAsync<OverseaPasswordLoginPage>();

    private async void OnQrClicked(object? sender, EventArgs e) =>
        await OpenAsync<QrLoginPage>();

    private async void OnMobileClicked(object? sender, EventArgs e) =>
        await OpenAsync<MobileCaptchaLoginPage>();

    private async void OnCookieClicked(object? sender, EventArgs e) =>
        await OpenAsync<ManualCookieLoginPage>();

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await LoginNavigation.GoBackAsync(this);
}

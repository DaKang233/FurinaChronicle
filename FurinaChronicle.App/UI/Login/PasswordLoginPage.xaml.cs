using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Passport;
using FurinaChronicle.Services.Passport;
using System.Text.Json;

namespace FurinaChronicle.App;

public partial class PasswordLoginPage : ContentPage
{
    private readonly PassportAccountService passportAccountService;
    private readonly UserPageViewModel userPageViewModel;

    public PasswordLoginPage(
        PassportAccountService passportAccountService,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.passportAccountService = passportAccountService;
        this.userPageViewModel = userPageViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StatusLabel.Text = "国服网页密码登录已停用，请返回并使用其他登录方式。";
    }

    private async Task PollCookiesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                string? cookie = await CollectCookieAsync();
                if (!string.IsNullOrWhiteSpace(cookie))
                {
                    string? loginResponse = await CollectLoginResponseAsync();
                    PassportAccount account =
                        await passportAccountService.CompletePasswordLoginAsync(
                            cookie,
                            loginResponse,
                            cancellationToken);
                    StatusLabel.Text = "登录成功。";
                    await LoginNavigation.CompleteAsync(
                        this,
                        userPageViewModel,
                        account);
                    return;
                }
            }
            catch (FormatException)
            {
                // The page has not finished issuing login cookies yet.
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                StatusLabel.TextColor = Colors.Red;
                StatusLabel.Text = exception.Message;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private async void OnPassportWebViewNavigated(
        object? sender,
        WebNavigatedEventArgs e)
    {
        try
        {
            await PassportWebView.EvaluateJavaScriptAsync(
                """
                (function () {
                    if (window.__furinaPassportInterceptorInstalled) return;
                    window.__furinaPassportInterceptorInstalled = true;
                    window.__furinaPassportLoginResponse = '';

                    const shouldCapture = function (url) {
                        return typeof url === 'string' &&
                            url.indexOf('/ma-cn-passport/web/login') >= 0;
                    };
                    const capture = function (url, body) {
                        if (!shouldCapture(url) || typeof body !== 'string') return;
                        try {
                            const value = JSON.parse(body);
                            if (value && value.retcode === 0 && value.data) {
                                window.__furinaPassportLoginResponse = body;
                            }
                        } catch (_) { }
                    };

                    const originalFetch = window.fetch;
                    if (originalFetch) {
                        window.fetch = async function () {
                            const response = await originalFetch.apply(this, arguments);
                            try {
                                const url = response.url || String(arguments[0] || '');
                                response.clone().text().then(function (body) {
                                    capture(url, body);
                                });
                            } catch (_) { }
                            return response;
                        };
                    }

                    const originalOpen = XMLHttpRequest.prototype.open;
                    const originalSend = XMLHttpRequest.prototype.send;
                    XMLHttpRequest.prototype.open = function (method, url) {
                        this.__furinaRequestUrl = String(url || '');
                        return originalOpen.apply(this, arguments);
                    };
                    XMLHttpRequest.prototype.send = function () {
                        this.addEventListener('load', function () {
                            capture(this.__furinaRequestUrl, this.responseText);
                        });
                        return originalSend.apply(this, arguments);
                    };
                })();
                """);
        }
        catch (Exception exception)
        {
            StatusLabel.TextColor = Colors.Red;
            StatusLabel.Text = $"无法监听登录结果：{exception.Message}";
        }
    }

    private async Task<string?> CollectLoginResponseAsync()
    {
        string? result = await PassportWebView.EvaluateJavaScriptAsync(
            "window.__furinaPassportLoginResponse || ''");
        if (string.IsNullOrWhiteSpace(result) || result == "null")
        {
            return null;
        }

        if (result.Length >= 2 && result[0] == '"')
        {
            try
            {
                return JsonSerializer.Deserialize<string>(result);
            }
            catch (JsonException)
            {
                // Some platform handlers already return the unquoted value.
            }
        }

        return result;
    }

    private async Task<string?> CollectCookieAsync()
    {
#if WINDOWS
        if (PassportWebView.Handler?.PlatformView is
            Microsoft.UI.Xaml.Controls.WebView2 nativeWebView &&
            nativeWebView.CoreWebView2 is not null)
        {
            IReadOnlyList<Microsoft.Web.WebView2.Core.CoreWebView2Cookie> cookies =
                await nativeWebView.CoreWebView2.CookieManager.GetCookiesAsync(
                    "https://mihoyo.com");
            return string.Join("; ", cookies.Select(cookie =>
                $"{cookie.Name}={cookie.Value}"));
        }
        return null;
#elif ANDROID
        return Android.Webkit.CookieManager.Instance?.GetCookie(
            "https://mihoyo.com");
#else
        return null;
#endif
    }
}

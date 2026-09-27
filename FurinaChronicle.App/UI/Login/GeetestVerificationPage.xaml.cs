using System.Text.Json;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class GeetestVerificationPage : ContentPage
{
    private const string CallbackPrefix =
        "furinachronicle-geetest://complete";
    private readonly TaskCompletionSource<PassportGeetestResult?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int isCompleted;

    public GeetestVerificationPage(PassportGeetestChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        InitializeComponent();
        VerificationWebView.Source = new HtmlWebViewSource
        {
            Html = CreateHtml(challenge)
        };
    }

    public async Task<PassportGeetestResult?> WaitForResultAsync(
        CancellationToken cancellationToken = default)
    {
        using CancellationTokenRegistration registration =
            cancellationToken.Register(() =>
                MainThread.BeginInvokeOnMainThread(() =>
                    _ = CompleteAsync(null)));
        return await completion.Task;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CompleteAsync(null);
        return true;
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        await CompleteAsync(null);
    }

    private void OnWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (!e.Url.StartsWith(CallbackPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        try
        {
            Uri uri = new(e.Url);
            IReadOnlyDictionary<string, string> query = ParseQuery(uri.Query);
            if (!query.TryGetValue("challenge", out string? challenge) ||
                !query.TryGetValue("validate", out string? validate) ||
                string.IsNullOrWhiteSpace(challenge) ||
                string.IsNullOrWhiteSpace(validate))
            {
                throw new FormatException("GeeTest 没有返回完整验证结果。");
            }

            _ = CompleteAsync(new PassportGeetestResult(challenge, validate));
        }
        catch (Exception exception)
        {
            _ = DisplayAlertAsync("安全验证失败", exception.Message, "确定");
            _ = CompleteAsync(null);
        }
    }

    private async Task CompleteAsync(PassportGeetestResult? result)
    {
        if (Interlocked.Exchange(ref isCompleted, 1) != 0)
        {
            return;
        }

        try
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }
        finally
        {
            completion.TrySetResult(result);
        }
    }

    private static string CreateHtml(PassportGeetestChallenge challenge)
    {
        string gt = JsonSerializer.Serialize(challenge.Gt);
        string initialChallenge = JsonSerializer.Serialize(challenge.Challenge);
        string apiServer = JsonSerializer.Serialize(
            challenge.IsOversea ? "api-na.geetest.com" : "api.geetest.com");
        return $$"""
            <!doctype html>
            <html>
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width,initial-scale=1">
                <style>
                    html,body,#geetest { height:100%; margin:0; }
                    body { display:flex; align-items:center; justify-content:center; background:transparent; }
                </style>
                <script src="https://static.geetest.com/static/js/gt.0.5.2.js"></script>
            </head>
            <body>
                <div id="geetest"></div>
                <script>
                    initGeetest({
                        protocol: 'https://',
                        gt: {{gt}},
                        challenge: {{initialChallenge}},
                        new_captcha: true,
                        product: 'bind',
                        api_server: {{apiServer}}
                    }, function (captcha) {
                        captcha.appendTo('#geetest');
                        captcha.onReady(function () { captcha.verify(); });
                        captcha.onSuccess(function () {
                            var result = captcha.getValidate();
                            if (!result) return;
                            location.href = 'furinachronicle-geetest://complete' +
                                '?challenge=' + encodeURIComponent(result.geetest_challenge) +
                                '&validate=' + encodeURIComponent(result.geetest_validate);
                        });
                    });
                </script>
            </body>
            </html>
            """;
    }

    private static IReadOnlyDictionary<string, string> ParseQuery(string query)
    {
        return query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(
                parts => Uri.UnescapeDataString(parts[0]),
                parts => Uri.UnescapeDataString(parts[1]),
                StringComparer.OrdinalIgnoreCase);
    }
}

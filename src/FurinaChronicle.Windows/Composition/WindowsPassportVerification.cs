// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Services.Passport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Composition;

public sealed class WindowsPassportVerification(XamlRoot root) : IPassportSecurityVerificationHandler
{
    public async Task<PassportGeetestResult?> VerifyGeetestAsync(
        PassportGeetestChallenge challenge, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var web = new WebView2 { Width = 420, Height = 340 };
        var dialog = CreateDialog("安全验证", web);
        PassportGeetestResult? result = null;
        bool open = true;
        await web.EnsureCoreWebView2Async();
        web.CoreWebView2.Settings.AreDevToolsEnabled = false;
        web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        web.CoreWebView2.NavigationStarting += (_, args) =>
        {
            if (args.Uri != "about:blank") args.Cancel = true;
        };
        web.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
        web.CoreWebView2.WebMessageReceived += (_, args) =>
        {
            if (!open || args.Source != "about:blank") return;
            try
            {
                if (challenge.IsVersion4)
                {
                    string payload = args.WebMessageAsJson;
                    using JsonDocument proof = JsonDocument.Parse(payload);
                    if (proof.RootElement.ValueKind != JsonValueKind.Object ||
                        !HasText(proof.RootElement, "lot_number") ||
                        !HasText(proof.RootElement, "captcha_output") ||
                        !HasText(proof.RootElement, "pass_token") ||
                        !HasText(proof.RootElement, "gen_time"))
                    {
                        return;
                    }

                    result = new PassportGeetestResult(
                        string.Empty,
                        string.Empty,
                        Payload: payload);
                    dialog.Hide();
                    return;
                }

                using JsonDocument message = JsonDocument.Parse(args.WebMessageAsJson);
                var value = message.RootElement;
                string? proofChallenge = value.GetProperty("geetest_challenge").GetString();
                string? validate = value.GetProperty("geetest_validate").GetString();
                string? seccode = value.GetProperty("geetest_seccode").GetString();
                if (string.IsNullOrWhiteSpace(proofChallenge) || string.IsNullOrWhiteSpace(validate) ||
                    string.IsNullOrWhiteSpace(seccode)) return;
                result = new PassportGeetestResult(proofChallenge, validate, seccode);
                dialog.Hide();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            {
            }
        };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            dialog.Opened += (_, _) => web.NavigateToString(BuildHtml(challenge));
            var pending = dialog.ShowAsync();
            using var registration = cancellationToken.Register(() =>
                dialog.DispatcherQueue.TryEnqueue(() => { if (open) dialog.Hide(); }));
            await pending;
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            open = false;
            web.Close();
        }
    }

    public async Task<string?> RequestAccountVerificationCodeAsync(
        PassportAccountVerificationChallenge challenge, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = new PasswordBox
        {
            Header = challenge.Method == HoYoLabVerificationMethod.Mobile ? "手机验证码" : "邮箱验证码",
            MaxLength = 8,
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        var dialog = CreateDialog("HoYoLAB 账号安全验证", input);
        dialog.PrimaryButtonText = "验证";
        bool open = true;
        var pending = dialog.ShowAsync();
        using var registration = cancellationToken.Register(() =>
            dialog.DispatcherQueue.TryEnqueue(() => { if (open) dialog.Hide(); }));
        try
        {
            var response = await pending;
            cancellationToken.ThrowIfCancellationRequested();
            return response == ContentDialogResult.Primary ? input.Password.Trim() : null;
        }
        finally
        {
            open = false;
            input.Password = string.Empty;
        }
    }

    private ContentDialog CreateDialog(string title, FrameworkElement content) => new()
    {
        Title = title, XamlRoot = root, Content = content, CloseButtonText = "取消"
    };

    private static string BuildHtml(PassportGeetestChallenge challenge)
    {
        if (challenge.IsVersion4)
        {
            return BuildVersion4Html(challenge);
        }

        string gt = JsonSerializer.Serialize(challenge.Gt);
        string value = JsonSerializer.Serialize(challenge.Challenge);
        string server = challenge.IsOversea ? "api-na.geetest.com" : "api.geetest.com";
        return $$"""
            <!doctype html>
            <html lang="zh-CN"><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <script src="https://static.geetest.com/static/js/gt.0.5.2.js"></script>
            <div id="geetest"></div>
            <script>
            initGeetest({
                protocol: 'https:', gt: {{gt}}, challenge: {{value}},
                new_captcha: true, product: 'bind', api_server: '{{server}}'
            }, function (captcha) {
                captcha.appendTo('#geetest');
                captcha.onReady(function () { captcha.verify(); });
                captcha.onSuccess(function () {
                    const proof = captcha.getValidate();
                    if (proof) window.chrome.webview.postMessage(proof);
                });
            });
            </script></html>
            """;
    }

    private static string BuildVersion4Html(PassportGeetestChallenge challenge)
    {
        string captchaId = JsonSerializer.Serialize(challenge.CaptchaId);
        string riskType = JsonSerializer.Serialize(challenge.RiskType);
        string sessionId = JsonSerializer.Serialize(challenge.SessionId);
        return $$"""
            <!doctype html>
            <html lang="zh-CN"><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <script src="https://static.geetest.com/v4/gt4.js"></script>
            <div id="geetest"></div>
            <script>
            initGeetest4({
                captchaId: {{captchaId}},
                riskType: {{riskType}} || undefined,
                userInfo: JSON.stringify({ session_id: {{sessionId}} }),
                protocol: 'https://',
                https: true,
                product: 'bind'
            }, function (captcha) {
                captcha.appendTo('#geetest');
                captcha.onReady(function () { captcha.showCaptcha(); });
                captcha.onSuccess(function () {
                    const proof = captcha.getValidate();
                    if (proof) window.chrome.webview.postMessage(proof);
                });
            });
            </script></html>
            """;
    }

    private static bool HasText(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());
}

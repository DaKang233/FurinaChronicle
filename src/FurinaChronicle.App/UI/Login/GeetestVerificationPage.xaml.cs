// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using System.Text.Json;
using FurinaChronicle.Services.Passport;

namespace FurinaChronicle.App;

public partial class GeetestVerificationPage : ContentPage
{
    private const string CallbackScheme = "furinachronicle-geetest";
    private readonly TaskCompletionSource<PassportGeetestResult?> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool isVersion4;
    private int isCompleted;

    public GeetestVerificationPage(PassportGeetestChallenge challenge)
    {
        ArgumentNullException.ThrowIfNull(challenge);
        InitializeComponent();
        isVersion4 = challenge.IsVersion4;
        VerificationPromptLabel.Text = challenge.IsOversea
            ? "请完成 HoYoLAB 安全验证"
            : "请完成米游社安全验证";
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
        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out Uri? uri) ||
            !uri.Scheme.Equals(CallbackScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        try
        {
            if (!uri.Host.Equals("complete", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath is not ("" or "/"))
            {
                throw new FormatException();
            }
            IReadOnlyDictionary<string, string> query = ParseQuery(uri.Query);
            if (isVersion4)
            {
                if (!query.TryGetValue("payload", out string? payload) ||
                    string.IsNullOrWhiteSpace(payload) ||
                    !IsVersion4Proof(payload))
                {
                    throw new FormatException("GeeTest 没有返回完整验证结果。");
                }

                _ = CompleteAsync(new PassportGeetestResult(
                    string.Empty,
                    string.Empty,
                    Payload: payload));
                return;
            }

            if (!query.TryGetValue("challenge", out string? challenge) ||
                !query.TryGetValue("validate", out string? validate) ||
                !query.TryGetValue("seccode", out string? seccode) ||
                string.IsNullOrWhiteSpace(challenge) ||
                string.IsNullOrWhiteSpace(validate) ||
                string.IsNullOrWhiteSpace(seccode))
            {
                throw new FormatException("GeeTest 没有返回完整验证结果。");
            }

            _ = CompleteAsync(new PassportGeetestResult(challenge, validate, seccode));
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            _ = DisplayAlertAsync("安全验证失败", "GeeTest 返回的验证结果不完整或无效。", "确定");
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
        if (challenge.IsVersion4)
        {
            return CreateVersion4Html(challenge);
        }

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
                                '&validate=' + encodeURIComponent(result.geetest_validate) +
                                '&seccode=' + encodeURIComponent(result.geetest_seccode ?? '');
                        });
                    });
                </script>
            </body>
            </html>
            """;
    }

    private static string CreateVersion4Html(PassportGeetestChallenge challenge)
    {
        string captchaId = JsonSerializer.Serialize(challenge.CaptchaId);
        string riskType = JsonSerializer.Serialize(challenge.RiskType);
        string sessionId = JsonSerializer.Serialize(challenge.SessionId);
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
                <script src="https://static.geetest.com/v4/gt4.js"></script>
            </head>
            <body>
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
                            var result = captcha.getValidate();
                            if (!result) return;
                            location.href = 'furinachronicle-geetest://complete?payload=' +
                                encodeURIComponent(JSON.stringify(result));
                        });
                    });
                </script>
            </body>
            </html>
            """;
    }

    private static bool IsVersion4Proof(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                HasText(root, "lot_number") &&
                HasText(root, "captcha_output") &&
                HasText(root, "pass_token") &&
                HasText(root, "gen_time");
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool HasText(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

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

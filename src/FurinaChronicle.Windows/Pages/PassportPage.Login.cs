// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Passport;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using QRCoder;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class PassportPage
{
    private async void OnLogin(object sender, RoutedEventArgs e)
    {
        if (signingIn) return;
        signingIn = true;
        Render();
        try
        {
            var choices = new ComboBox { Header = "登录方式", HorizontalAlignment = HorizontalAlignment.Stretch };
            choices.Items.Add("米游社扫码");
            choices.Items.Add("米游社 Cookie");
            choices.Items.Add("米游社手机验证码");
            choices.Items.Add("HoYoLAB 账号与密码");
            choices.SelectedIndex = 0;
            var chooser = LoginDialog("登录通行证", choices, "继续");
            if (await chooser.ShowAsync() != ContentDialogResult.Primary) return;

            PassportAccount? account = choices.SelectedIndex switch
            {
                0 => await SignInWithQrAsync(),
                1 => await SignInWithCookieAsync(),
                2 => await SignInWithSmsAsync(),
                _ => await SignInOverseasAsync()
            };
            if (account is null) return;
            await EnterAsync();
            try
            {
                PassportAccountProfile profile = await profiles.GetAsync(account);
                await credentials.UpdateRolesAsync(account, profile.Roles);
                await EnterAsync();
                notifications.Publish(new(
                    NotificationSeverity.Information,
                    NotificationCategory.Passport,
                    "登录完成",
                    $"已读取 {profile.Roles.Count} 个角色。")
                {
                    TaskKey = NotificationTaskKeys.PassportRefresh,
                });
            }
            catch (Exception exception)
            {
                diagnostics.Report(new DiagnosticEvent(
                    NotificationCategory.Passport,
                    "passport-login",
                    "登录后读取角色失败",
                    "凭据已加密保存，角色列表暂未读取；可稍后刷新列表。",
                    DiagnosticSeverity.Warning,
                    exception)
                {
                    TaskKey = NotificationTaskKeys.PassportRefresh,
                });
            }
        }
        catch (OperationCanceledException)
        {
            // The user cancelled the sign-in; no failure feedback is required.
        }
        catch (Exception exception)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Passport,
                "passport-login",
                "登录未完成",
                "请检查凭据、网络和安全验证结果后重试。",
                DiagnosticSeverity.Warning,
                exception));
        }
        finally
        {
            signingIn = false;
            Render();
        }
    }

    private async Task<PassportAccount?> SignInWithQrAsync()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        PassportQrSession session = await mainland.BeginQrLoginAsync(cancellation.Token);
        using QRCodeData data = QRCodeGenerator.GenerateQrCode(session.Url, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        using var buffer = new MemoryStream(png.GetGraphic(6));
        using var imageStream = buffer.AsRandomAccessStream();
        var source = new BitmapImage();
        await source.SetSourceAsync(imageStream);
        var status = new TextBlock { Text = "请使用米游社扫描并在手机上确认。", TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new Image { Source = source, Width = 240, Height = 240 });
        content.Children.Add(status);
        ContentDialog dialog = LoginDialog("米游社扫码登录", content);
        var completion = dialog.ShowAsync();
        Task<ContentDialogResult> dismissed = completion.AsTask();
        dialog.Closing += (_, _) => cancellation.Cancel();
        try
        {
            while (completion.Status == global::Windows.Foundation.AsyncStatus.Started)
            {
                Task delay = Task.Delay(TimeSpan.FromSeconds(2), cancellation.Token);
                if (await Task.WhenAny(delay, dismissed) != delay) return null;
                await delay;
                var (result, account) = await mainland.PollQrLoginAsync(session, cancellation.Token);
                if (account is not null) return account;
                if (result == PassportQrStatus.Expired)
                {
                    status.Text = "二维码已过期，请关闭后重新生成。";
                    await completion;
                    return null;
                }
                if (result == PassportQrStatus.Scanned) status.Text = "已扫描，请在手机上确认登录。";
            }
            return null;
        }
        finally
        {
            if (completion.Status == global::Windows.Foundation.AsyncStatus.Started)
            {
                dialog.Hide();
                await completion;
            }
        }
    }

    private async Task<PassportAccount?> SignInWithCookieAsync()
    {
        var cookie = new PasswordBox { Header = "米游社 Cookie", PasswordRevealMode = PasswordRevealMode.Hidden };
        if (await LoginDialog("米游社 Cookie 登录", cookie, "登录").ShowAsync() != ContentDialogResult.Primary)
            return null;
        string value = cookie.Password;
        cookie.Password = string.Empty;
        return await mainland.LoginWithManualCookieAsync(value);
    }

    private async Task<PassportAccount?> SignInWithSmsAsync()
    {
        var mobile = new TextBox { Header = "中国大陆手机号", MaxLength = 11, InputScope = new()
        {
            Names = { new InputScopeName(InputScopeNameValue.TelephoneNumber) }
        } };
        if (await LoginDialog("米游社手机登录", mobile, "发送验证码").ShowAsync() != ContentDialogResult.Primary)
            return null;
        MobileCaptchaChallenge challenge = await mainland.SendMobileCaptchaAsync(mobile.Text);
        var code = new PasswordBox { Header = "短信验证码", MaxLength = 8, PasswordRevealMode = PasswordRevealMode.Hidden };
        if (await LoginDialog("输入短信验证码", code, "登录").ShowAsync() != ContentDialogResult.Primary)
            return null;
        string value = code.Password;
        code.Password = string.Empty;
        return await mainland.LoginWithMobileCaptchaAsync(mobile.Text, value, challenge);
    }

    private async Task<PassportAccount?> SignInOverseasAsync()
    {
        var account = new TextBox { Header = "HoYoLAB 账号" };
        var password = new PasswordBox { Header = "密码", PasswordRevealMode = PasswordRevealMode.Hidden };
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(account);
        content.Children.Add(password);
        if (await LoginDialog("HoYoLAB 登录", content, "登录").ShowAsync() != ContentDialogResult.Primary)
            return null;
        string value = password.Password;
        password.Password = string.Empty;
        return await overseas.LoginWithPasswordAsync(account.Text, value, new WindowsPassportVerification(XamlRoot));
    }

    private ContentDialog LoginDialog(string title, FrameworkElement content, string? primary = null) =>
        new()
        {
            XamlRoot = XamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = primary ?? string.Empty,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class PassportPage : UserControl
{
    private readonly IDiagnosticService diagnostics;
    private readonly PassportCredentialState state;
    private readonly MainlandPassportLoginService mainland;
    private readonly HoYoLabPassportLoginService overseas;
    private readonly IMiHoYoAccountProfileClient profiles;
    private readonly DpapiPassportCredentialStore credentials;
    private readonly INotificationCenter notifications;
    private bool rendering;
    private bool signingIn;
    private string? lastLoadIssue;

    public PassportPage(PassportCredentialState state, MainlandPassportLoginService mainland,
        HoYoLabPassportLoginService overseas, IMiHoYoAccountProfileClient profiles,
        DpapiPassportCredentialStore credentials, INotificationCenter notifications,
        IDiagnosticService diagnostics)
    {
        this.state = state;
        this.mainland = mainland;
        this.overseas = overseas;
        this.profiles = profiles;
        this.credentials = credentials;
        this.notifications = notifications;
        this.diagnostics = diagnostics;
        InitializeComponent();
    }

    public event EventHandler? RefreshRequested;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await state.EnterAsync(cancellationToken);
            Render();
        }
        catch (Exception exception)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Passport,
                "passport-load",
                "登录凭据读取失败",
                "登录凭据暂未载入；可重试。",
                DiagnosticSeverity.Error,
                exception));
        }
    }

    private void Render()
    {
        rendering = true;
        try
        {
            PassportCredentialSnapshot snapshot = state.Snapshot;
            ReportLoadIssue(snapshot.Error, snapshot.LoadWarning);
            CredentialRow[] rows = snapshot.Credentials
                .Select(item => new CredentialRow(item))
                .ToArray();
            CredentialList.ItemsSource = rows;
            CredentialList.SelectedItem = rows.FirstOrDefault(
                row => row.Credential.Id == snapshot.SelectedCredentialId);
            IsolatedCredentialSummary? selected = snapshot.SelectedCredential;
            DetailTitleText.Text = selected is null
                ? "未选择凭据"
                : selected.Label ?? "通行证";
            DetailBodyText.Text = selected is null
                ? "添加凭据后在此查看绑定角色 UID、脱敏 MID、更新时间和可用状态；不显示 SToken。"
                : $"MID：{selected.MaskedMid}（脱敏）\n" +
                    $"绑定角色 UID：{selected.RoleLabel}\n" +
                    $"更新时间：{selected.UpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\n" +
                    $"状态：{(selected.IsUsable ? "可用" : "失效")}" +
                    (string.IsNullOrWhiteSpace(selected.FailureReason)
                        ? string.Empty
                        : $"\n原因：{selected.FailureReason}") +
                    "\nSToken：不在界面显示。";
            bool canEdit = !snapshot.IsBusy && !signingIn;
            DeleteButton.IsEnabled = canEdit && selected is not null;
            ClearButton.IsEnabled = canEdit && snapshot.HasCredentials;
            AddButton.IsEnabled = canEdit;
            LoginButton.IsEnabled = canEdit;
            RefreshButton.IsEnabled = canEdit;
        }
        finally
        {
            rendering = false;
        }
    }

    private async void OnCredentialSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (rendering || CredentialList.SelectedItem is not CredentialRow row) return;
        await state.SelectAsync(row.Credential.Id);
        Render();
    }

    private async void OnAddCredential(object sender, RoutedEventArgs e)
    {
        var sTokenBox = new PasswordBox
        {
            Header = "SToken",
            PlaceholderText = "仅用于本机 DPAPI 加密保存；不会回显",
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        var midBox = new PasswordBox
        {
            Header = "MID",
            PlaceholderText = "米哈游通行证 MID（纯数字）",
            PasswordRevealMode = PasswordRevealMode.Hidden
        };
        var uidBox = new TextBox
        {
            Header = "绑定角色 UID",
            PlaceholderText = "例如 100000001"
        };
        var labelBox = new TextBox
        {
            Header = "标签（可选）",
            MaxLength = 50
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "手动添加抽卡刷新凭据",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "用于绑定角色的抽卡刷新。凭据由 Windows 加密保存在本机。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    sTokenBox,
                    midBox,
                    uidBox,
                    labelBox
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4PassportAddDialog");
        AutomationProperties.SetAutomationId(sTokenBox, "Ui4PassportSToken");
        AutomationProperties.SetAutomationId(midBox, "Ui4PassportMid");
        AutomationProperties.SetAutomationId(uidBox, "Ui4PassportBoundUid");
        AutomationProperties.SetAutomationId(labelBox, "Ui4PassportLabel");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        PassportCredentialActionResult result = await state.SaveAsync(
            sTokenBox.Password,
            midBox.Password,
            uidBox.Text,
            labelBox.Text);
        Render();
        Report("保存登录凭据", NotificationTaskKeys.PassportSave, result);
    }

    private async void OnDeleteCredential(object sender, RoutedEventArgs e)
    {
        if (state.Snapshot.SelectedCredentialId is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "删除本机凭据",
            PrimaryButtonText = "删除凭据",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "只删除本机隔离宿主的这一条登录凭据，不删除档案、账号或抽卡事实。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4PassportDeleteDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        PassportCredentialActionResult result = await state.DeleteSelectedAsync();
        Render();
        Report("删除登录凭据", NotificationTaskKeys.PassportDelete, result);
    }

    private async void OnClearCredentials(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清除全部本机凭据",
            PrimaryButtonText = "清除全部",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = "只清除本机隔离宿主的全部登录凭据，不删除档案、账号或抽卡事实。",
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4PassportClearDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        PassportCredentialActionResult result = await state.ClearAsync();
        Render();
        Report("清除全部登录凭据", NotificationTaskKeys.PassportClear, result);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        Guid? selected = state.Snapshot.SelectedCredentialId;
        signingIn = true;
        Render();
        string? notice = null;
        try
        {
            if (selected is Guid id && await credentials.GetByIdAsync(id) is { Credentials.LToken: not null } account)
            {
                var profile = await profiles.GetAsync(account);
                await credentials.UpdateRolesAsync(account, profile.Roles);
                notice = $"已更新 {profile.Roles.Count} 个角色。";
            }
            await state.RefreshAsync();
        }
        catch (Exception exception)
        {
            notice = "角色列表暂未更新，请检查网络或重新登录。";
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Passport,
                "passport-refresh",
                "刷新角色列表失败",
                notice,
                DiagnosticSeverity.Warning,
                exception));
        }
        finally
        {
            signingIn = false;
            Render();
            if (notice is not null && notice.StartsWith("已更新", StringComparison.Ordinal))
            {
                notifications.Publish(new(
                    NotificationSeverity.Information,
                    NotificationCategory.Passport,
                    "刷新角色列表",
                    notice)
                {
                    TaskKey = NotificationTaskKeys.PassportRefresh,
                });
            }
        }
    }

    private void Report(string operation, string taskKey, PassportCredentialActionResult result)
    {
        if (!result.Succeeded)
        {
            diagnostics.Report(new DiagnosticEvent(
                NotificationCategory.Passport,
                "passport",
                operation,
                result.Message,
                DiagnosticSeverity.Warning)
            {
                TaskKey = taskKey,
            });
            return;
        }

        notifications.Publish(new(
            NotificationSeverity.Information,
            NotificationCategory.Passport,
            operation,
            result.Message)
        {
            TaskKey = taskKey,
        });
    }

    private void ReportLoadIssue(string? error, string? warning)
    {
        string? issue = !string.IsNullOrWhiteSpace(error) ? error : warning;
        if (string.IsNullOrWhiteSpace(issue) || issue == lastLoadIssue)
        {
            return;
        }
        lastLoadIssue = issue;
        diagnostics.Report(new DiagnosticEvent(
            NotificationCategory.Passport,
            "passport-load",
            "登录凭据读取异常",
            issue,
            DiagnosticSeverity.Warning));
    }

    private void OnOpenGachaRefresh(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    public sealed record CredentialRow(IsolatedCredentialSummary Credential)
    {
        public string DisplayLabel =>
            Credential.Label ?? "通行证";

        public string DetailLabel =>
            $"MID {Credential.MaskedMid} · 角色 {Credential.RoleLabel} · " +
            $"{(Credential.IsUsable ? "可用" : "失效")} · " +
            $"{Credential.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
    }
}

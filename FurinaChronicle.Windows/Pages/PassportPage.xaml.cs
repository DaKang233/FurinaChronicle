// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class PassportPage : UserControl
{
    private readonly PassportCredentialState state;
    private bool rendering;

    public PassportPage(PassportCredentialState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public event EventHandler? RefreshRequested;

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        rendering = true;
        try
        {
            PassportCredentialSnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            LoadWarningText.Text = snapshot.LoadWarning ?? string.Empty;
            LoadWarningText.Visibility = string.IsNullOrWhiteSpace(snapshot.LoadWarning)
                ? Visibility.Collapsed
                : Visibility.Visible;
            CredentialRow[] rows = snapshot.Credentials
                .Select(item => new CredentialRow(item))
                .ToArray();
            CredentialList.ItemsSource = rows;
            CredentialList.SelectedItem = rows.FirstOrDefault(
                row => row.Credential.Id == snapshot.SelectedCredentialId);
            IsolatedCredentialSummary? selected = snapshot.SelectedCredential;
            DetailTitleText.Text = selected is null
                ? "未选择凭据"
                : selected.Label ?? $"UID {selected.BoundRoleUid}";
            DetailBodyText.Text = selected is null
                ? "添加凭据后在此查看绑定角色 UID、脱敏 MID、更新时间和可用状态；不显示 SToken。"
                : $"MID：{selected.MaskedMid}（脱敏）\n" +
                    $"绑定角色 UID：{selected.BoundRoleUid}\n" +
                    $"更新时间：{selected.UpdatedAt:yyyy-MM-dd HH:mm:ss zzz}\n" +
                    $"状态：{(selected.IsUsable ? "可用" : "失效")}" +
                    (string.IsNullOrWhiteSpace(selected.FailureReason)
                        ? string.Empty
                        : $"\n原因：{selected.FailureReason}") +
                    "\nSToken：不在界面显示。";
            DeleteButton.IsEnabled = !snapshot.IsBusy && selected is not null;
            ClearButton.IsEnabled = !snapshot.IsBusy && snapshot.HasCredentials;
            AddButton.IsEnabled = !snapshot.IsBusy;
            RefreshButton.IsEnabled = !snapshot.IsBusy;
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
            Title = "手动添加隔离宿主凭据",
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
                        Text = "仅保存到本机隔离宿主（DPAPI CurrentUser 加密）；" +
                            "不会显示、记录或回传 SToken/MID。生产凭据迁移待 UI-6 评审。",
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
        if (!result.Succeeded)
        {
            await ShowMessageAsync("凭据未保存", result.Message);
        }
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
        if (!result.Succeeded) await ShowMessageAsync("凭据未删除", result.Message);
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
        if (!result.Succeeded) await ShowMessageAsync("凭据未清除", result.Message);
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await state.RefreshAsync();
        Render();
    }

    private void OnOpenGachaRefresh(object sender, RoutedEventArgs e) =>
        RefreshRequested?.Invoke(this, EventArgs.Empty);

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            CloseButtonText = "确定",
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }
        };
        await dialog.ShowAsync();
    }

    public sealed record CredentialRow(IsolatedCredentialSummary Credential)
    {
        public string DisplayLabel =>
            Credential.Label ?? $"绑定 UID {Credential.BoundRoleUid}";

        public string DetailLabel =>
            $"MID {Credential.MaskedMid} · UID {Credential.BoundRoleUid} · " +
            $"{(Credential.IsUsable ? "可用" : "失效")} · " +
            $"{Credential.UpdatedAt:yyyy-MM-dd HH:mm:ss}";
    }
}

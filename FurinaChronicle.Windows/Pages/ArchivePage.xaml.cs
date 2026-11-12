// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Core.Archives;
using FurinaChronicle.Presentation.Management;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace FurinaChronicle.Windows.Pages;

public sealed partial class ArchivePage : UserControl
{
    private static readonly RegionOption[] Regions =
    [
        new(GameServerRegion.ChinaOfficial),
        new(GameServerRegion.ChinaBilibili),
        new(GameServerRegion.America),
        new(GameServerRegion.Europe),
        new(GameServerRegion.Asia),
        new(GameServerRegion.TaiwanHongKongMacao)
    ];

    private readonly ArchiveManagementState state;
    private bool renderGuard;

    public ArchivePage(ArchiveManagementState state)
    {
        this.state = state;
        InitializeComponent();
    }

    public event EventHandler? TransferRequested;

    private void OnOpenArchiveTransfer(object sender, RoutedEventArgs e) =>
        TransferRequested?.Invoke(this, EventArgs.Empty);

    public async Task EnterAsync(CancellationToken cancellationToken = default)
    {
        await state.EnterAsync(cancellationToken);
        Render();
    }

    private void Render()
    {
        renderGuard = true;
        try
        {
            ArchiveManagementSnapshot snapshot = state.Snapshot;
            StatusText.Text = snapshot.Status;
            ErrorText.Text = snapshot.Error ?? string.Empty;
            ErrorText.Visibility = string.IsNullOrWhiteSpace(snapshot.Error)
                ? Visibility.Collapsed
                : Visibility.Visible;
            ArchiveList.ItemsSource = snapshot.Archives;
            ArchiveList.SelectedItem = snapshot.Archives.FirstOrDefault(
                archive => archive.Id == snapshot.SelectedArchiveId);
            SelectedArchiveText.Text = snapshot.SelectedArchive?.Name ?? "未选择档案";
            AccountRow[] rows = snapshot.Accounts
                .Select(account => new AccountRow(
                    account,
                    ManagementTexts.AccountLabel(account),
                    ManagementTexts.IdentityStatus(account, state.DiagnosticFor(account))))
                .ToArray();
            AccountList.ItemsSource = rows;
            AccountList.SelectedItem = rows.FirstOrDefault(
                row => row.Account.Id == snapshot.SelectedAccountId);
            UnresolvedBanner.Visibility = snapshot.UnresolvedCount > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            UnresolvedText.Text = BuildUnresolvedText(snapshot);
            bool idle = !snapshot.IsBusy;
            bool hasArchive = snapshot.SelectedArchiveId is not null;
            bool hasAccount = snapshot.SelectedAccountId is not null;
            CreateArchiveButton.IsEnabled = idle;
            RenameArchiveButton.IsEnabled = idle && hasArchive;
            DeleteArchiveButton.IsEnabled = idle && hasArchive;
            OpenArchiveTransferButton.IsEnabled = idle;
            AddAccountButton.IsEnabled = idle && hasArchive;
            EditAccountButton.IsEnabled = idle && hasAccount;
            DeleteAccountButton.IsEnabled = idle && hasAccount;
            AutomationProperties.SetName(SelectedArchiveText, SelectedArchiveText.Text);
        }
        finally
        {
            renderGuard = false;
        }
    }

    private string BuildUnresolvedText(ArchiveManagementSnapshot snapshot)
    {
        string details = string.Join(
            Environment.NewLine,
            snapshot.Unresolved
                .Take(5)
                .Select(item =>
                    $"· {item.PlayerArchiveName} · UID {item.Uid}：" +
                    ManagementTexts.UnresolvedReasonLabel(item.Reason)));
        return $"身份待确认：{snapshot.UnresolvedCount} 个账号副本。请编辑账号补全 UID/区服，或删除后重新添加。" +
            (string.IsNullOrWhiteSpace(details) ? string.Empty : Environment.NewLine + details);
    }

    private async void OnArchiveSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || ArchiveList.SelectedItem is not PlayerArchive archive) return;
        await state.SelectArchiveAsync(archive.Id);
        Render();
    }

    private async void OnAccountSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (renderGuard || AccountList.SelectedItem is not AccountRow row) return;
        await state.SelectAccountAsync(row.Account.Id);
        Render();
    }

    private async void OnCreateArchive(object sender, RoutedEventArgs e)
    {
        string? name = await PromptAsync("创建档案", "请输入档案名称（由服务校验精确同名与长度）。", string.Empty);
        if (name is null) return;
        await state.CreateArchiveAsync(name);
        Render();
    }

    private async void OnRenameArchive(object sender, RoutedEventArgs e)
    {
        PlayerArchive? selected = state.Snapshot.SelectedArchive;
        if (selected is null) return;
        string? name = await PromptAsync("重命名档案", "请输入新的档案名称。", selected.Name);
        if (name is null) return;
        await state.RenameArchiveAsync(name);
        Render();
    }

    private async void OnDeleteArchive(object sender, RoutedEventArgs e)
    {
        if (state.Snapshot.SelectedArchiveId is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "永久删除档案",
            PrimaryButtonText = "永久删除档案",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = state.BuildDeleteArchiveWarning(),
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4DeleteArchiveDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await state.DeleteArchiveAsync();
        Render();
    }

    private async void OnAddAccount(object sender, RoutedEventArgs e)
    {
        if (state.Snapshot.SelectedArchiveId is null) return;
        AccountDialogResult? input = await ShowAccountDialogAsync(
            "添加游戏账号",
            uid: string.Empty,
            region: GameServerRegion.ChinaOfficial,
            displayName: string.Empty,
            allowIdentityEdit: true,
            primaryText: "添加");
        if (input is null) return;
        await state.AddAccountAsync(input.Uid, input.Region, input.DisplayName);
        Render();
    }

    private async void OnEditAccount(object sender, RoutedEventArgs e)
    {
        AccountRow? row = state.Snapshot.SelectedAccount is GameAccount account
            ? new AccountRow(
                account,
                ManagementTexts.AccountLabel(account),
                ManagementTexts.IdentityStatus(account, state.DiagnosticFor(account)))
            : null;
        if (row is null) return;
        bool resolved = row.Account.RoleIdentity is not null;
        AccountDialogResult? input = await ShowAccountDialogAsync(
            resolved ? "编辑游戏账号（已解析身份）" : "编辑游戏账号（身份待确认）",
            row.Account.Uid,
            row.Account.ServerRegion,
            row.Account.DisplayName ?? string.Empty,
            allowIdentityEdit: !resolved,
            primaryText: "保存");
        if (input is null) return;
        await state.UpdateAccountAsync(
            resolved ? row.Account.Uid : input.Uid,
            resolved ? row.Account.ServerRegion : input.Region,
            input.DisplayName);
        Render();
    }

    private async void OnDeleteAccount(object sender, RoutedEventArgs e)
    {
        if (state.Snapshot.SelectedAccountId is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "永久删除游戏账号",
            PrimaryButtonText = "永久删除账号",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            Content = new TextBlock
            {
                Text = state.BuildDeleteAccountWarning(),
                TextWrapping = TextWrapping.Wrap
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4DeleteAccountDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await state.DeleteAccountAsync();
        Render();
    }

    private async Task<string?> PromptAsync(string title, string message, string initialValue)
    {
        var input = new TextBox
        {
            Text = initialValue,
            Header = "名称",
            MaxLength = 50
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                    input
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4ArchiveNameDialog");
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? input.Text : null;
    }

    private async Task<AccountDialogResult?> ShowAccountDialogAsync(
        string title,
        string uid,
        GameServerRegion region,
        string displayName,
        bool allowIdentityEdit,
        string primaryText)
    {
        var uidBox = new TextBox
        {
            Header = "UID（9～10 位数字）",
            Text = uid,
            MaxLength = 10,
            IsEnabled = allowIdentityEdit
        };
        var regionBox = new ComboBox
        {
            Header = "区服",
            ItemsSource = Regions,
            DisplayMemberPath = nameof(RegionOption.Label),
            SelectedItem = Regions.FirstOrDefault(option => option.Region == region) ?? Regions[0],
            IsEnabled = allowIdentityEdit
        };
        var displayBox = new TextBox
        {
            Header = "备注（可留空）",
            Text = displayName,
            MaxLength = 50
        };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = title,
            PrimaryButtonText = primaryText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = allowIdentityEdit
                            ? "身份待确认账号可用 UID 与区服修复；保存后由服务重新解析角色身份。"
                            : "已解析身份账号只能修改备注；UID 与区服由账号身份确定，服务会拒绝改变已解析身份。",
                        TextWrapping = TextWrapping.Wrap
                    },
                    uidBox,
                    regionBox,
                    displayBox
                }
            }
        };
        AutomationProperties.SetAutomationId(dialog, "Ui4AccountDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        RegionOption selected = regionBox.SelectedItem as RegionOption ?? Regions[0];
        return new AccountDialogResult(uidBox.Text, selected.Region, displayBox.Text);
    }

    public sealed record AccountRow(GameAccount Account, string Label, string IdentityStatus);

    private sealed record AccountDialogResult(string Uid, GameServerRegion Region, string DisplayName);

    public sealed record RegionOption(GameServerRegion Region)
    {
        public string Label => ManagementTexts.RegionLabel(Region);
    }
}

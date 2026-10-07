// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.Core.Archives;

namespace FurinaChronicle.App.UI.Pages;

public partial class ArchivePage : ContentPage
{
    private ArchivePageViewModel ViewModel =>
        (ArchivePageViewModel)BindingContext;

    public ArchivePage(
        ArchivePageViewModel viewModel,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await ViewModel.InitializeAsync();
    }

    private async void OnArchiveSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            await ViewModel.SelectArchiveAsync(
                picker.SelectedItem as PlayerArchive);
        }
    }

    private void OnAccountSelectionChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker)
        {
            ViewModel.SelectAccount(picker.SelectedItem as GameAccount);
        }
    }

    private async void OnCreateArchiveClicked(object? sender, EventArgs e)
    {
        string? name = await DisplayPromptAsync(
            "创建档案",
            "请输入档案名称。",
            "创建",
            "取消",
            placeholder: "例如：我的原神档案",
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (name is not null)
        {
            await ViewModel.CreateArchiveAsync(name);
        }
    }

    private async void OnRenameArchiveClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedArchive is null)
        {
            await DisplayAlertAsync("无法重命名", "请先选择档案。", "确定");
            return;
        }

        string? name = await DisplayPromptAsync(
            "重命名档案",
            "请输入新的档案名称。",
            "重命名",
            "取消",
            initialValue: ViewModel.SelectedArchive.Name,
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (name is not null)
        {
            await ViewModel.RenameSelectedArchiveAsync(name);
        }
    }

    private async void OnDeleteArchiveClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedArchive is null)
        {
            await DisplayAlertAsync("无法删除", "请先选择档案。", "确定");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "删除档案",
            "确定要永久删除所选档案吗？档案内的全部账号和业务数据也会被删除。",
            "删除",
            "取消");
        if (confirmed)
        {
            await ViewModel.DeleteSelectedArchiveAsync();
        }
    }

    private async void OnCreateAccountClicked(object? sender, EventArgs e)
    {
        await ViewModel.CreateAccountAsync();
    }

    private async void OnEditAccountClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync("无法编辑", "请先选择游戏账号。", "确定");
            return;
        }

        string? displayName = await DisplayPromptAsync(
            "编辑游戏账号",
            "请输入账号备注；留空表示只显示 UID。",
            "下一步",
            "取消",
            maxLength: 50,
            keyboard: Keyboard.Text);
        if (displayName is null)
        {
            return;
        }

        string? uid = await DisplayPromptAsync(
            "编辑游戏账号",
            "请输入 UID；留空表示不修改 UID。",
            "保存",
            "取消",
            placeholder: ViewModel.SelectedAccount.Uid,
            maxLength: 10,
            keyboard: Keyboard.Numeric);
        if (uid is null)
        {
            return;
        }

        if (uid.Length > 0 && !GameUidValidation.IsValidUid(uid))
        {
            await DisplayAlertAsync(
                "无效的 UID",
                "UID 必须是符合游戏账号规则的 9～10 位数字。",
                "确定");
            return;
        }

        await ViewModel.EditSelectedAccountAsync(displayName, uid);
    }

    private async void OnDeleteAccountClicked(object? sender, EventArgs e)
    {
        if (ViewModel.SelectedAccount is null)
        {
            await DisplayAlertAsync("无法删除", "请先选择游戏账号。", "确定");
            return;
        }

        bool confirmed = await DisplayAlertAsync(
            "删除游戏账号",
            "确定要永久删除所选游戏账号吗？该账号的全部业务数据也会被删除。",
            "删除",
            "取消");
        if (confirmed)
        {
            await ViewModel.DeleteSelectedAccountAsync();
        }
    }
}

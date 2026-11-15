// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Controls;

public partial class PassportFlyoutHeader : ContentView
{
    public PassportFlyoutHeader(UserPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current is AppShell shell)
        {
            await shell.ShowUserPageAsync();
        }
    }

    private void OnSidebarToggleClicked(object? sender, EventArgs e)
    {
        if (Shell.Current is AppShell shell)
        {
            shell.ToggleWindowsSidebar();
        }
    }

    public void SetSidebarCollapsed(bool isCollapsed)
    {
        SidebarToggleButton.Text = isCollapsed
            ? "固定侧边栏"
            : "收起侧边栏";
    }
}

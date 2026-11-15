// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

namespace FurinaChronicle.App.UI.Pages;

// "数据" center destination. It only routes to the existing domain pages; it owns no
// domain state and introduces no global current-account selection.
public partial class DataHubPage : ContentPage
{
    private bool navigationRunning;

    public DataHubPage()
    {
        InitializeComponent();
    }

    private async void OnOpenGachaClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("gacha-page");
    }

    private async void OnOpenCharacterWeaponClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("character-history");
    }

    private async void OnOpenChallengeClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("challenge-history");
    }

    private async void OnOpenChronicleClicked(object? sender, EventArgs e)
    {
        await NavigateAsync("//main/chronicle");
    }

    private async Task NavigateAsync(string route)
    {
        if (navigationRunning || Shell.Current is not Shell shell)
        {
            return;
        }

        navigationRunning = true;
        try
        {
            await shell.GoToAsync(route, animate: true);
        }
        finally
        {
            navigationRunning = false;
        }
    }
}

// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Windows.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace FurinaChronicle.Windows;

public sealed partial class MainWindow : Window
{
    private readonly WindowsComposition composition;
    private readonly StartupPage startupPage;
    private readonly OverviewPage overviewPage;
    private readonly ArchivePage archivePage;
    private bool shellReady;
    private bool switching;
    private bool disposed;
    private bool startupStarted;
    private string? currentPageTag;

    public MainWindow(WindowsComposition composition)
    {
        this.composition = composition;
        InitializeComponent();
        Title = composition.Version.UserVisibleName;

        composition.Services.GetRequiredService<IFilePickerService>().SetOwner(this);
        GachaView.Initialize(composition.Services.GetRequiredService<GachaPageServices>());

        startupPage = new StartupPage();
        startupPage.SetVersion(
            $"{composition.Version.UserVisibleName} · Phase {composition.Version.PhaseLabel} · " +
            $"Commit {composition.Version.Commit}");
        startupPage.RetryRequested += OnRetryStartup;
        StartupHost.Children.Add(startupPage);

        overviewPage = new OverviewPage(
            composition.Services.GetRequiredService<OverviewState>())
        {
            Visibility = Visibility.Collapsed
        };
        overviewPage.ContinueRequested += OnContinueToGacha;
        PageHost.Children.Add(overviewPage);

        archivePage = new ArchivePage(
            composition.Services.GetRequiredService<ArchiveManagementState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(archivePage);

        RootGrid.Loaded += OnRootLoaded;
        Closed += OnClosed;
        ApplyBackdrop();
        ResizeInitialWindow();
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (startupStarted) return;
        startupStarted = true;
        await RunStartupAsync();
    }

    private async void OnRetryStartup(object? sender, EventArgs e) =>
        await RunStartupAsync();

    private async Task RunStartupAsync()
    {
        StartupHost.Visibility = Visibility.Visible;
        Navigation.Visibility = Visibility.Collapsed;
        shellReady = false;
        StartupState state = composition.Services.GetRequiredService<StartupState>();
        await state.InitializeAsync();
        startupPage.Render(state.Snapshot);
        if (!state.Snapshot.IsReady) return;

        StartupHost.Visibility = Visibility.Collapsed;
        Navigation.Visibility = Visibility.Visible;
        shellReady = true;
        switching = true;
        Navigation.SelectedItem = OverviewDestination;
        switching = false;
        await ShowPageAsync("overview");
    }

    private async void OnNavigationSelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (!shellReady || switching) return;
        if (args.SelectedItemContainer is not NavigationViewItem item) return;
        await ShowPageAsync(item.Tag?.ToString());
    }

    private async Task ShowPageAsync(string? tag)
    {
        if (tag is null || tag == currentPageTag) return;
        switching = true;
        try
        {
            GachaView.Leave();
            GachaView.Visibility = Visibility.Collapsed;
            overviewPage.Visibility = Visibility.Collapsed;
            archivePage.Visibility = Visibility.Collapsed;
            switch (tag)
            {
                case "overview":
                    currentPageTag = tag;
                    overviewPage.Visibility = Visibility.Visible;
                    await overviewPage.EnterAsync();
                    break;
                case "gacha":
                    currentPageTag = tag;
                    GachaView.Visibility = Visibility.Visible;
                    await GachaView.EnterAsync();
                    break;
                case "archives":
                    currentPageTag = tag;
                    archivePage.Visibility = Visibility.Visible;
                    await archivePage.EnterAsync();
                    break;
            }
        }
        finally
        {
            switching = false;
        }
    }

    private void OnContinueToGacha(object? sender, EventArgs e)
    {
        if (!shellReady) return;
        Navigation.SelectedItem = GachaDestination;
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        if (disposed) return;
        disposed = true;
        await GachaView.CloseAsync();
        await composition.DisposeAsync();
    }

    private void ResizeInitialWindow()
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1100, 720));
    }

    private void ApplyBackdrop()
    {
        try
        {
            RootGrid.Background = new SolidColorBrush(Colors.Transparent);
            SystemBackdrop = new MicaBackdrop();
        }
        catch (Exception)
        {
            SystemBackdrop = null;
            RootGrid.Background =
                (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        }
    }
}

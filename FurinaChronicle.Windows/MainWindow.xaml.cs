// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.History;
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
    private OverviewPage? overviewPage;
    private ArchivePage? archivePage;
    private HistoryPage? historyPage;
    private SettingsPage? settingsPage;
    private ArchiveTransferPage? archiveTransferPage;
    private ChroniclePage? chroniclePage;
    private CharacterWeaponPage? characterWeaponPage;
    private ChallengePage? challengePage;
    private bool shellReady;
    private bool shellBuilt;
    private bool switching;
    private bool disposed;
    private bool startupStarted;
    private bool startupRunning;
    private string? currentPageTag;

    public MainWindow(WindowsComposition composition)
    {
        this.composition = composition;
        InitializeComponent();
        Title = composition.Version.UserVisibleName;

        // Only platform services that cannot touch the data directory are resolved here. Page
        // states, the gacha coordinators and the isolated environment are built after the startup
        // guard passed (BuildShell), so an invalid FURINA_UI1_DIRECTORY can never throw out of
        // Application.OnLaunched before the startup failure page exists.
        composition.Services.GetRequiredService<IFilePickerService>().SetOwner(this);

        startupPage = new StartupPage();
        startupPage.SetVersion(
            $"{composition.Version.UserVisibleName} · Phase {composition.Version.PhaseLabel} · " +
            $"Commit {composition.Version.Commit}");
        startupPage.RetryRequested += OnRetryStartup;
        StartupHost.Children.Add(startupPage);

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
        if (startupRunning) return;
        startupRunning = true;
        try
        {
            StartupHost.Visibility = Visibility.Visible;
            Navigation.Visibility = Visibility.Collapsed;
            shellReady = false;
            StartupState state = composition.Services.GetRequiredService<StartupState>();
            await state.InitializeAsync();
            startupPage.Render(state.Snapshot);
            if (!state.Snapshot.IsReady) return;

            BuildShell();
            await ApplyPersistedThemeAsync();
            StartupHost.Visibility = Visibility.Collapsed;
            Navigation.Visibility = Visibility.Visible;
            shellReady = true;
            switching = true;
            Navigation.SelectedItem = OverviewDestination;
            switching = false;
            await ShowPageAsync("overview");
        }
        catch (Exception exception)
        {
            // A page/service construction failure must degrade to the recoverable startup page,
            // never to an unhandled XAML exception on the UI thread.
            try
            {
                File.WriteAllText(
                    Path.Combine(Path.GetTempPath(), "furina-ui4-buildshell-error.txt"),
                    exception.ToString());
            }
            catch (Exception)
            {
            }

            StartupHost.Visibility = Visibility.Visible;
            Navigation.Visibility = Visibility.Collapsed;
            shellReady = false;
            startupPage.Render(new StartupSnapshot(
                false,
                false,
                StartupFailureKind.Unknown,
                "启动后页面初始化失败。",
                $"{exception.GetType().Name}: {exception.Message}",
                true));
        }
        finally
        {
            startupRunning = false;
        }
    }

    private void BuildShell()
    {
        if (shellBuilt) return;
        GachaView.Initialize(composition.Services.GetRequiredService<GachaPageServices>());

        overviewPage = new OverviewPage(
            composition.Services.GetRequiredService<OverviewState>())
        {
            Visibility = Visibility.Collapsed
        };
        overviewPage.ContinueRequested += OnContinueToGacha;
        overviewPage.BackupRestoreRequested += OnOpenArchiveTransfer;
        PageHost.Children.Add(overviewPage);

        archivePage = new ArchivePage(
            composition.Services.GetRequiredService<ArchiveManagementState>())
        {
            Visibility = Visibility.Collapsed
        };
        archivePage.TransferRequested += OnOpenArchiveTransfer;
        PageHost.Children.Add(archivePage);

        historyPage = new HistoryPage(
            composition.Services.GetRequiredService<ArchiveHistoryState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(historyPage);

        settingsPage = new SettingsPage(
            composition.Services.GetRequiredService<SettingsState>(),
            ApplyTheme)
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(settingsPage);

        archiveTransferPage = new ArchiveTransferPage(
            composition.Services.GetRequiredService<ArchiveTransferState>(),
            composition.Services.GetRequiredService<IFilePickerService>(),
            composition.Version)
        {
            Visibility = Visibility.Collapsed
        };
        archiveTransferPage.BackRequested += OnTransferBack;
        archiveTransferPage.RestoreApplied += OnRestoreApplied;
        PageHost.Children.Add(archiveTransferPage);

        chroniclePage = new ChroniclePage(
            composition.Services.GetRequiredService<ChronicleWorkspaceState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(chroniclePage);

        characterWeaponPage = new CharacterWeaponPage(
            composition.Services.GetRequiredService<CharacterWeaponHistoryState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(characterWeaponPage);

        challengePage = new ChallengePage(
            composition.Services.GetRequiredService<ChallengeHistoryState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(challengePage);
        shellBuilt = true;
    }

    private async void OnOpenArchiveTransfer(object? sender, EventArgs e)
    {
        if (!shellReady) return;
        await ShowPageAsync("transfer");
    }

    private async void OnTransferBack(object? sender, EventArgs e)
    {
        if (!shellReady) return;
        switching = true;
        Navigation.SelectedItem = ArchiveDestination;
        switching = false;
        await ShowPageAsync("archives");
    }

    private void OnRestoreApplied(object? sender, EventArgs e)
    {
        // Overview and archive pages re-query on entry; no cached global selection is kept.
    }

    private async Task ApplyPersistedThemeAsync()
    {
        try
        {
            ISettingsAppearanceStore store =
                composition.Services.GetRequiredService<ISettingsAppearanceStore>();
            ApplyTheme(await store.LoadThemeAsync());
        }
        catch (Exception)
        {
            ApplyTheme(SettingsTheme.FollowSystem);
        }
    }

    private void ApplyTheme(SettingsTheme theme) =>
        RootGrid.RequestedTheme = theme switch
        {
            SettingsTheme.Light => ElementTheme.Light,
            SettingsTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

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
        if (tag is null || tag == currentPageTag || !shellBuilt) return;
        switching = true;
        try
        {
            GachaView.Leave();
            GachaView.Visibility = Visibility.Collapsed;
            chroniclePage?.Leave();
            characterWeaponPage?.Leave();
            challengePage?.Leave();
            if (overviewPage is not null) overviewPage.Visibility = Visibility.Collapsed;
            if (archivePage is not null) archivePage.Visibility = Visibility.Collapsed;
            if (historyPage is not null) historyPage.Visibility = Visibility.Collapsed;
            if (settingsPage is not null) settingsPage.Visibility = Visibility.Collapsed;
            if (archiveTransferPage is not null) archiveTransferPage.Visibility = Visibility.Collapsed;
            if (chroniclePage is not null) chroniclePage.Visibility = Visibility.Collapsed;
            if (characterWeaponPage is not null) characterWeaponPage.Visibility = Visibility.Collapsed;
            if (challengePage is not null) challengePage.Visibility = Visibility.Collapsed;
            switch (tag)
            {
                case "overview":
                    currentPageTag = tag;
                    overviewPage!.Visibility = Visibility.Visible;
                    await overviewPage.EnterAsync();
                    break;
                case "gacha":
                    currentPageTag = tag;
                    GachaView.Visibility = Visibility.Visible;
                    await GachaView.EnterAsync();
                    break;
                case "archives":
                    currentPageTag = tag;
                    archivePage!.Visibility = Visibility.Visible;
                    await archivePage.EnterAsync();
                    break;
                case "history":
                    currentPageTag = tag;
                    historyPage!.Visibility = Visibility.Visible;
                    await historyPage.EnterAsync();
                    break;
                case "settings":
                    currentPageTag = tag;
                    settingsPage!.Visibility = Visibility.Visible;
                    await settingsPage.EnterAsync();
                    break;
                case "transfer":
                    currentPageTag = tag;
                    archiveTransferPage!.Visibility = Visibility.Visible;
                    await archiveTransferPage.EnterAsync();
                    break;
                case "chronicle":
                    currentPageTag = tag;
                    chroniclePage!.Visibility = Visibility.Visible;
                    await chroniclePage.EnterAsync();
                    break;
                case "characters":
                    currentPageTag = tag;
                    characterWeaponPage!.Visibility = Visibility.Visible;
                    await characterWeaponPage.EnterAsync();
                    break;
                case "challenges":
                    currentPageTag = tag;
                    challengePage!.Visibility = Visibility.Visible;
                    await challengePage.EnterAsync();
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

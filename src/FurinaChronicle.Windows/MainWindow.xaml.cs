// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.History;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Presentation.Notifications;
using FurinaChronicle.Services.HistoryDomains.Portable;
using FurinaChronicle.Services.Passport;
using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Windows.Pages;
using FurinaChronicle.Windows.Shell;
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
    private readonly INotificationCenter notificationCenter;
    private readonly IDiagnosticService diagnostics;
    private readonly StartupPage startupPage;
    private OverviewPage? overviewPage;
    private ArchivePage? archivePage;
    private HistoryPage? historyPage;
    private SettingsPage? settingsPage;
    private ArchiveTransferPage? archiveTransferPage;
    private ChroniclePage? chroniclePage;
    private CharacterWeaponPage? characterWeaponPage;
    private ChallengePage? challengePage;
    private AiPage? aiPage;
    private PassportPage? passportPage;
    private NotificationCenterPanel? notificationCenterPanel;
    private HostAdoptionPage? adoptionPage;
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
        AppWindow.SetIcon(Path.Combine(
            AppContext.BaseDirectory, "Assets", "AppIcon", "FurinaChronicle.ico"));

        // Only platform services that cannot touch the data directory are resolved here. Page
        // states, the gacha coordinators and the isolated environment are built after the startup
        // guard passed (BuildShell), so an invalid FURINA_UI1_DIRECTORY can never throw out of
        // Application.OnLaunched before the startup failure page exists.
        composition.Services.GetRequiredService<IFilePickerService>().SetOwner(this);
        notificationCenter = composition.Services.GetRequiredService<INotificationCenter>();
        diagnostics = composition.Services.GetRequiredService<IDiagnosticService>();

        // The notification center is created before the startup gate so a blocking startup
        // failure can still report details there. With no notifications the whole overlay is
        // hidden and never blocks the page.
        notificationCenterPanel = new NotificationCenterPanel(
            notificationCenter,
            diagnostics,
            composition.Services.GetRequiredService<IDiagnosticReportExporter>())
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 16, 16),
            Visibility = Visibility.Collapsed
        };
        Canvas.SetZIndex(notificationCenterPanel, 100);
        RootGrid.Children.Add(notificationCenterPanel);
        RootGrid.SizeChanged += OnRootGridSizeChanged;


        startupPage = new StartupPage(composition.Paths.IsIsolated);
        startupPage.SetVersion(
            $"{composition.Version.UserVisibleName} · Phase {composition.Version.PhaseLabel} · " +
            $"Commit {composition.Version.Commit}");
        startupPage.RetryRequested += OnRetryStartup;
        StartupHost.Children.Add(startupPage);
        adoptionPage = new HostAdoptionPage(
            composition.Services.GetRequiredService<HostAdoptionState>())
        {
            Visibility = Visibility.Collapsed
        };
        adoptionPage.Completed += OnAdoptionCompleted;
        StartupHost.Children.Add(adoptionPage);

        RootGrid.Loaded += OnRootLoaded;
        Closed += OnClosed;
        AppWindow.Closing += (_, args) => { if (GachaView.IsRefreshRunning) args.Cancel = true; };
        ApplyBackdrop();
        ResizeInitialWindow();
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        InitializeWindowSizing();
        if (startupStarted) return;
        startupStarted = true;
#if DEBUG
        if (Environment.GetEnvironmentVariable("FURINA_UI7_SIMULATE_FATAL") == "1")
        {
            throw new InvalidOperationException(
                "Simulated fatal error for UI-7 acceptance.");
        }
#endif
        await RunStartupAsync();
    }

    private async void OnRetryStartup(object? sender, EventArgs e)
    {
        if (adoptionPage is not null) adoptionPage.Visibility = Visibility.Collapsed;
        startupPage.Visibility = Visibility.Visible;
        await RunStartupAsync();
    }

    private async void OnAdoptionCompleted(object? sender, EventArgs e)
    {
        if (adoptionPage is not null) adoptionPage.Visibility = Visibility.Collapsed;
        startupPage.Visibility = Visibility.Visible;
        await RunStartupAsync();
    }

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
            if (state.Snapshot.FailureKind ==
                StartupFailureKind.ProductionAdoptionRequired)
            {
                startupPage.Visibility = Visibility.Collapsed;
                if (adoptionPage is not null)
                {
                    adoptionPage.Visibility = Visibility.Visible;
                    await adoptionPage.EnterAsync();
                }

                return;
            }

            if (!state.Snapshot.IsReady)
            {
                diagnostics.Report(new DiagnosticEvent(
                    NotificationCategory.System,
                    "startup",
                    state.Snapshot.FailureKind == StartupFailureKind.ProductionAdoptionRequired
                        ? "启动需要处理"
                        : "启动失败",
                    $"{state.Snapshot.Status}：{state.Snapshot.Detail}",
                    state.Snapshot.FailureKind == StartupFailureKind.ProductionAdoptionRequired
                        ? DiagnosticSeverity.Warning
                        : DiagnosticSeverity.Error)
                {
                    TaskKey = NotificationTaskKeys.StartupFailure,
                });
                return;
            }

            BuildShell();
            await ApplyPersistedThemeAsync();
            StartupHost.Visibility = Visibility.Collapsed;
            Navigation.Visibility = Visibility.Visible;
            shellReady = true;
            if (!string.IsNullOrWhiteSpace(state.Snapshot.Warning))
            {
                diagnostics.Report(new DiagnosticEvent(
                    NotificationCategory.System,
                    "startup",
                    "数据目录提示",
                    state.Snapshot.Warning!,
                    DiagnosticSeverity.Warning)
                {
                    TaskKey = NotificationTaskKeys.StartupWarning,
                });
            }
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
                diagnostics.Report(new DiagnosticEvent(
                    NotificationCategory.System,
                    "BuildShell",
                    "页面初始化失败",
                    "启动后页面初始化失败；可重试启动。",
                    DiagnosticSeverity.Error,
                    exception)
                {
                    TaskKey = NotificationTaskKeys.StartupFailure,
                });
            }
            catch (Exception)
            {
                try
                {
                    File.WriteAllText(
                        Path.Combine(Path.GetTempPath(), "furina-ui7-buildshell-error.txt"),
                        LogSanitizer.SanitizeException(exception, "BuildShell"));
                }
                catch (Exception)
                {
                }
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

    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        notificationCenterPanel?.SetAvailableWidth(e.NewSize.Width);

    private void BuildShell()
    {
        if (shellBuilt) return;
        GachaView.Initialize(composition.Services.GetRequiredService<GachaPageServices>());
        GachaView.RefreshInteractionChanged += OnGachaRefreshInteractionChanged;

        overviewPage = new OverviewPage(
            composition.Services.GetRequiredService<OverviewState>(),
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        overviewPage.ContinueRequested += OnContinueToGacha;
        overviewPage.BackupRestoreRequested += OnOpenArchiveTransfer;
        PageHost.Children.Add(overviewPage);

        archivePage = new ArchivePage(
            composition.Services.GetRequiredService<ArchiveManagementState>(),
            notificationCenter,
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        archivePage.TransferRequested += OnOpenArchiveTransfer;
        PageHost.Children.Add(archivePage);

        historyPage = new HistoryPage(
            composition.Services.GetRequiredService<ArchiveHistoryState>(),
            notificationCenter,
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(historyPage);

        settingsPage = new SettingsPage(
            composition.Services.GetRequiredService<SettingsState>(),
            ApplyTheme,
            notificationCenter,
            diagnostics,
            composition.Services.GetRequiredService<FurinaChronicle.Presentation.Ai.AiSettingsState>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(settingsPage);

        archiveTransferPage = new ArchiveTransferPage(
            composition.Services.GetRequiredService<ArchiveTransferState>(),
            composition.Services.GetRequiredService<IFilePickerService>(),
            composition.Version,
            composition.Services.GetRequiredService<IArchiveDomainPortableFileService>(),
            composition.Services.GetRequiredService<IArchiveDomainPortableApplier>(),
            notificationCenter,
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        archiveTransferPage.BackRequested += OnTransferBack;
        archiveTransferPage.RestoreApplied += OnRestoreApplied;
        PageHost.Children.Add(archiveTransferPage);

        chroniclePage = new ChroniclePage(
            composition.Services.GetRequiredService<ChronicleWorkspaceState>(),
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(chroniclePage);

        characterWeaponPage = new CharacterWeaponPage(
            composition.Services.GetRequiredService<CharacterWeaponHistoryState>(),
            composition.Services.GetRequiredService<WindowsHistoryCollection>(),
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(characterWeaponPage);

        challengePage = new ChallengePage(
            composition.Services.GetRequiredService<ChallengeHistoryState>(),
            composition.Services.GetRequiredService<WindowsHistoryCollection>(),
            diagnostics,
            composition.Services.GetRequiredService<FurinaChronicle.Services.Abstractions.IChallengeArtworkCache>())
        {
            Visibility = Visibility.Collapsed
        };
        PageHost.Children.Add(challengePage);

        aiPage = new AiPage(composition.Services.GetRequiredService<FurinaChronicle.Presentation.Ai.AiWorkspaceState>(),
            notificationCenter, composition.Services.GetRequiredService<IFilePickerService>(),
            composition.Services.GetRequiredService<FurinaChronicle.Infrastructure.Ai.AiAttachmentReader>(),
            () => Navigation.SelectedItem = SettingsDestination) { Visibility = Visibility.Collapsed };
        PageHost.Children.Add(aiPage);

        passportPage = new PassportPage(
            composition.Services.GetRequiredService<PassportCredentialState>(),
            composition.Services.GetRequiredService<MainlandPassportLoginService>(),
            composition.Services.GetRequiredService<HoYoLabPassportLoginService>(),
            composition.Services.GetRequiredService<IMiHoYoAccountProfileClient>(),
            composition.Services.GetRequiredService<DpapiPassportCredentialStore>(),
            notificationCenter,
            diagnostics)
        {
            Visibility = Visibility.Collapsed
        };
        passportPage.RefreshRequested += OnPassportRefreshRequested;
        PageHost.Children.Add(passportPage);
        shellBuilt = true;
    }

    private void OnGachaRefreshInteractionChanged(object? sender, EventArgs args)
    {
        bool enabled = !GachaView.IsRefreshInteractionLocked;
        foreach (var item in Navigation.MenuItems.Concat(Navigation.FooterMenuItems).OfType<NavigationViewItem>())
            item.IsEnabled = enabled;
        Navigation.IsPaneToggleButtonVisible = enabled;
        if (notificationCenterPanel is not null)
        {
            notificationCenterPanel.IsEnabled = enabled;
            notificationCenterPanel.IsHitTestVisible = enabled;
            notificationCenterPanel.Opacity = enabled ? 1 : 0;
        }
    }

    private void hideCurrentPages()
    {
        GachaView.Leave();
        GachaView.Visibility = Visibility.Collapsed;
        chroniclePage?.Leave();
        characterWeaponPage?.Leave();
        challengePage?.Leave();
        aiPage?.Leave();
        if (overviewPage is not null) overviewPage.Visibility = Visibility.Collapsed;
        if (archivePage is not null) archivePage.Visibility = Visibility.Collapsed;
        if (historyPage is not null) historyPage.Visibility = Visibility.Collapsed;
        if (settingsPage is not null) settingsPage.Visibility = Visibility.Collapsed;
        if (archiveTransferPage is not null) archiveTransferPage.Visibility = Visibility.Collapsed;
        if (chroniclePage is not null) chroniclePage.Visibility = Visibility.Collapsed;
        if (characterWeaponPage is not null) characterWeaponPage.Visibility = Visibility.Collapsed;
        if (challengePage is not null) challengePage.Visibility = Visibility.Collapsed;
        if (aiPage is not null) aiPage.Visibility = Visibility.Collapsed;
        if (passportPage is not null) passportPage.Visibility = Visibility.Collapsed;
    }

    private async void OnPassportRefreshRequested(object? sender, EventArgs e)
    {
        if (!shellReady) return;
        switching = true;
        Navigation.SelectedItem = GachaDestination;
        switching = false;
        await ShowPageAsync("gacha");
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
        if (!shellReady || switching || GachaView.IsRefreshInteractionLocked) return;
        if (args.SelectedItemContainer is not NavigationViewItem item) return;
        await ShowPageAsync(item.Tag?.ToString());
    }

    private async Task ShowPageAsync(string? tag)
    {
        if (tag is null || tag == currentPageTag || !shellBuilt || GachaView.IsRefreshInteractionLocked) return;
        switching = true;
        Navigation.IsEnabled = false;
        try
        {
            hideCurrentPages();
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
                case "ai":
                    currentPageTag = tag;
                    aiPage!.Visibility = Visibility.Visible;
                    await aiPage.EnterAsync();
                    break;
                case "passport":
                    currentPageTag = tag;
                    passportPage!.Visibility = Visibility.Visible;
                    await passportPage.EnterAsync();
                    break;
            }
        }
        finally
        {
            switching = false;
            Navigation.IsEnabled = shellReady && !disposed;
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
        DetachWindowSizing();
        challengePage?.Leave();
        if (aiPage is not null) await aiPage.LeaveAsync();
        await GachaView.CloseAsync();
        await composition.DisposeAsync();
    }

    private void ResizeInitialWindow()
    {
        nint windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(windowHandle);
        AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
        ApplyMinimumWindowSize();
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

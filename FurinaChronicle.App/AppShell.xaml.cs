// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.Pages;
using FurinaChronicle.App.UI.Controls;
using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App;

public partial class AppShell : Shell
{
    private const string StartupRoute = "startup";
    private const string OverviewRoute = "home";
    private const string ChronicleRoute = "chronicle-page";
    private const string GachaPageRoute = "gacha-page";
    private const string ArchiveHistoryPageRoute = "archive-history-page";
    private const string UserPageRoute = "user-page";
    private const string SettingsPageRoute = "settings-page";
    private const string Ui0PreviewRoute = "ui0-preview";
    private const string Ui1PreviewRoute = "ui1-preview";
    private readonly OverviewPage overviewPage;
    private readonly DataHubPage dataHubPage;
    private readonly HomePage homePage;
    private readonly ChroniclePage chroniclePage;
    private readonly GachaPage gachaPage;
    private readonly ArchiveHistoryPage archiveHistoryPage;
    private readonly UserPage userPage;
    private readonly UserPageViewModel userPageViewModel;
    private readonly ArchivePage archivePage;
    private readonly ArchiveTransferPage archiveTransferPage;
    private readonly SettingsPage settingsPage;
#if PHASE8B_VALIDATION
    private readonly Phase8BValidationPage phase8BValidationPage;
#endif
#if !PHASE8B_VALIDATION
    private PassportFlyoutHeader? passportFlyoutHeader;
#endif
    private bool mainNavigationConfigured;

    public AppShell(
        StartupPage startupPage,
        OverviewPage overviewPage,
        DataHubPage dataHubPage,
        HomePage homePage,
        ChroniclePage chroniclePage,
        GachaPage gachaPage,
        ArchiveHistoryPage archiveHistoryPage,
        ArchivePage archivePage,
        ArchiveTransferPage archiveTransferPage,
        SettingsPage settingsPage,
#if PHASE8B_VALIDATION
        Phase8BValidationPage phase8BValidationPage,
#endif
        UserPage userPage,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.overviewPage = overviewPage;
        this.dataHubPage = dataHubPage;
        this.homePage = homePage;
        this.chroniclePage = chroniclePage;
        this.gachaPage = gachaPage;
        this.archiveHistoryPage = archiveHistoryPage;
        this.archivePage = archivePage;
        this.archiveTransferPage = archiveTransferPage;
        this.settingsPage = settingsPage;
#if PHASE8B_VALIDATION
        this.phase8BValidationPage = phase8BValidationPage;
#endif
        this.userPage = userPage;
        this.userPageViewModel = userPageViewModel;
        Routing.RegisterRoute(
            "backup-restore-detail",
            typeof(ArchiveTransferPage));
        Routing.RegisterRoute(ChronicleRoute, typeof(ChroniclePage));
        Routing.RegisterRoute("challenge-history", typeof(ChallengeHistoryPage));
        Routing.RegisterRoute("character-history", typeof(CharacterHistoryPage));
        // Android pushes the shared MAUI pages from the overview / data / archive
        // destinations; the names differ from the Windows flyout routes on purpose.
        Routing.RegisterRoute(GachaPageRoute, typeof(GachaPage));
        Routing.RegisterRoute(
            ArchiveHistoryPageRoute,
            typeof(ArchiveHistoryPage));
        Routing.RegisterRoute(UserPageRoute, typeof(UserPage));
        Routing.RegisterRoute(SettingsPageRoute, typeof(SettingsPage));
        Routing.RegisterRoute(Ui0PreviewRoute, typeof(HomePage));
        Routing.RegisterRoute(Ui1PreviewRoute, typeof(Ui1GachaPage));
        Items.Add(CreateContent("正在启动", StartupRoute, startupPage));
    }

    public async Task ShowMainPageAsync()
    {
        if (!mainNavigationConfigured)
        {
            Items.Clear();
            ConfigureMainNavigation();
            mainNavigationConfigured = true;
        }

        await GoToAsync(HomeRoute, animate: false);
#if !PHASE8B_VALIDATION
        _ = userPageViewModel.InitializeAsync();
#endif
    }

    public Task ShowUserPageAsync()
    {
        return GoToAsync(
            OperatingSystem.IsAndroid() ? UserPageRoute : "//user",
            animate: true);
    }

    public Task ShowGachaPageAsync()
    {
        return GoToAsync(
            OperatingSystem.IsAndroid() ? GachaPageRoute : "//gacha",
            animate: true);
    }

    public Task ShowArchiveHistoryPageAsync()
    {
        return GoToAsync(
            OperatingSystem.IsAndroid()
                ? ArchiveHistoryPageRoute
                : "//archive-history",
            animate: true);
    }

    public Task ShowSettingsPageAsync()
    {
        return GoToAsync(
            OperatingSystem.IsAndroid() ? SettingsPageRoute : "//settings",
            animate: true);
    }

    public Task ShowBackupRestorePageAsync()
    {
        return GoToAsync(
            OperatingSystem.IsAndroid()
                ? "backup-restore-detail"
                : "//backup-restore",
            animate: true);
    }

    public void ToggleWindowsSidebar()
    {
        if (OperatingSystem.IsAndroid())
        {
            return;
        }

        bool collapse = FlyoutBehavior == FlyoutBehavior.Locked;
        FlyoutBehavior = collapse
            ? FlyoutBehavior.Flyout
            : FlyoutBehavior.Locked;
#if !PHASE8B_VALIDATION
        passportFlyoutHeader?.SetSidebarCollapsed(collapse);
#endif
        FlyoutIsPresented = false;
    }

    private string HomeRoute => OperatingSystem.IsAndroid()
#if PHASE8B_VALIDATION
        ? "//main/phase8b7-validation"
        : "//phase8b7-validation";
#else
        ? $"//main/{OverviewRoute}"
        : "//home";
#endif

    private void ConfigureMainNavigation()
    {
#if PHASE8B_VALIDATION
        if (OperatingSystem.IsAndroid())
        {
            FlyoutBehavior = FlyoutBehavior.Disabled;
            var tabs = new TabBar { Route = "main" };
            tabs.Items.Add(CreateContent(
                "8B.7 验收",
                "phase8b7-validation",
                phase8BValidationPage));
            Items.Add(tabs);
            return;
        }

        FlyoutBehavior = FlyoutBehavior.Locked;
        Items.Add(CreateFlyoutItem(
            "8B.7 隔离验收",
            "phase8b7-validation",
            phase8BValidationPage));
        return;
#else
        if (OperatingSystem.IsAndroid())
        {
            // Four destinations (UI-4 §6.3): overview / chronicle / data / archive.
            // History, user/login and settings are reachable from the archive page.
            FlyoutBehavior = FlyoutBehavior.Disabled;
            var tabs = new TabBar { Route = "main" };
            tabs.Items.Add(CreateContent("概览", OverviewRoute, overviewPage));
            tabs.Items.Add(CreateContent("游戏历程", "chronicle", chroniclePage));
            tabs.Items.Add(CreateContent("数据", "data", dataHubPage));
            tabs.Items.Add(CreateContent("档案", "archive", archivePage));
            Items.Add(tabs);
            return;
        }

        FlyoutBehavior = FlyoutBehavior.Locked;
        passportFlyoutHeader = new PassportFlyoutHeader(userPageViewModel);
        FlyoutHeader = passportFlyoutHeader;
        Items.Add(CreateFlyoutItem("主页", "home", homePage));
        Items.Add(CreateFlyoutItem("游戏历程", "chronicle", chroniclePage));
        Items.Add(CreateFlyoutItem("抽卡记录", "gacha", gachaPage));
        Items.Add(CreateFlyoutItem("档案修改历史", "archive-history", archiveHistoryPage));
        Items.Add(CreateFlyoutItem("档案与账号管理", "archive", archivePage));
        Items.Add(CreateFlyoutItem(
            "备份与恢复",
            "backup-restore",
            archiveTransferPage));
        Items.Add(CreateFlyoutItem("用户", "user", userPage));
        Items.Add(CreateFlyoutItem("设置", "settings", settingsPage));
#endif
    }

    private static FlyoutItem CreateFlyoutItem(
        string title,
        string route,
        Page page)
    {
        var item = new FlyoutItem
        {
            Title = title,
            Route = route
        };
        item.Items.Add(CreateContent(title, $"{route}-content", page));
        return item;
    }

    private static ShellContent CreateContent(
        string title,
        string route,
        Page page)
    {
        return new ShellContent
        {
            Title = title,
            Route = route,
            Content = page
        };
    }
}

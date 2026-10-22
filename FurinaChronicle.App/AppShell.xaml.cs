// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.Pages;
using FurinaChronicle.App.UI.Controls;
using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App;

public partial class AppShell : Shell
{
    private const string StartupRoute = "startup";
    private readonly HomePage homePage;
    private readonly GachaPage gachaPage;
    private readonly ArchiveHistoryPage archiveHistoryPage;
    private readonly UserPage userPage;
    private readonly UserPageViewModel userPageViewModel;
    private readonly ArchivePage archivePage;
    private readonly Phase8BValidationPage phase8BValidationPage;
    private PassportFlyoutHeader? passportFlyoutHeader;
    private bool mainNavigationConfigured;

    public AppShell(
        StartupPage startupPage,
        HomePage homePage,
        GachaPage gachaPage,
        ArchiveHistoryPage archiveHistoryPage,
        ArchivePage archivePage,
        Phase8BValidationPage phase8BValidationPage,
        UserPage userPage,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.homePage = homePage;
        this.gachaPage = gachaPage;
        this.archiveHistoryPage = archiveHistoryPage;
        this.archivePage = archivePage;
        this.phase8BValidationPage = phase8BValidationPage;
        this.userPage = userPage;
        this.userPageViewModel = userPageViewModel;
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
        return GoToAsync(UserRoute, animate: true);
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
        passportFlyoutHeader?.SetSidebarCollapsed(collapse);
        FlyoutIsPresented = false;
    }

    private string HomeRoute => OperatingSystem.IsAndroid()
#if PHASE8B_VALIDATION
        ? "//main/phase8b7-validation"
        : "//phase8b7-validation";
#else
        ? "//main/home"
        : "//home";
#endif

    private string UserRoute => OperatingSystem.IsAndroid()
#if PHASE8B_VALIDATION
        ? "//main/phase8b7-validation"
        : "//phase8b7-validation";
#else
        ? "//main/user"
        : "//user";
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
            FlyoutBehavior = FlyoutBehavior.Disabled;
            var tabs = new TabBar { Route = "main" };
            tabs.Items.Add(CreateContent("主页", "home", homePage));
            tabs.Items.Add(CreateContent("抽卡记录", "gacha", gachaPage));
            tabs.Items.Add(CreateContent("修改历史", "archive-history", archiveHistoryPage));
            tabs.Items.Add(CreateContent("档案", "archive", archivePage));
            tabs.Items.Add(CreateContent("用户", "user", userPage));
            Items.Add(tabs);
            return;
        }

        FlyoutBehavior = FlyoutBehavior.Locked;
        passportFlyoutHeader = new PassportFlyoutHeader(userPageViewModel);
        FlyoutHeader = passportFlyoutHeader;
        Items.Add(CreateFlyoutItem("主页", "home", homePage));
        Items.Add(CreateFlyoutItem("抽卡记录", "gacha", gachaPage));
        Items.Add(CreateFlyoutItem("档案修改历史", "archive-history", archiveHistoryPage));
        Items.Add(CreateFlyoutItem("档案与账号管理", "archive", archivePage));
        Items.Add(CreateFlyoutItem("用户", "user", userPage));
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

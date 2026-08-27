using FurinaChronicle.App.UI.Controls;
using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App;

public partial class AppShell : Shell
{
    private const string StartupRoute = "startup";
    private readonly HomePage homePage;
    private readonly MainPage gachaPage;
    private readonly UserPage userPage;
    private readonly UserPageViewModel userPageViewModel;
    private bool mainNavigationConfigured;

    public AppShell(
        StartupPage startupPage,
        HomePage homePage,
        MainPage gachaPage,
        UserPage userPage,
        UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        this.homePage = homePage;
        this.gachaPage = gachaPage;
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
        _ = userPageViewModel.InitializeAsync();
    }

    public Task ShowUserPageAsync()
    {
        return GoToAsync(UserRoute, animate: true);
    }

    private string HomeRoute => OperatingSystem.IsAndroid()
        ? "//main/home"
        : "//home";

    private string UserRoute => OperatingSystem.IsAndroid()
        ? "//main/user"
        : "//user";

    private void ConfigureMainNavigation()
    {
        if (OperatingSystem.IsAndroid())
        {
            FlyoutBehavior = FlyoutBehavior.Disabled;
            var tabs = new TabBar { Route = "main" };
            tabs.Items.Add(CreateContent("主页", "home", homePage));
            tabs.Items.Add(CreateContent("抽卡记录", "gacha", gachaPage));
            tabs.Items.Add(CreateContent("用户", "user", userPage));
            Items.Add(tabs);
            return;
        }

        FlyoutBehavior = FlyoutBehavior.Locked;
        FlyoutHeader = new PassportFlyoutHeader(userPageViewModel);
        Items.Add(CreateFlyoutItem("主页", "home", homePage));
        Items.Add(CreateFlyoutItem("抽卡记录", "gacha", gachaPage));
        Items.Add(CreateFlyoutItem("用户", "user", userPage));
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

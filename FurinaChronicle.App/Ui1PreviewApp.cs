// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.UI.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace FurinaChronicle.App;

public sealed partial class Ui1PreviewApp : Application
{
    private readonly IServiceProvider services;

    public Ui1PreviewApp(IServiceProvider services)
    {
        this.services = services;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new Ui2PreviewShell(services.GetRequiredService<Ui1GachaPage>()));
}

internal sealed class Ui2PreviewShell : Shell
{
    public Ui2PreviewShell(Ui1GachaPage page)
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        var tabBar = new TabBar();
        var open = new Button { Text = "打开抽卡分析", MinimumHeightRequest = 48 };
        var theme = new Picker { Title = "外观", ItemsSource = new[] { "跟随系统", "浅色", "深色" }, SelectedIndex = 0 };
        theme.SelectedIndexChanged += (_, _) =>
        {
            if (Application.Current is not Application app) return;
            app.UserAppTheme = theme.SelectedIndex switch { 1 => AppTheme.Light, 2 => AppTheme.Dark, _ => AppTheme.Unspecified };
        };
        var overview = new ContentPage
        {
            Title = "概览", SafeAreaEdges = SafeAreaEdges.All,
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    Padding = 20, Spacing = 12,
                    Children =
                    {
                        new Label { Text = "FurinaChronicle", FontSize = 26, FontAttributes = FontAttributes.Bold },
                        new Label { Text = "抽卡分析使用本机隔离合成数据，可离线查看。" }, open, theme
                    }
                }
            }
        };
        var home = new Tab { Title = "概览", Items = { new ShellContent { Title = "概览", Content = overview } } };
        var data = new Tab { Title = "数据", Items = { new ShellContent { Title = "抽卡分析", Content = page } } };
        tabBar.Items.Add(home);
        tabBar.Items.Add(data);
        Items.Add(tabBar);
        tabBar.CurrentItem = data;
        open.Clicked += (_, _) => tabBar.CurrentItem = data;
        var homeButton = new ToolbarItem { Text = "打开概览", Order = ToolbarItemOrder.Secondary };
        homeButton.Clicked += (_, _) => tabBar.CurrentItem = home;
        page.ToolbarItems.Add(homeButton);
        page.SizeChanged += (_, _) => SetTabBarIsVisible(page,
            DeviceDisplay.Current.MainDisplayInfo.Orientation != DisplayOrientation.Landscape);
        foreach (Page target in new Page[] { overview, page })
        {
            target.SetAppThemeColor(VisualElement.BackgroundColorProperty, Color.FromArgb("#FAFAFA"), Color.FromArgb("#181818"));
            target.SetAppThemeColor(Shell.BackgroundColorProperty, Color.FromArgb("#FAFAFA"), Color.FromArgb("#181818"));
            target.SetAppThemeColor(TitleColorProperty, Colors.Black, Colors.White);
            target.SetAppThemeColor(ForegroundColorProperty, Colors.Black, Colors.White);
        }
    }
}

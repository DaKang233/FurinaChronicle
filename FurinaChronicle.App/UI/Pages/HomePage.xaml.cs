// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.ViewModels;
using FurinaChronicle.App.UI.TechnicalPreview;

namespace FurinaChronicle.App.UI.Pages;

public partial class HomePage : ContentPage
{
    private readonly IMaterialBottomSheetPreview bottomSheetPreview;
    private MaterialExtendedActionButton? actionButton = null;

    public HomePage(
        UserPageViewModel userPageViewModel,
        IMaterialBottomSheetPreview bottomSheetPreview)
    {
        InitializeComponent();
        this.bottomSheetPreview = bottomSheetPreview;
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }

#if ANDROID
        actionButton = new MaterialExtendedActionButton
        {
            Text = "＋  数据操作",
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.End,
            HeightRequest = 56,
            Margin = new Thickness(0, 0, 8, 8),
            ZIndex = 10
        };
        SemanticProperties.SetDescription(
            actionButton,
            "打开 UI-0 数据操作底部面板");
        actionButton.Clicked += OnOpenMaterialActionsClicked;
        RootLayout.Children.Add(actionButton);
#endif

        SizeChanged += OnPageSizeChanged;
        Loaded += OnPageLoaded;
        Unloaded += OnPageUnloaded;
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        UpdateResponsiveState();
        UpdateThemeState();
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged += OnRequestedThemeChanged;
        }
    }

    private void OnPageUnloaded(object? sender, EventArgs e)
    {
        if (Application.Current is not null)
        {
            Application.Current.RequestedThemeChanged -= OnRequestedThemeChanged;
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        UpdateResponsiveState();
    }

    private void UpdateResponsiveState()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        string widthClass = Width switch
        {
            < 600 => "Compact",
            < 840 => "Medium",
            _ => "Expanded"
        };
        string heightState = Height < 480 ? "低高度" : "常规高度";
        LayoutStateLabel.Text =
            $"可用窗口 {Width:0} × {Height:0} dp · {widthClass} · {heightState}";

        if (actionButton is not null)
        {
            actionButton.Text = Width < 360 ? "＋" : "＋  数据操作";
            SemanticProperties.SetDescription(
                actionButton,
                "打开 UI-0 数据操作底部面板");
        }
    }

    private void OnRequestedThemeChanged(
        object? sender,
        AppThemeChangedEventArgs e)
    {
        UpdateThemeState();
    }

    private void UpdateThemeState()
    {
        ThemeStateLabel.Text =
            $"系统请求主题：{Application.Current?.RequestedTheme ?? AppTheme.Unspecified}；面板每次打开都从当前 Activity 主题创建。";
    }

    private async void OnOpenMaterialActionsClicked(
        object? sender,
        EventArgs e)
    {
        if (actionButton is null || !actionButton.IsEnabled)
        {
            return;
        }

        actionButton.IsEnabled = false;
        SheetStateLabel.Text = "正在显示原生 Material BottomSheet…";
        try
        {
            await bottomSheetPreview.ShowAsync();
            SheetStateLabel.Text =
                "面板已关闭；入口按钮重新获得焦点。请分别用返回键、手势、遮罩和关闭按钮复测。";
        }
        catch (Exception exception)
        {
            SheetStateLabel.Text =
                $"面板打开失败：{exception.GetType().Name}。";
        }
        finally
        {
            actionButton.IsEnabled = true;
            actionButton.Focus();
        }
    }
}

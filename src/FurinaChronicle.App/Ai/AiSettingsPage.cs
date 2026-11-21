// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Ai;

namespace FurinaChronicle.App.Ai;

public sealed class AiSettingsPage : ContentPage
{
    private readonly AiSettingsView editor;
    private bool loaded;
    public AiSettingsPage(AiSettingsState state)
    {
        Title = "AI 设置";
        AutomationId = "AiSettingsPage";
        Shell.SetNavBarIsVisible(this, false);
        editor = new AiSettingsView(state);
        var back = new Button { Text = "返回设置", AutomationId = "AiSettingsBack" };
        back.Clicked += async (_, _) => await Navigation.PopAsync();
        var header = new HorizontalStackLayout { Spacing = 12 };
        header.Add(back);
        header.Add(new Label { Text = "AI 设置", FontSize = 22, VerticalOptions = LayoutOptions.Center });
        var root = new Grid { Padding = 16, RowSpacing = 12, RowDefinitions = { new(GridLength.Auto), new(GridLength.Star) } };
        root.Add(header);
        var scroll = new ScrollView { Content = editor, VerticalScrollBarVisibility = ScrollBarVisibility.Always };
        Grid.SetRow(scroll, 1); root.Add(scroll); Content = root;
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Native pickers / app resume must not replace an in-progress settings draft.
        if (loaded) return;
        loaded = true;
        await editor.LoadAsync();
    }
}

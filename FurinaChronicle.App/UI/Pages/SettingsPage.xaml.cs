// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Diagnostics;
using FurinaChronicle.App.UI.Gacha;

namespace FurinaChronicle.App.UI.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly GachaAppearanceSettings appearance;

    public SettingsPage(
        GachaAppearanceSettings appearance,
        ApplicationVersionInfo versionInfo)
    {
        this.appearance = appearance;
        InitializeComponent();
        VersionLabel.Text = versionInfo.UserVisibleName;
        VersionDiagnosticLabel.Text =
            $"Phase {versionInfo.PhaseLabel} · {versionInfo.Commit}" +
            (versionInfo.IsReliable ? string.Empty : " · 非发布构建");
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ItemBackgroundPicker.SelectedIndex = appearance.UseClassicBackground ? 1 : 0;
    }

    private void OnItemBackgroundChanged(object? sender, EventArgs e)
    {
        if (sender is not Picker { SelectedIndex: 0 or 1 } picker || Application.Current is not Application app)
            return;

        bool useClassic = picker.SelectedIndex == 1;
        if (appearance.UseClassicBackground != useClassic)
            appearance.SetClassicBackground(useClassic, app.Resources);
    }
}

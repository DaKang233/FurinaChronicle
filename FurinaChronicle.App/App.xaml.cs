// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.App.Composition;
using FurinaChronicle.App.UI.Gacha;

namespace FurinaChronicle.App
{
    public partial class App : Application
    {
        private readonly AppShell appShell;

        public App(
            AppShell appShell,
            GachaAppearanceSettings appearance,
            MauiSettingsAppearanceStore settingsAppearance)
        {
            InitializeComponent();
            appearance.ApplyTo(Resources);
            // Persisted settings-page theme is applied before the first window shows.
            settingsAppearance.ApplyPersistedTheme();
            this.appShell = appShell;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(appShell);
            if (OperatingSystem.IsWindows())
            {
                window.MinimumWidth = 1000;
                window.MinimumHeight = 600;
            }
            return window;
        }
    }
}

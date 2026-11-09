// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.DependencyInjection;

namespace FurinaChronicle.App
{
    public partial class App : Application
    {
        private readonly AppShell appShell;

        public App(AppShell appShell)
        {
            InitializeComponent();
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

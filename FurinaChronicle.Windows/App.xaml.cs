// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;

namespace FurinaChronicle.Windows;

public partial class App : Application
{
    private Window? window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        WindowsComposition composition = WindowsComposition.Create();
        window = new MainWindow(composition);
        window.Activate();
    }
}

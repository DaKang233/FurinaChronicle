// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Windows.Composition;
using Microsoft.UI.Xaml;

namespace FurinaChronicle.Windows;

public partial class App : Application
{
    private Window? window;
    private SingleInstanceGuard? singleInstance;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Remove legacy unsanitized build diagnostics from earlier UI-4 runs.
        try
        {
            string legacy = Path.Combine(
                Path.GetTempPath(),
                "furina-ui4-buildshell-error.txt");
            if (File.Exists(legacy)) File.Delete(legacy);
        }
        catch (IOException)
        {
        }

        // Same-data-directory instances are refused before any environment/database is opened.
        var paths = new WindowsPaths();
        singleInstance = SingleInstanceGuard.TryAcquire(paths.BaseDirectory);
        if (singleInstance is null)
        {
            Environment.Exit(0);
            return;
        }

        WindowsComposition composition = WindowsComposition.Create();
        window = new MainWindow(composition);
        window.Activate();
    }
}

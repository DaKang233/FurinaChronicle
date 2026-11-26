// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT

using FurinaChronicle.Presentation.Diagnostics;
using FurinaChronicle.Presentation.Management;
using FurinaChronicle.Windows.Composition;
using FurinaChronicle.Windows.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace FurinaChronicle.Windows;

public partial class App : Application
{
    private Window? window;
    private Window? fatalWindow;
    private SingleInstanceGuard? singleInstance;
    private WindowsComposition? composition;
    private bool fatalShown;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
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
        singleInstance = SingleInstanceGuard.TryAcquire(paths.DataRoot);
        if (singleInstance is null)
        {
            Environment.Exit(0);
            return;
        }

        composition = WindowsComposition.Create();
        window = new MainWindow(composition);
        window.Activate();
    }

    private void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowFatal(e.Exception);
    }

    private void ShowFatal(Exception exception)
    {
        if (fatalShown)
        {
            Environment.Exit(1);
            return;
        }
        fatalShown = true;

        DiagnosticReport report;
        IDiagnosticReportExporter? exporter = null;
        try
        {
            IDiagnosticService? diagnostics =
                composition?.Services.GetService<IDiagnosticService>();
            exporter = composition?.Services.GetService<IDiagnosticReportExporter>();
            report = diagnostics?.Report(new DiagnosticEvent(
                Presentation.Notifications.NotificationCategory.System,
                "unhandled-exception",
                "应用发生致命错误",
                "应用无法继续运行；本地数据未被修改。",
                DiagnosticSeverity.Fatal,
                exception)) ?? FallbackReport(exception);
        }
        catch (Exception)
        {
            report = FallbackReport(exception);
            exporter = null;
        }

        try
        {
            fatalWindow = new Window { Title = "FurinaChronicle · 致命错误" };
            fatalWindow.AppWindow.SetIcon(Path.Combine(
                AppContext.BaseDirectory, "Assets", "AppIcon", "FurinaChronicle.ico"));
            if (composition?.Services.GetService<IFilePickerService>() is { } picker)
            {
                picker.SetOwner(fatalWindow);
            }
            fatalWindow.Content = new FatalErrorPage(report, exporter);
            fatalWindow.Activate();
            window?.Close();
        }
        catch (Exception)
        {
            // Last-resort local hint: keep the sanitized diagnostic text and exit safely.
            try
            {
                File.WriteAllText(
                    Path.Combine(
                        Path.GetTempPath(),
                        $"furina-fatal-{report.DiagnosticId}.txt"),
                    $"{report.DiagnosticId}{Environment.NewLine}" +
                    $"{report.Message}{Environment.NewLine}{report.StackTrace}");
            }
            catch (Exception)
            {
            }
            Environment.Exit(1);
        }
    }

    private static DiagnosticReport FallbackReport(Exception exception)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id = $"FCD-FATAL-{now:yyyyMMdd-HHmmss}";
        var report = new DiagnosticReport(
            id,
            now,
            DiagnosticSeverity.Fatal,
            Presentation.Notifications.NotificationCategory.System,
            "unhandled-exception",
            "应用发生致命错误",
            "应用无法继续运行；本地数据未被修改。",
            exception.GetType().FullName,
            LogSanitizer.SanitizeException(exception, "unhandled-exception"),
            LogWriteFailed: true);
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FurinaChronicle",
                "logs");
            new FileDiagnosticLog(directory).Write(report);
        }
        catch (Exception)
        {
        }
        return report;
    }
}

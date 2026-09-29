using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace FurinaChronicle.App.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            UnhandledException += OnUnhandledException;
            this.InitializeComponent();
        }

        private static void OnUnhandledException(
            object sender,
            Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                string logDirectory = Path.Combine(
                    Environment.GetFolderPath(
                        Environment.SpecialFolder.LocalApplicationData),
                    "FurinaChronicle",
                    "Logs");
                Directory.CreateDirectory(logDirectory);
                File.AppendAllText(
                    Path.Combine(logDirectory, "windows-unhandled.log"),
                    $"[{DateTimeOffset.Now:O}]{Environment.NewLine}" +
                    $"{e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never replace the original UI exception.
            }
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}

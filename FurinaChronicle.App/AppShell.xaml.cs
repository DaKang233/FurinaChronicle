namespace FurinaChronicle.App
{
    public partial class AppShell : Shell
    {
        private const string StartupRoute = "Startup";
        private const string MainRoute = "Main";

        public AppShell(
            StartupPage startupPage,
            MainPage mainPage)
        {
            InitializeComponent();

            Items.Add(
                new ShellContent
                {
                    Title = "正在启动",
                    Route = StartupRoute,
                    Content = startupPage
                });

            Items.Add(
                new ShellContent
                {
                    Title = "FurinaChronicle",
                    Route = MainRoute,
                    Content = mainPage
                });
        }

        public Task ShowMainPageAsync()
        {
            return GoToAsync(
                $"//{MainRoute}",
                animate: false);
        }
    }
}

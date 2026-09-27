using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App;

public partial class StartupPage : ContentPage
{
    private readonly StartupPageViewModel viewModel;
    private bool navigationCompleted;

    public StartupPage(StartupPageViewModel viewModel)
    {
        InitializeComponent();

        this.viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (navigationCompleted)
        {
            return;
        }

        bool succeeded = await viewModel.InitializeAsync();
        if (!succeeded)
        {
            return;
        }

        navigationCompleted = true;

        // Let the current WinUI layout pass finish before changing Shell route.
        await Task.Yield();
        if (Shell.Current is AppShell shell)
        {
            await shell.ShowMainPageAsync();
            _ = viewModel.MaintainPassportAccountsAsync();
        }
    }
}

using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Controls;

public partial class PassportFlyoutHeader : ContentView
{
    public PassportFlyoutHeader(UserPageViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Shell.Current is AppShell shell)
        {
            await shell.ShowUserPageAsync();
        }
    }
}

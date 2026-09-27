using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Controls;

public partial class PassportAvatarButton : ContentView
{
    public PassportAvatarButton()
    {
        InitializeComponent();
    }

    public PassportAvatarButton(UserPageViewModel viewModel)
        : this()
    {
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

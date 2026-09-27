using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App;

public partial class HomePage : ContentPage
{
    public HomePage(UserPageViewModel userPageViewModel)
    {
        InitializeComponent();
        if (Shell.GetTitleView(this) is UI.Controls.PassportAvatarButton avatar)
        {
            avatar.BindingContext = userPageViewModel;
        }
    }
}

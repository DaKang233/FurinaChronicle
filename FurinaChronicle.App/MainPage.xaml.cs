using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App
{
    public partial class MainPage : ContentPage
    {
        public MainPage(MainPageViewModel viewModel)
        {
            InitializeComponent();
            BindingContext = viewModel;
        }
    }
}

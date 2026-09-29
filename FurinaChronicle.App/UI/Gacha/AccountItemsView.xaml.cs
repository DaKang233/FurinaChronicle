using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Gacha;

public partial class AccountItemsView : ContentView
{
    public AccountItemsView()
    {
        InitializeComponent();
    }

    private void OnToggleTimesClicked(object? sender, EventArgs e)
    {
        if (sender is Button
            {
                CommandParameter: WishItemStatisticsDisplayItem item
            })
        {
            item.IsExpanded = !item.IsExpanded;
        }
    }
}

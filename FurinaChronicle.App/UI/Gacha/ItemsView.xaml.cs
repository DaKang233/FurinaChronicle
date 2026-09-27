using FurinaChronicle.App.ViewModels;

namespace FurinaChronicle.App.UI.Gacha;

public partial class ItemsView : ContentView
{
    public ItemsView()
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

using FurinaChronicle.App.ViewModels;
using System.ComponentModel;

namespace FurinaChronicle.App.UI.Gacha;

public partial class ArchiveGachaView : ContentView
{
    private GachaAnalysisViewModel? viewModel;
    private GachaAnalysisSection? loadedSection;

    public ArchiveGachaView()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnAnalysisPropertyChanged;
        }
        ReleaseAnalysisPage();

        base.OnBindingContextChanged();
        viewModel = BindingContext as GachaAnalysisViewModel;
        if (viewModel is null)
        {
            return;
        }

        viewModel.PropertyChanged += OnAnalysisPropertyChanged;
        LoadAnalysisPage(viewModel.CurrentSection);
    }

    private void OnAnalysisPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GachaAnalysisViewModel.CurrentSection) &&
            viewModel is not null)
        {
            LoadAnalysisPage(viewModel.CurrentSection);
        }
    }

    private void LoadAnalysisPage(GachaAnalysisSection section)
    {
        if (viewModel is null ||
            loadedSection == section && AnalysisPageHost.Content is not null)
        {
            return;
        }

        ReleaseAnalysisPage();
        viewModel.PrepareForSection(section);
        ContentView page = section switch
        {
            GachaAnalysisSection.Overview => new ArchiveOverviewView(),
            GachaAnalysisSection.Details => new DetailView(),
            GachaAnalysisSection.History => new HistoryView(),
            GachaAnalysisSection.Calendar => new CalendarView(),
            GachaAnalysisSection.Items => new ArchiveItemsView(),
            _ => throw new ArgumentOutOfRangeException(nameof(section))
        };
        page.BindingContext = viewModel;
        AnalysisPageHost.Content = page;
        loadedSection = section;
    }

    private void ReleaseAnalysisPage()
    {
        if (AnalysisPageHost.Content is ContentView oldPage)
        {
            oldPage.BindingContext = null;
            AnalysisPageHost.Content = null;
        }
        loadedSection = null;
    }
}

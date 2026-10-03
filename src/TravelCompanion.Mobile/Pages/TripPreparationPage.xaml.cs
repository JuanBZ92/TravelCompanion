using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TripPreparationPage : TripScopedPage
{
    public TripPreparationPage(TripPreparationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        Shell.SetNavBarIsVisible(this, true);
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await FolderPanel.ActivateAsync();
    }
    protected override void OnDisappearing()
    {
        FolderPanel.Deactivate();
        base.OnDisappearing();
    }
}

using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class JournalPage : ContentPage
{
    private readonly JournalViewModel _viewModel;
    public JournalPage()
    {
        InitializeComponent();
        BindingContext = _viewModel = MauiProgram.Services.GetRequiredService<JournalViewModel>();
    }
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
    }
    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }
}

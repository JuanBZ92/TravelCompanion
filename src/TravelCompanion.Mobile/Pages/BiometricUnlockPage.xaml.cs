using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class BiometricUnlockPage : ContentPage
{
    private readonly BiometricUnlockViewModel _viewModel;

    public BiometricUnlockPage()
        : this(MauiProgram.Services.GetRequiredService<BiometricUnlockViewModel>())
    {
    }

    public BiometricUnlockPage(BiometricUnlockViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.TryAutoUnlockAsync();
    }
}

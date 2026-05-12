using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class PackagesPage : ContentPage
{
    private readonly PackagesViewModel _viewModel;
    private bool _loaded;

    public PackagesPage()
        : this(MauiProgram.Services.GetRequiredService<PackagesViewModel>())
    {
    }

    public PackagesPage(PackagesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await _viewModel.LoadPackagesCommand.ExecuteAsync(null);
    }
}

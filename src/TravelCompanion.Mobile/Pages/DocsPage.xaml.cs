namespace TravelCompanion.Mobile.Pages;

public partial class DocsPage : ContentPage
{
    private readonly ViewModels.DocsViewModel _viewModel;

    public DocsPage()
        : this(MauiProgram.Services.GetRequiredService<ViewModels.DocsViewModel>())
    {
    }

    public DocsPage(ViewModels.DocsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.IsBusy)
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }
}

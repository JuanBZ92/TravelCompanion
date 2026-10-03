namespace TravelCompanion.Mobile.Pages;

public partial class DocsPage : ContentPage, IQueryAttributable
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
        _viewModel.SetCategory(null);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _viewModel.SetCategory(query.TryGetValue("DocumentCategory", out var value)
            && value is Services.LocalDocumentCategory category ? category : null);
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

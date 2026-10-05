using System.ComponentModel;

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
        _viewModel.PropertyChanged -= OnDocumentStateChanged;
        _viewModel.PropertyChanged += OnDocumentStateChanged;
        if (_viewModel.HasError) EditorialUi.RevealError(DocumentError);
        if (!_viewModel.IsBusy)
        {
            await _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnDocumentStateChanged;
        _viewModel.CancelLoading();
        base.OnDisappearing();
    }

    private void OnDocumentStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.HasError) && _viewModel.HasError)
            EditorialUi.RevealError(DocumentError);
    }
}

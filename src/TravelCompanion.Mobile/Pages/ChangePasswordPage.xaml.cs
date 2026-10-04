using TravelCompanion.Mobile.ViewModels;
using System.ComponentModel;

namespace TravelCompanion.Mobile.Pages;

public partial class ChangePasswordPage : ContentPage
{
    private readonly ChangePasswordViewModel _viewModel;
    public ChangePasswordPage()
        : this(MauiProgram.Services.GetRequiredService<ChangePasswordViewModel>())
    {
    }

    public ChangePasswordPage(ChangePasswordViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged -= OnValidationChanged;
        _viewModel.PropertyChanged += OnValidationChanged;
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnValidationChanged;
        base.OnDisappearing();
    }

    private void OnValidationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.HasError) && _viewModel.HasError)
            EditorialUi.RevealError(FormScroll, ValidationMessage);
    }
}

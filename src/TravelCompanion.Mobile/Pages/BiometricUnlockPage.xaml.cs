using System;
using System.ComponentModel;
using TravelCompanion.Mobile.Services;
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
        _viewModel.PropertyChanged -= OnUnlockStateChanged;
        _viewModel.PropertyChanged += OnUnlockStateChanged;

        try
        {
            await _viewModel.TryAutoUnlockAsync();
        }
        catch (Exception ex)
        {
            ClientDiagnostics.Record("biometric_auto_unlock_failed", exception: ex);
            _viewModel.ErrorMessage = LocalizationResourceManager.Instance["BiometricUnlockError"];
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.PropertyChanged -= OnUnlockStateChanged;
        base.OnDisappearing();
    }

    private void OnUnlockStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(_viewModel.HasError) && _viewModel.HasError)
            EditorialUi.RevealError(UnlockScroll, UnlockError);
        else if (e.PropertyName == nameof(_viewModel.UnlockStatusMessage))
            Dispatcher.Dispatch(() => SemanticScreenReader.Default.Announce(_viewModel.UnlockStatusMessage));
    }
}

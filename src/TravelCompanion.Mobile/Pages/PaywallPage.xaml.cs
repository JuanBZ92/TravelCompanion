using System.ComponentModel;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

[QueryProperty(nameof(EntryPoint), "EntryPoint")]
public partial class PaywallPage : ContentPage
{
    private readonly PaywallViewModel _viewModel;
    private bool _isVisible;
    public string? EntryPoint { set => _viewModel.SetEntryPoint(value); }
    public PaywallPage()
        : this(MauiProgram.Services.GetRequiredService<PaywallViewModel>())
    {
    }

    public PaywallPage(PaywallViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        PassBack.IsVisible = Navigation.NavigationStack.Count > 1;
        _isVisible = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        await _viewModel.LoadOfferCommand.ExecuteAsync(null);
        if (_isVisible)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnDisappearing()
    {
        _isVisible = false;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        base.OnDisappearing();
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        if (!_isVisible || Navigation.NavigationStack.Count <= 1) return;
        try { await Shell.Current.GoToAsync(".."); }
        catch (Exception ex) { ClientDiagnostics.Record("paywall_back_failed", exception: ex); }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaywallViewModel.IsBusy) && !_viewModel.IsBusy)
            Dispatcher.Dispatch(() => _ = RevealFeedbackAsync());
    }

    private async Task RevealFeedbackAsync()
    {
        if (!_isVisible || _viewModel.IsBusy) return;
        var message = _viewModel.HasError ? _viewModel.ErrorMessage : _viewModel.StateText;
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            // Purchase and restore results must remain visible even when initiated below the fold.
            await PassScroll.ScrollToAsync(_viewModel.HasError ? PassError : PassState,
                ScrollToPosition.MakeVisible, animated: false);
            if (_isVisible && !_viewModel.IsBusy)
                SemanticScreenReader.Default.Announce(message);
        }
        catch (Exception ex)
        {
            ClientDiagnostics.Record("paywall_feedback_failed", exception: ex);
        }
    }
}

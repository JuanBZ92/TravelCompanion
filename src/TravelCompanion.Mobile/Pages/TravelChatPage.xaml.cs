using TravelCompanion.Mobile.ViewModels;

namespace TravelCompanion.Mobile.Pages;

public partial class TravelChatPage : ContentPage, IQueryAttributable
{
    private readonly TravelChatViewModel _viewModel;
    private DateOnly? _reviewDate;
    private string? _reviewCity;
    private string? _reviewSummary;

    public TravelChatPage()
        : this(MauiProgram.Services.GetRequiredService<TravelChatViewModel>())
    {
    }

    public TravelChatPage(TravelChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _reviewDate = query.TryGetValue("ReviewDate", out var dateValue) && dateValue is DateOnly date
            ? date
            : null;
        _reviewCity = query.TryGetValue("ReviewCity", out var cityValue) ? cityValue as string : null;
        _reviewSummary = query.TryGetValue("ReviewSummary", out var summaryValue) ? summaryValue as string : null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.LoadContextAsync();
            if (_reviewDate is { } reviewDate)
            {
                var city = _reviewCity;
                var summary = _reviewSummary;
                _reviewDate = null;
                _reviewCity = null;
                _reviewSummary = null;
                await _viewModel.RequestDayAlternativeAsync(reviewDate, city, summary);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user leaves the page while its context is loading.
        }
        catch
        {
            _viewModel.ErrorMessage = "No pudimos preparar la mejora del día. Intenta nuevamente.";
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelActiveOperations();
        base.OnDisappearing();
    }

    private static bool ReduceMotion
    {
        get
        {
#if ANDROID
            return OperatingSystem.IsAndroidVersionAtLeast(26) && !Android.Animation.ValueAnimator.AreAnimatorsEnabled();
#elif IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif WINDOWS
            return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return true;
#endif
        }
    }

    private async void OnCardLoaded(object? sender, EventArgs e)
    {
        if (sender is not View view || view.BindingContext is not TravelChatCardViewModel { AnimateEntrance: true } card) return;
        card.AnimateEntrance = false;
        if (ReduceMotion) return;
        try
        {
            view.CancelAnimations();
            view.Opacity = 0;
            view.TranslationY = 12;
            await Task.WhenAll(view.FadeToAsync(1, 220, Easing.CubicOut), view.TranslateToAsync(0, 0, 220, Easing.CubicOut));
        }
        catch (OperationCanceledException) { }
        finally { view.Opacity = 1; view.TranslationY = 0; }
    }

    private void OnCardUnloaded(object? sender, EventArgs e)
    {
        if (sender is not View view) return;
        view.CancelAnimations();
        view.Opacity = 1;
        view.TranslationY = 0;
    }

    private async void OnSuggestedReplyTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is string reply)
        {
            await _viewModel.SendSuggestedReplyCommand.ExecuteAsync(reply);
        }
    }

    private async void OnSaveItineraryItemClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.SaveItineraryItemCommand.ExecuteAsync(card);
        }
    }

    private async void OnReplaceRecommendationClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
        {
            await _viewModel.ReplaceRecommendationCommand.ExecuteAsync(card);
        }
    }

    private async void OnFindCloserDayStopClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
            await _viewModel.FindCloserDayStopCommand.ExecuteAsync(card);
    }

    private async void OnTagActionTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatTagActionViewModel tagAction)
        {
            await _viewModel.AvoidTagCommand.ExecuteAsync(tagAction.Tag);
        }
    }

    private async void OnGuidedOptionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatGuidedOptionViewModel option)
        {
            await _viewModel.SelectGuidedOptionCommand.ExecuteAsync(option);
        }
    }

    private void OnAdjustGuidedPlanClicked(object? sender, EventArgs e)
    {
        _viewModel.AdjustGuidedPlanCommand.Execute(null);
    }
}

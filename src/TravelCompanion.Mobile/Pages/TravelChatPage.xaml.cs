using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Mobile.Services;

namespace TravelCompanion.Mobile.Pages;

public partial class TravelChatPage : ContentPage, IQueryAttributable
{
    private readonly TravelChatViewModel _viewModel;
    private DateOnly? _reviewDate;
    private string? _reviewCity;
    private string? _reviewSummary;
    private string? _adaptationReason;
    private int? _delayMinutes;
    private TravelCompanion.Shared.Dtos.GuidedPlanCriteriaDto? _personalizedCriteria;

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
        _adaptationReason = query.TryGetValue("AdaptationReason", out var reasonValue) ? reasonValue as string : null;
        _delayMinutes = query.TryGetValue("DelayMinutes", out var delayValue) && delayValue is int delay && delay > 0 ? delay : null;
        _personalizedCriteria = query.TryGetValue("PersonalizedCriteria", out var criteriaValue)
            ? criteriaValue as TravelCompanion.Shared.Dtos.GuidedPlanCriteriaDto : null;
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
                var reason = _adaptationReason;
                var delay = _delayMinutes;
                var personalizedCriteria = _personalizedCriteria;
                _adaptationReason = null;
                _delayMinutes = null;
                _personalizedCriteria = null;
                if (personalizedCriteria is not null)
                    await _viewModel.RequestPersonalizedDayAsync(reviewDate, city, personalizedCriteria);
                else if (reason is null) await _viewModel.RequestDayAlternativeAsync(reviewDate, city, summary);
                else await _viewModel.RequestDayAdaptationAsync(reviewDate, city, reason, delay);
            }
            else
            {
                await _viewModel.RefreshAssistantProposalAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the user leaves the page while its context is loading.
        }
        catch
        {
            _viewModel.ErrorMessage = LocalizationResourceManager.Instance["UXAuditAssistantLoadError"];
        }
    }

    protected override void OnDisappearing()
    {
        _viewModel.CancelActiveOperations();
        _viewModel.OpenAssistantCard(null);
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() =>
        _viewModel.TryAssistantBack() || base.OnBackButtonPressed();

    private void OnQuickCriterionClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatGuidedOptionViewModel option)
            _viewModel.SelectQuickCriterionCommand.Execute(option);
    }

    private async void OnProposalRowClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is AssistantProposalRow row)
            await _viewModel.OpenProposalRowAsync(row);
    }

    private async void OnSaveDetailClicked(object? sender, EventArgs e) =>
        await _viewModel.SaveAssistantCardCommand.ExecuteAsync(null);

    private async void OnSaveProposalClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is AssistantProposalRow row)
            await _viewModel.SaveProposalCardAsync(row.Suggestion);
    }

    private async void OnReplaceProposalClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is AssistantProposalRow row)
            await _viewModel.ReplaceProposalCardAsync(row.Suggestion);
    }

    private async void OnReplaceDetailClicked(object? sender, EventArgs e) =>
        await _viewModel.ReplaceAssistantCardCommand.ExecuteAsync(null);

    private void OnAdjustDetailClicked(object? sender, EventArgs e) =>
        _viewModel.AdjustAssistantCardCommand.Execute(null);

    private void OnCloseDetailClicked(object? sender, EventArgs e) =>
        _viewModel.CloseAssistantCardCommand.Execute(null);

    private async void OnAssistantDateSelected(object? sender, DateChangedEventArgs e)
    {
        if (e.NewDate is { } date) await _viewModel.UpdateCityForDateAsync(date);
    }

    private void OnOpenCardClicked(object? sender, EventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is TravelChatCardViewModel card)
            _viewModel.OpenAssistantCard(card);
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

    private async void OnSuggestedReplyClicked(object? sender, EventArgs e)
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

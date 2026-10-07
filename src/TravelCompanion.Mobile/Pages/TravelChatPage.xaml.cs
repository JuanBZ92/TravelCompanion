using TravelCompanion.Mobile.ViewModels;
using TravelCompanion.Mobile.Services;
using System.ComponentModel;

namespace TravelCompanion.Mobile.Pages;

public partial class TravelChatPage : ContentPage, IQueryAttributable
{
    private readonly TravelChatViewModel _viewModel;
    private DateOnly? _freeTimeDate;
    private bool _visible;
    private int _appearanceVersion;
    private int _guidedRevealVersion;
    private bool _guidedRevealPending;
    private long _guidedRevealContextVersion;
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
        _freeTimeDate = query.TryGetValue("FreeTimeDate", out var freeTimeDate) && freeTimeDate is DateOnly freeDate ? freeDate : null;
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
        _visible = true;
        _viewModel.PropertyChanged -= OnAssistantPropertyChanged;
        _viewModel.PropertyChanged += OnAssistantPropertyChanged;
        UpdateConversationScrollMode();
        var version = ++_appearanceVersion;
        var session = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var contextVersion = session.ContextVersion;
        try
        {
            await _viewModel.LoadContextAsync();
            if (!_visible || version != _appearanceVersion || contextVersion != session.ContextVersion) return;
            if (_freeTimeDate is { } freeDate)
            {
                _freeTimeDate = null;
                await _viewModel.OpenFreeTimeForDateAsync(freeDate);
                return;
            }
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
            if (_visible && version == _appearanceVersion && contextVersion == session.ContextVersion)
                _viewModel.ErrorMessage = LocalizationResourceManager.Instance["UXAuditAssistantLoadError"];
        }
    }

    protected override void OnDisappearing()
    {
        _visible = false;
        _appearanceVersion++;
        _guidedRevealVersion++;
        _guidedRevealPending = false;
        _viewModel.PropertyChanged -= OnAssistantPropertyChanged;
        _viewModel.CancelActiveOperations();
        _viewModel.OpenAssistantCard(null);
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() =>
        _viewModel.TryAssistantBack() || base.OnBackButtonPressed();

    private void UpdateConversationScrollMode() => ConversationMessagesView.ItemsUpdatingScrollMode =
        _viewModel.HasGuidedQuestion ? ItemsUpdatingScrollMode.KeepScrollOffset : ItemsUpdatingScrollMode.KeepLastItemInView;

    private void OnAssistantPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(TravelChatViewModel.HasGuidedQuestion)
            or nameof(TravelChatViewModel.GuidedQuestionText)
            or nameof(TravelChatViewModel.ShowAssistantConversation))) return;
        var revealVersion = ++_guidedRevealVersion;
        var appearance = _appearanceVersion;
        var session = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var context = session.ContextVersion;
        Dispatcher.Dispatch(() =>
        {
            if (!_visible || appearance != _appearanceVersion || context != session.ContextVersion
                || revealVersion != _guidedRevealVersion) return;
            UpdateConversationScrollMode();
            _guidedRevealPending = _viewModel.HasGuidedQuestion && _viewModel.ShowAssistantConversation;
            _guidedRevealContextVersion = context;
            RevealPendingGuidedQuestion();
        });
    }

    private void OnConversationMessagesReady(object? sender, EventArgs e)
    {
        if (_guidedRevealPending) Dispatcher.Dispatch(RevealPendingGuidedQuestion);
    }

    private void RevealPendingGuidedQuestion()
    {
        if (!_visible || !_guidedRevealPending || !_viewModel.HasGuidedQuestion
            || !_viewModel.ShowAssistantConversation || ConversationMessagesView.Height <= 0
            || _guidedRevealContextVersion != MauiProgram.Services.GetRequiredService<AuthSessionService>().ContextVersion) return;
        // ScrollTo(0) targets the first message, after the question in Header.
        // Reveal the actual header without replacing or nesting this virtualized list.
#if ANDROID
        if (ConversationMessagesView.Handler?.PlatformView is not AndroidX.RecyclerView.Widget.RecyclerView list
            || list.GetLayoutManager() is not AndroidX.RecyclerView.Widget.LinearLayoutManager layout) return;
        list.StopScroll();
        if (ReduceMotion) layout.ScrollToPositionWithOffset(0, 0);
        else list.SmoothScrollToPosition(0);
#elif IOS || MACCATALYST
        if (ConversationMessagesView.Handler?.PlatformView is not UIKit.UICollectionView list) return;
        list.SetContentOffset(new CoreGraphics.CGPoint(list.ContentOffset.X, -list.AdjustedContentInset.Top), !ReduceMotion);
#elif WINDOWS
        if (ConversationMessagesView.Handler?.PlatformView is not Microsoft.UI.Xaml.DependencyObject root) return;
        var children = new Queue<Microsoft.UI.Xaml.DependencyObject>();
        children.Enqueue(root);
        Microsoft.UI.Xaml.Controls.ScrollViewer? scroller = null;
        while (children.TryDequeue(out var child))
        {
            if (child is Microsoft.UI.Xaml.Controls.ScrollViewer found) { scroller = found; break; }
            for (var index = 0; index < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(child); index++)
                children.Enqueue(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(child, index));
        }
        if (scroller is null) return;
        scroller.ChangeView(null, 0, null, ReduceMotion);
#else
        return;
#endif
        _guidedRevealPending = false;
    }

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

    private void OnConversationInputHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        // These controls already have their own paper border; keep the native input
        // and date selection behavior without a second underline inside that border.
        if (sender is View { Handler.PlatformView: Android.Widget.EditText input })
            input.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
#endif
    }

    private void OnWriteConversationClicked(object? sender, EventArgs e)
    {
        if (!_viewModel.IsFreeTextVisible) _viewModel.ToggleFreeTextCommand.Execute(null);
        FocusConversationInput();
    }

    private void OnConversationStarterClicked(object? sender, EventArgs e)
    {
        if (_viewModel.IsBusy || !_viewModel.ShowAssistantConversation
            || sender is not Button { CommandParameter: string question }) return;
        if (!_viewModel.IsFreeTextVisible) _viewModel.ToggleFreeTextCommand.Execute(null);
        _viewModel.MessageText = question;
        FocusConversationInput();
    }

    private void FocusConversationInput()
    {
        Dispatcher.Dispatch(() =>
        {
            if (Handler is not null && _viewModel.ShowAssistantConversation && _viewModel.IsFreeTextVisible)
                ConversationMessageInput.Focus();
        });
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
        var version = _appearanceVersion;
        var session = MauiProgram.Services.GetRequiredService<AuthSessionService>();
        var contextVersion = session.ContextVersion;
        try
        {
            view.CancelAnimations();
            view.Opacity = 0;
            view.TranslationY = 12;
            await Task.WhenAll(view.FadeToAsync(1, 220, Easing.CubicOut), view.TranslateToAsync(0, 0, 220, Easing.CubicOut));
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_visible && version == _appearanceVersion && contextVersion == session.ContextVersion
                && ReferenceEquals(view.BindingContext, card)) { view.Opacity = 1; view.TranslationY = 0; }
        }
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

using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService,
    ILocationService locationService,
    MobileBootstrapStore bootstrapStore,
    OfflineMutationQueueService mutationQueueService,
    OfflineSyncCoordinator syncCoordinator,
    PendingItineraryActionStore pendingItineraryActionStore) : ViewModelBase, ISessionStateResettable
{
    private static readonly TimeSpan TravelChatNetworkTimeout = TimeSpan.FromSeconds(20);
    private string? _conversationId;
    private string? _lastIntent;
    private string? _lastFailedMessage;
    private bool _isLocalizationSubscribed;
    private string _messageText = string.Empty;
    private DateTime _planningDate = DateTime.Today;
    private string? _city;
    private bool _hasLoadedContext;
    private string? _missingContextMessage;
    private string? _missingContextField;
    private CancellationTokenSource? _chatRequestCancellationTokenSource;
    private GuidedPlanCriteriaDto? _guidedCriteria;
    private GuidedTravelActionDto? _pendingGuidedAction;
    private TravelChatCardViewModel? _pendingReplacementCard;
    private string _guidedStep = "category";
    private string _guidedQuestionText = Resource("AssistantGuidedCategoryQuestion");
    private bool _hasGuidedQuestion = true;
    private bool _isFreeTextVisible;
    private bool _isSecondaryMenuVisible;
    private bool _isExplicitlyCancelled;
    private bool _isFullDayFlow;
    private readonly Stack<string> _guidedHistory = new();
    private readonly HashSet<string> _selectedCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _selectedBudgets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _selectedWalkingMinutes = [];

    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> SuggestedReplies { get; } =
    [
        Resource("AssistantPlanFood"),
        Resource("AssistantPlanRelax"),
        Resource("AssistantRecommendNearby"),
        Resource("AssistantViewPreferences")
    ];
    public ObservableCollection<string> MissingContextSuggestions { get; } = [];
    public ObservableCollection<TravelChatGuidedOptionViewModel> GuidedOptions { get; } =
        new(CreateCategoryOptions(includeContinue: true));
    public ObservableCollection<TravelChatGuidedOptionViewModel> SecondaryMenuOptions { get; } =
        new(CreateSecondaryMenuOptions());
    public bool CanEditItinerary => sessionService.CanEditItinerary;
    public string AssistantEyebrow => Resource("AssistantEyebrow");
    public string AssistantTitle => Resource("AssistantTitle");
    public string EmptyStateTitle => Resource("AssistantEmptyTitle");
    public string EmptyStateSubtitle => Resource("AssistantEmptySubtitle");
    public string MessagePlaceholder => Resource("AssistantMessagePlaceholder");
    public string SendButtonText => Resource("AssistantSend");
    public string GuidedQuestionText
    {
        get => _guidedQuestionText;
        private set => SetProperty(ref _guidedQuestionText, value);
    }
    public bool HasGuidedQuestion
    {
        get => _hasGuidedQuestion;
        private set => SetProperty(ref _hasGuidedQuestion, value);
    }
    public bool IsFreeTextVisible
    {
        get => _isFreeTextVisible;
        private set => SetProperty(ref _isFreeTextVisible, value);
    }
    public bool IsSecondaryMenuVisible
    {
        get => _isSecondaryMenuVisible;
        private set => SetProperty(ref _isSecondaryMenuVisible, value);
    }
    public bool CanGoBack => _guidedHistory.Count > 0;
    public bool CanRestartGuided => _guidedCriteria is not null
        || _guidedHistory.Count > 0
        || !string.Equals(_guidedStep, "category", StringComparison.Ordinal);
    public string BackText => Resource("AssistantGuidedBack");
    public string RestartText => Resource("AssistantGuidedRestart");
    public string WriteRequestText => Resource("AssistantGuidedWriteRequest");
    public string MenuText => Resource("AssistantGuidedMenu");

    public string MessageText
    {
        get => _messageText;
        set
        {
            if (SetProperty(ref _messageText, value))
            {
                SendMessageCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public DateTime PlanningDate
    {
        get => _planningDate;
        set => SetProperty(ref _planningDate, value);
    }

    public string? City
    {
        get => _city;
        set => SetProperty(ref _city, value);
    }

    public string? MissingContextMessage
    {
        get => _missingContextMessage;
        private set
        {
            if (SetProperty(ref _missingContextMessage, value))
            {
                OnPropertyChanged(nameof(HasMissingContext));
            }
        }
    }

    public string? MissingContextField
    {
        get => _missingContextField;
        private set => SetProperty(ref _missingContextField, value);
    }

    public bool HasMissingContext => !string.IsNullOrWhiteSpace(MissingContextMessage);
    public bool HasMessages => Messages.Count > 0;
    public bool ShowEmptyState => !HasMessages && !IsBusy;

    public async Task LoadContextAsync()
    {
        EnsureLocalizationSubscription();
        if (_hasLoadedContext)
        {
            var cached = await bootstrapStore.GetCachedAsync();
            if (cached is not null)
            {
                ApplyPlanningContext(cached.Value.Schedule);
                MarkLastUpdated(cached.SavedAt);
            }
            return;
        }

        await LoadAsync(async ct =>
        {
            var token = await sessionService.GetTokenAsync();
            if (string.IsNullOrWhiteSpace(token))
            {
                sessionService.Clear();
                await Shell.Current.GoToAsync("//login");
                return;
            }

            var cached = await bootstrapStore.GetCachedAsync(cancellationToken: ct);
            if (cached is not null)
            {
                ApplyPlanningContext(cached.Value.Schedule);
                MarkLastUpdated(cached.SavedAt);
                StatusMessage = null;

                // Yield after applying local state so the cached itinerary can render
                // before network synchronization starts.
                await Task.Yield();
            }

            await ReplayPendingMutationsAsync(token, ct);

            if (cached is not null)
            {
                if (bootstrapStore.HasFreshSnapshot())
                {
                    StatusMessage = null;
                    _hasLoadedContext = true;
                    return;
                }
            }

            try
            {
                var result = await bootstrapStore.RefreshResultAsync(token, cancellationToken: ct);
                if (result.IsUnauthorized)
                {
                    sessionService.Clear();
                    await Shell.Current.GoToAsync("//login");
                    return;
                }
                var bootstrap = result.Value;
                if (bootstrap is null)
                {
                    StatusMessage = cached is null
                        ? Resource("AssistantOfflineStatusNoCache")
                        : null;
                    _hasLoadedContext = true;
                    return;
                }

                ApplyPlanningContext(bootstrap.Schedule);
                MarkLastUpdated(DateTimeOffset.UtcNow);
                StatusMessage = null;
                _hasLoadedContext = true;
            }
            catch (Exception ex) when (cached is not null || IsTransientNetworkException(ex))
            {
                if (cached is null)
                {
                    StatusMessage = Resource("AssistantOfflineStatusNoCache");
                    _hasLoadedContext = true;
                    return;
                }

                StatusMessage = null;
                _hasLoadedContext = true;
            }
        });
    }

    public async Task RequestDayAlternativeAsync(DateOnly date, string? city, string? reviewSummary)
    {
        if (IsBusy)
        {
            return;
        }

        PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(city))
        {
            City = city;
        }

        _isFullDayFlow = true;
        _guidedCriteria = null;
        _pendingGuidedAction = null;
        _pendingReplacementCard = null;
        _guidedHistory.Clear();
        ClearGuidedSelections();
        MessageText = string.Empty;
        ErrorMessage = null;
        StatusMessage = null;
        ClearMissingContext();
        IsFreeTextVisible = false;
        IsSecondaryMenuVisible = false;
        var token = await sessionService.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            var profile = await apiClient.GetTravelPreferenceProfileAsync(token);
            if (TryUseSavedPreferences(profile))
            {
                await SendGuidedPlanAsync(alternative: false);
                return;
            }
        }

        ShowGuidedStep("category", Resource("AssistantGuidedCategoryQuestion"),
            CreateCategoryOptions(includeContinue: true), addHistory: false);
    }

    public async Task RequestThematicRouteAsync(DateOnly date, string? city, string theme)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(theme))
        {
            return;
        }

        PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(city))
        {
            City = city;
        }

        MessageText = $"Crea un plan de {theme.Trim()} para este día con varias paradas cercanas entre sí. Mantén todas mis reservas confirmadas y usa solamente los espacios libres.";
        IsFreeTextVisible = true;
        await SendMessageAsync();
    }

    public void ResetForNewSession()
    {
        ResetLoadState();
        _conversationId = null;
        _lastIntent = null;
        _lastFailedMessage = null;
        _hasLoadedContext = false;
        MessageText = string.Empty;
        PlanningDate = DateTime.Today;
        City = null;
        Messages.Clear();
        ResetDefaultSuggestedReplies();
        ClearMissingContext();
        RestartGuidedFlow();
        OnMessagesChanged();
    }

    [RelayCommand]
    private async Task SelectGuidedOptionAsync(TravelChatGuidedOptionViewModel? option)
    {
        if (option is null || IsBusy)
        {
            return;
        }

        var id = option.Id;
        if (id == "category.continue")
        {
            if (_selectedCategories.Count == 0) return;
            UpdateGuidedCriteriaFromSelections();
            ShowGuidedStep("budget", Resource("AssistantGuidedBudgetQuestion"), CreateBudgetOptions());
            return;
        }

        if (id.StartsWith("category.", StringComparison.Ordinal))
        {
            var category = id["category.".Length..];
            if (!GuidedTravelCategories.IsValid(category))
            {
                return;
            }

            ToggleSelection(_selectedCategories, category);
            UpdateGuidedCriteriaFromSelections();
            RefreshGuidedSelectionState();
            return;
        }

        switch (id)
        {
            case "priority.budget":
                _guidedCriteria = _guidedCriteria! with { Priority = GuidedTravelPriorities.Budget };
                ShowGuidedStep("budget", Resource("AssistantGuidedBudgetQuestion"), CreateBudgetOptions());
                return;
            case "priority.distance":
                _guidedCriteria = _guidedCriteria! with { Priority = GuidedTravelPriorities.Distance };
                ShowGuidedStep("distance", Resource("AssistantGuidedDistanceQuestion"), CreateDistanceOptions());
                return;
            case "priority.duration":
                _guidedCriteria = _guidedCriteria! with { Priority = GuidedTravelPriorities.Duration };
                ShowGuidedStep("duration", Resource("AssistantGuidedDurationQuestion"), CreateDurationOptions());
                return;
            case "priority.direct":
                _guidedCriteria = _guidedCriteria! with { Priority = GuidedTravelPriorities.Direct };
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "budget.low":
            case "budget.medium":
            case "budget.high":
                ToggleSelection(_selectedBudgets, id["budget.".Length..]);
                UpdateGuidedCriteriaFromSelections();
                RefreshGuidedSelectionState();
                return;
            case "budget.continue":
                if (_selectedBudgets.Count == 0) return;
                UpdateGuidedCriteriaFromSelections();
                ShowGuidedStep("distance", Resource("AssistantGuidedDistanceQuestion"), CreateDistanceOptions());
                return;
            case "distance.15":
            case "distance.30":
                _selectedWalkingMinutes.Remove(0);
                var minutes = int.Parse(id["distance.".Length..], CultureInfo.InvariantCulture);
                if (!_selectedWalkingMinutes.Add(minutes)) _selectedWalkingMinutes.Remove(minutes);
                UpdateGuidedCriteriaFromSelections();
                RefreshGuidedSelectionState();
                return;
            case "distance.continue":
                if (_selectedWalkingMinutes.Count == 0) return;
                UpdateGuidedCriteriaFromSelections();
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "distance.none":
                _selectedWalkingMinutes.Clear();
                _selectedWalkingMinutes.Add(0);
                UpdateGuidedCriteriaFromSelections();
                RefreshGuidedSelectionState();
                return;
            case "location.skip":
                _selectedWalkingMinutes.Clear();
                _selectedWalkingMinutes.Add(0);
                UpdateGuidedCriteriaFromSelections();
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "location.retry":
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "offline.retry":
                await SendMessageAsync();
                return;
            case "duration.60":
            case "duration.120":
                _guidedCriteria = _guidedCriteria! with { MaxDurationMinutes = int.Parse(id["duration.".Length..], CultureInfo.InvariantCulture) };
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "duration.none":
                _guidedCriteria = _guidedCriteria! with { MaxDurationMinutes = null, Priority = GuidedTravelPriorities.Direct };
                await SendGuidedPlanAsync(alternative: false);
                return;
            case "adjust.category":
                ShowGuidedStep("category", Resource("AssistantGuidedCategoryQuestion"), CreateCategoryOptions(includeContinue: true));
                return;
            case "adjust.budget":
                ShowGuidedStep("budget", Resource("AssistantGuidedBudgetQuestion"), CreateBudgetOptions());
                return;
            case "adjust.distance":
                ShowGuidedStep("distance", Resource("AssistantGuidedDistanceQuestion"), CreateDistanceOptions());
                return;
            case "adjust.duration":
                ShowGuidedStep("duration", Resource("AssistantGuidedDurationQuestion"), CreateDurationOptions());
                return;
            case "menu.schedule":
                IsSecondaryMenuVisible = false;
                await SendActionMessageAsync(Resource("AssistantViewSchedule"));
                return;
            case "menu.preferences":
                IsSecondaryMenuVisible = false;
                await SendActionMessageAsync(Resource("AssistantViewPreferences"));
                return;
            case "menu.help":
                IsSecondaryMenuVisible = false;
                await SendActionMessageAsync(Resource("AssistantHelpCapabilities"));
                return;
        }
    }

    [RelayCommand]
    private void GoBackGuided()
    {
        if (_guidedHistory.Count == 0)
        {
            return;
        }

        ShowGuidedStepById(_guidedHistory.Pop(), addHistory: false);
    }

    [RelayCommand]
    private void RestartGuided() => RestartGuidedFlow();

    [RelayCommand]
    private void ToggleFreeText()
    {
        _pendingGuidedAction = null;
        _pendingReplacementCard = null;
        IsFreeTextVisible = !IsFreeTextVisible;
    }

    [RelayCommand]
    private void ToggleSecondaryMenu() => IsSecondaryMenuVisible = !IsSecondaryMenuVisible;

    [RelayCommand]
    private void AdjustGuidedPlan()
    {
        if (_guidedCriteria is null)
        {
            RestartGuidedFlow();
            return;
        }

        ShowGuidedStep("adjust", Resource("AssistantGuidedAdjustQuestion"), CreateAdjustOptions());
    }

    [RelayCommand(CanExecute = nameof(CanSendMessage))]
    private async Task SendMessageAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var message = MessageText.Trim();
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        try
        {
            var isGuidedSubmission = _pendingGuidedAction is not null;
            var isFullDaySubmission = _pendingGuidedAction?.Action == GuidedTravelActions.FullDay;
            var replacementCard = _pendingReplacementCard;
            var isTargetedReplacement = replacementCard is not null
                && _pendingGuidedAction?.Action == GuidedTravelActions.Alternative;
            _isExplicitlyCancelled = false;
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            ClearMissingContext();
            MessageText = string.Empty;
            if (isGuidedSubmission && !isTargetedReplacement)
            {
                Messages.Clear();
                HasGuidedQuestion = false;
            }
            else
            {
                Messages.Add(new TravelChatMessageViewModel(message, isFromUser: true));
            }
            OnMessagesChanged();
            var currentLocation = (_pendingGuidedAction is not null && _guidedCriteria?.MaxWalkingMinutes is not null)
                || ShouldAttachLocation(message)
                ? await locationService.GetCurrentLocationAsync()
                : null;

            _chatRequestCancellationTokenSource?.Cancel();
            _chatRequestCancellationTokenSource?.Dispose();
            _chatRequestCancellationTokenSource = new CancellationTokenSource(TravelChatNetworkTimeout);
            var activeRequest = _chatRequestCancellationTokenSource;
            var response = await apiClient.SendTravelChatAsync(
                token,
                new TravelChatRequest(
                    message,
                    _conversationId,
                    City,
                    DateOnly.FromDateTime(PlanningDate),
                    currentLocation,
                    CultureInfo.CurrentUICulture.Name,
                    _pendingGuidedAction,
                    _pendingGuidedAction is null ? null : _guidedCriteria),
                activeRequest.Token);

            if (response is null)
            {
                await ApplyChatOfflineFallbackAsync(message);
                return;
            }

            _lastFailedMessage = null;
            if (response.TrialAccess is not null)
            {
                sessionService.ApplyTrialAccess(response.TrialAccess);
            }
            _conversationId = response.ConversationId;
            _lastIntent = response.Intent;
            _guidedCriteria = response.Criteria ?? _guidedCriteria;
            _pendingGuidedAction = null;
            _pendingReplacementCard = null;
            var cards = (response.Cards ?? [])
                .Select(card => new TravelChatCardViewModel(card))
                .ToList();
            var responseMessage = response.Message;
            if (isFullDaySubmission && response.MissingContext is null && cards.Count > 0)
            {
                var savedCount = await SaveFullDayCardsAsync(cards, token, activeRequest.Token);
                responseMessage = savedCount == cards.Count
                    ? string.Format(CultureInfo.CurrentCulture, Resource("AssistantFullDaySaved"), savedCount)
                    : string.Format(CultureInfo.CurrentCulture, Resource("AssistantFullDayPartiallySaved"), savedCount, cards.Count);
            }
            if (isTargetedReplacement && replacementCard is not null)
            {
                var replacement = cards.FirstOrDefault();
                var containingMessage = Messages.FirstOrDefault(item => item.Cards.Contains(replacementCard));
                if (replacement is not null)
                {
                    containingMessage?.ReplaceCard(replacementCard, replacement);
                }

                StatusMessage = response.Message;
            }
            else if (!DuplicatesMissingContext(response))
            {
                Messages.Add(new TravelChatMessageViewModel(responseMessage, isFromUser: false, cards));
            }
            SuggestedReplies.Clear();
            foreach (var reply in response.SuggestedReplies ?? [])
            {
                SuggestedReplies.Add(reply);
            }

            ApplyMissingContext(response.MissingContext);
            if (response.MissingContext is not null
                && !string.Equals(response.MissingContext.Field, "preferences", StringComparison.OrdinalIgnoreCase))
            {
                HasGuidedQuestion = false;
            }
            else
            {
                ApplyGuidedQuestion(response.GuidedQuestion);
            }
            if (response.GuidedQuestion is null && cards.Count > 0)
            {
                HasGuidedQuestion = false;
            }
            OnMessagesChanged();
        }
        catch (OperationCanceledException) when (_isExplicitlyCancelled)
        {
        }
        catch (Exception ex) when (IsTransientNetworkException(ex))
        {
            await ApplyChatOfflineFallbackAsync(message);
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Resource("AssistantPrepareError"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SendSuggestedReplyAsync(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply))
        {
            return;
        }

        if (IsSaveReply(reply))
        {
            await SaveItineraryItemAsync(FindLatestSaveableCard());
            return;
        }

        if (sessionService.IsBuilder
            && string.Equals(reply.Trim(), "Configurar mi viaje", StringComparison.OrdinalIgnoreCase))
        {
            await Shell.Current.GoToAsync(nameof(BuilderSetupPage));
            return;
        }

        if (string.Equals(MissingContextField, "upgrade", StringComparison.OrdinalIgnoreCase)
            || string.Equals(reply.Trim(), "Activar mi pase", StringComparison.OrdinalIgnoreCase))
        {
            await RedeemPassAsync();
            return;
        }

        if (IsRetryReply(reply))
        {
            MessageText = string.IsNullOrWhiteSpace(_lastFailedMessage)
                ? MessageText
                : _lastFailedMessage;
            await SendMessageAsync();
            return;
        }

        if (await TryHandleLocalOfflineActionAsync(reply))
        {
            return;
        }

        MessageText = reply;
        await SendMessageAsync();
    }

    [RelayCommand]
    private async Task SaveItineraryItemAsync(TravelChatCardViewModel? card)
    {
        if (card is null || !card.CanSave || !card.RecommendationId.HasValue)
        {
            StatusMessage = Resource("AssistantNoReadyPlan");
            return;
        }

        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        try
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            var recommendation = await FindRecommendationAsync(card.RecommendationId.Value, token);
            if (recommendation is null)
            {
                StatusMessage = Resource("AssistantDetailNotFound");
                return;
            }

            if (!sessionService.CanEditItinerary)
            {
                pendingItineraryActionStore.Set(recommendation, DateOnly.FromDateTime(PlanningDate), card.StartsAt);
                await PaywallNavigation.OpenAsync(PaywallEntryPoint.Assistant);
                return;
            }

            if (sessionService.RequiresTripSetup)
            {
                pendingItineraryActionStore.Set(recommendation);
                await Shell.Current.GoToAsync(nameof(BuilderSetupPage));
                return;
            }

            var parameters = new ShellNavigationQueryParameters
            {
                ["Recommendation"] = recommendation,
                ["Date"] = DateOnly.FromDateTime(PlanningDate)
            };
            if (card.StartsAt.HasValue)
            {
                parameters["SuggestedStartTime"] = card.StartsAt.Value;
            }

            await Shell.Current.GoToAsync(nameof(ItineraryItemEditorPage), parameters);
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Resource("AssistantSaveErrorWithReason"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenRecommendationDetailAsync(TravelChatCardViewModel? card)
    {
        if (card?.RecommendationId is null)
        {
            StatusMessage = Resource("AssistantDetailNotFound");
            return;
        }

        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        try
        {
            var recommendation = await FindRecommendationAsync(card.RecommendationId.Value, token);
            if (recommendation is null)
            {
                StatusMessage = Resource("AssistantDetailNotFound");
                return;
            }

            await Shell.Current.GoToAsync(
                nameof(RecommendationDetailPage),
                new Dictionary<string, object>
                {
                    ["Recommendation"] = recommendation,
                    ["IsUnlocked"] = await IsRecommendationUnlockedAsync(recommendation)
                });
        }
        catch (Exception ex)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Resource("AssistantDetailOpenError"), ex.Message);
        }
    }

    [RelayCommand]
    private Task RequestLessWalkingAsync(TravelChatCardViewModel? card)
    {
        var reference = card?.RecommendationReference;
        return string.IsNullOrWhiteSpace(reference)
            ? SendActionMessageAsync(Resource("AssistantRecommendNearby"))
            : SendActionMessageAsync($"{Resource("AssistantRecommendNearby")} {reference}");
    }

    [RelayCommand]
    private Task ReplaceRecommendationAsync(TravelChatCardViewModel? card)
    {
        if (_guidedCriteria is not null)
        {
            return SendGuidedPlanAsync(alternative: true, card);
        }

        var reference = card?.RecommendationReference;
        return string.IsNullOrWhiteSpace(reference)
            ? SendActionMessageAsync(Resource("AssistantOtherOption"))
            : SendActionMessageAsync($"{Resource("AssistantReplaceButton")} {reference}");
    }

    [RelayCommand]
    private Task AvoidTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Task.CompletedTask;
        }

        return SendActionMessageAsync($"{Resource("AssistantAvoidTagPrefix")} {tag.Trim()}");
    }

    [RelayCommand]
    private Task MarkUsefulAsync(TravelChatCardViewModel? card)
    {
        return SendFeedbackAsync(card, TravelAssistantFeedbackSignal.Helpful);
    }

    [RelayCommand]
    private Task MarkNotUsefulAsync(TravelChatCardViewModel? card)
    {
        return SendFeedbackAsync(card, TravelAssistantFeedbackSignal.NotHelpful);
    }

    [RelayCommand]
    private Task HideSimilarAsync(TravelChatCardViewModel? card)
    {
        return SendFeedbackAsync(card, TravelAssistantFeedbackSignal.HideSimilar);
    }

    private async Task SendFeedbackAsync(
        TravelChatCardViewModel? card,
        TravelAssistantFeedbackSignal signal)
    {
        if (card?.RecommendationId is null || string.IsNullOrWhiteSpace(_conversationId))
        {
            return;
        }

        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return;
        }

        try
        {
            var response = await apiClient.SendTravelAssistantFeedbackAsync(
                token,
                new TravelAssistantFeedbackRequest(
                    _conversationId,
                    card.RecommendationId.Value,
                    signal,
                    CultureInfo.CurrentUICulture.Name,
                    _lastIntent,
                    null));
            card.FeedbackStatusMessage = response?.Message ?? Resource("AssistantFeedbackError");
        }
        catch (Exception)
        {
            card.FeedbackStatusMessage = Resource("AssistantFeedbackError");
        }
    }

    private async Task SendActionMessageAsync(string message)
    {
        if (IsBusy)
        {
            return;
        }

        MessageText = message;
        await SendMessageAsync();
    }

    private async Task SendGuidedPlanAsync(
        bool alternative,
        TravelChatCardViewModel? replacementCard = null)
    {
        if (_guidedCriteria is null || !GuidedTravelCategories.IsValid(_guidedCriteria.Category))
        {
            RestartGuidedFlow();
            return;
        }

        if (_isFullDayFlow && !alternative)
        {
            await PersistGuidedPreferencesAsync();
        }

        _pendingGuidedAction = new GuidedTravelActionDto(
            alternative
                ? GuidedTravelActions.Alternative
                : _isFullDayFlow ? GuidedTravelActions.FullDay : GuidedTravelActions.Recommend,
            OptionId: !alternative && _isFullDayFlow ? Guid.NewGuid().ToString("N") : null,
            RecommendationId: replacementCard?.RecommendationId?.ToString());
        _pendingReplacementCard = alternative ? replacementCard : null;
        MessageText = alternative
            ? Resource("AssistantGuidedAnotherRequest")
            : _isFullDayFlow
                ? Resource("AssistantGuidedFullDayRequestSummary")
                : BuildGuidedRequestSummary(_guidedCriteria);
        IsFreeTextVisible = false;
        IsSecondaryMenuVisible = false;
        await SendMessageAsync();
    }

    private async Task PersistGuidedPreferencesAsync()
    {
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token) || _guidedCriteria is null) return;
        var categories = _guidedCriteria.Categories.Append(_guidedCriteria.Category)
            .Where(GuidedTravelCategories.IsValid)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (categories.Count == 0) return;

        var budgets = _guidedCriteria.Budgets.Append(_guidedCriteria.Budget)
            .Where(value => value is "low" or "medium" or "high")
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var walkingMinutes = _selectedWalkingMinutes.Contains(0)
            ? 180
            : _guidedCriteria.MaxWalkingMinutes ?? 30;
        try
        {
            await apiClient.PatchTravelPreferenceProfileAsync(
                token,
                new TravelPreferenceProfilePatchDto(
                    null, null, budgets.FirstOrDefault() ?? "medium", "balanced",
                    categories, null, null, walkingMinutes));
        }
        catch (Exception ex) when (IsTransientNetworkException(ex))
        {
            // The plan can still be generated. A later profile update may retry.
        }
    }

    private async Task<int> SaveFullDayCardsAsync(
        IReadOnlyList<TravelChatCardViewModel> cards,
        string token,
        CancellationToken cancellationToken)
    {
        if (!sessionService.CanEditItinerary || sessionService.RequiresTripSetup)
        {
            return 0;
        }

        var savedCount = 0;
        foreach (var card in cards.Take(4))
        {
            if (!card.RecommendationId.HasValue || !card.StartsAt.HasValue) continue;
            SaveItineraryItemResponse? result;
            try
            {
                result = await apiClient.SaveItineraryItemAsync(
                    token,
                    new SaveItineraryItemRequest(
                        card.RecommendationId.Value,
                        DateOnly.FromDateTime(PlanningDate),
                        card.StartsAt.Value,
                        card.EndsAt,
                        Guid.NewGuid()),
                    cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsTransientNetworkException(ex))
            {
                continue;
            }
            if (result?.Saved != true) continue;
            card.IsSaved = true;
            savedCount++;
        }

        if (savedCount > 0)
        {
            await bootstrapStore.RefreshAsync(token, cancellationToken: CancellationToken.None);
        }
        return savedCount;
    }

    private async Task ApplyChatOfflineFallbackAsync(string message)
    {
        _lastFailedMessage = message;
        MessageText = message;
        ErrorMessage = null;
        ClearMissingContext();

        var cached = await bootstrapStore.GetCachedAsync();
        var offlineMessage = cached is null
            ? Resource("AssistantOfflineFallbackNoCache")
            : string.Format(
                CultureInfo.CurrentCulture,
                Resource("AssistantOfflineFallbackWithCache"),
                OfflineCacheService.FormatSavedAt(cached.SavedAt));

        StatusMessage = cached is null
            ? Resource("AssistantOfflineStatusNoCache")
            : null;

        Messages.Add(new TravelChatMessageViewModel(offlineMessage, isFromUser: false));
        SuggestedReplies.Clear();
        SuggestedReplies.Add(Resource("AssistantRetry"));
        SuggestedReplies.Add(Resource("AssistantOpenToday"));
        SuggestedReplies.Add(Resource("AssistantOpenDiscover"));
        SuggestedReplies.Add(Resource("AssistantOpenDocs"));
        ShowGuidedStep(
            "offline",
            Resource("AssistantGuidedOfflineQuestion"),
            [new TravelChatGuidedOptionViewModel("offline.retry", Resource("AssistantRetry"))]);
        OnMessagesChanged();
    }

    private static bool IsTransientNetworkException(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or IOException;
    }

    private static bool IsRetryReply(string reply)
    {
        return string.Equals(reply.Trim(), Resource("AssistantRetry"), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<bool> TryHandleLocalOfflineActionAsync(string reply)
    {
        var normalized = NormalizeCommandText(reply);
        if (normalized == NormalizeCommandText(Resource("AssistantOpenToday")))
        {
            await Shell.Current.GoToAsync("//main/schedule");
            return true;
        }

        if (normalized == NormalizeCommandText(Resource("AssistantOpenDiscover")))
        {
            await Shell.Current.GoToAsync("//main/map");
            return true;
        }

        if (normalized == NormalizeCommandText(Resource("AssistantOpenDocs")))
        {
            await Shell.Current.GoToAsync("//main/docs");
            return true;
        }

        return false;
    }

    private async Task QueueSaveItineraryItemAsync(
        TravelChatCardViewModel card,
        string reason,
        Guid? clientMutationId = null)
    {
        if (!card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            ErrorMessage = string.Format(CultureInfo.CurrentCulture, Resource("AssistantSaveErrorWithReason"), reason);
            return;
        }

        await mutationQueueService.EnqueueSaveItineraryItemAsync(
            new SaveItineraryItemRequest(
                card.RecommendationId.Value,
                DateOnly.FromDateTime(PlanningDate),
                card.StartsAt.Value,
                card.EndsAt,
                clientMutationId));
        await syncCoordinator.PublishPendingCountAsync();
        card.IsSaved = true;
        var pendingCount = await mutationQueueService.GetPendingCountAsync();
        StatusMessage = pendingCount == 1
            ? Resource("AssistantOfflineQueuedSingle")
            : string.Format(CultureInfo.CurrentCulture, Resource("AssistantOfflineQueuedMany"), pendingCount);
    }

    private async Task ReplayPendingMutationsAsync(string token, CancellationToken cancellationToken)
    {
        var result = await syncCoordinator.SynchronizeAsync(cancellationToken);
        if (result.Total == 0)
        {
            return;
        }

        if (result.Succeeded > 0 && result.Failed == 0)
        {
            StatusMessage = result.Succeeded == 1
                ? Resource("AssistantPendingSyncedSingle")
                : string.Format(CultureInfo.CurrentCulture, Resource("AssistantPendingSyncedMany"), result.Succeeded);
            return;
        }

        if (result.Succeeded > 0)
        {
            StatusMessage = string.Format(CultureInfo.CurrentCulture, Resource("AssistantPendingPartial"), result.Succeeded, result.Failed);
        }
        else if (result.PermanentFailures > 0)
        {
            StatusMessage = Resource("AssistantPendingNeedsAttention");
        }
    }

    private async Task<RecommendationDto?> FindRecommendationAsync(Guid recommendationId, string token)
    {
        var cached = await bootstrapStore.GetCachedAsync();
        var recommendation = cached?.Value.Recommendations
            .FirstOrDefault(existing => existing.Id == recommendationId);
        if (recommendation is not null)
        {
            return recommendation;
        }

        var result = await bootstrapStore.RefreshResultAsync(token);
        if (result.IsUnauthorized)
        {
            sessionService.Clear();
            await Shell.Current.GoToAsync("//login");
            return null;
        }
        return result.Value?.Recommendations
            .FirstOrDefault(existing => existing.Id == recommendationId);
    }

    private async Task<bool> IsRecommendationUnlockedAsync(RecommendationDto recommendation)
    {
        var currentEntitlements = (await bootstrapStore.GetCachedAsync())?.Value.Entitlements;
        if (currentEntitlements is null)
        {
            return true;
        }

        return ContentAccessPolicy.IsRecommendationUnlocked(
            currentEntitlements,
            recommendation.AccessLevel,
            recommendation.DestinationId,
            recommendation.PackageIds);
    }

    private TravelChatCardViewModel? FindLatestSaveableCard()
    {
        return Messages
            .Reverse()
            .SelectMany(message => message.Cards)
            .FirstOrDefault(card => card.CanSave);
    }

    private static bool IsSaveReply(string reply)
    {
        return reply.Contains("guardar plan", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("guardar este plan", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("guardar itinerario", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("save plan", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("save itinerary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldAttachLocation(string message)
    {
        var normalized = NormalizeCommandText(message);
        if (normalized.Contains("preferencia", StringComparison.Ordinal)
            || normalized.Contains("preference", StringComparison.Ordinal)
            || normalized.Contains("perfil", StringComparison.Ordinal)
            || normalized.Contains("profile", StringComparison.Ordinal)
            || normalized is "ver mi agenda" or "ver agenda" or "mi agenda" or "agenda" or "show my schedule" or "my schedule" or "schedule"
            || normalized.Contains("que puedo pedirte", StringComparison.Ordinal)
            || normalized.Contains("what can i ask", StringComparison.Ordinal)
            || normalized is "ayuda" or "comandos" or "help")
        {
            return false;
        }

        return true;
    }

    private static string NormalizeCommandText(string value)
    {
        return string.Join(
            ' ',
            value
                .Trim()
                .ToLowerInvariant()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private void ApplyMissingContext(MissingContextDto? missingContext)
    {
        if (string.Equals(missingContext?.Field, "preferences", StringComparison.OrdinalIgnoreCase))
        {
            ClearMissingContext();
            _isFullDayFlow = false;
            _guidedCriteria = null;
            _guidedHistory.Clear();
            ShowGuidedStep(
                "category",
                Resource("AssistantGuidedCategoryQuestion"),
                CreateCategoryOptions(includeContinue: true),
                addHistory: false);
            return;
        }

        MissingContextMessage = missingContext?.Message;
        MissingContextField = missingContext?.Field;
        MissingContextSuggestions.Clear();

        if (missingContext is null)
        {
            return;
        }

        foreach (var suggestion in missingContext.Suggestions ?? [])
        {
            MissingContextSuggestions.Add(suggestion);
        }
    }

    private void ClearMissingContext()
    {
        MissingContextMessage = null;
        MissingContextField = null;
        MissingContextSuggestions.Clear();
    }

    private Task RedeemPassAsync() => PaywallNavigation.OpenAsync(TravelCompanion.Shared.Dtos.PaywallEntryPoint.Assistant);

    private bool CanSendMessage()
    {
        return !IsBusy && !string.IsNullOrWhiteSpace(MessageText);
    }

    protected override void OnLoadStateChanged()
    {
        SendMessageCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void OnMessagesChanged()
    {
        OnPropertyChanged(nameof(HasMessages));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void EnsureLocalizationSubscription()
    {
        if (_isLocalizationSubscribed)
        {
            return;
        }

        _isLocalizationSubscribed = true;
        LocalizationResourceManager.Instance.CultureChanged += OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(AssistantEyebrow));
        OnPropertyChanged(nameof(AssistantTitle));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateSubtitle));
        OnPropertyChanged(nameof(MessagePlaceholder));
        OnPropertyChanged(nameof(SendButtonText));
        OnPropertyChanged(nameof(BackText));
        OnPropertyChanged(nameof(RestartText));
        OnPropertyChanged(nameof(WriteRequestText));
        OnPropertyChanged(nameof(MenuText));
        SecondaryMenuOptions.Clear();
        foreach (var option in CreateSecondaryMenuOptions())
        {
            SecondaryMenuOptions.Add(option);
        }
        ShowGuidedStepById(_guidedStep, addHistory: false);

        if (!HasMessages)
        {
            MessageText = string.Empty;
            ResetDefaultSuggestedReplies();
        }
    }

    private void ApplyGuidedQuestion(GuidedQuestionDto? question)
    {
        if (question is null)
        {
            return;
        }

        _guidedStep = question.Id;
        GuidedQuestionText = question.Message;
        GuidedOptions.Clear();
        foreach (var option in question.Options)
        {
            GuidedOptions.Add(new TravelChatGuidedOptionViewModel(option.Id, option.Label));
        }

        HasGuidedQuestion = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanRestartGuided));
    }

    private void ApplyPlanningContext(TripScheduleDto? schedule)
    {
        var firstUsefulDay = (schedule?.Items ?? [])
            .GroupBy(item => item.Date)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .FirstOrDefault();

        if (firstUsefulDay is null)
        {
            return;
        }

        PlanningDate = firstUsefulDay.Key.ToDateTime(TimeOnly.MinValue);
        City = firstUsefulDay
            .Select(item => item.City)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    public void CancelActiveOperations()
    {
        _isExplicitlyCancelled = true;
        CancelLoading();
        _chatRequestCancellationTokenSource?.Cancel();
    }

    private void RestartGuidedFlow()
    {
        _isFullDayFlow = false;
        _guidedCriteria = null;
        _pendingGuidedAction = null;
        _pendingReplacementCard = null;
        _guidedHistory.Clear();
        ClearGuidedSelections();
        IsFreeTextVisible = false;
        IsSecondaryMenuVisible = false;
        ShowGuidedStep("category", Resource("AssistantGuidedCategoryQuestion"), CreateCategoryOptions(includeContinue: true), addHistory: false);
    }

    private void ShowGuidedStep(
        string step,
        string question,
        IReadOnlyList<TravelChatGuidedOptionViewModel> options,
        bool addHistory = true)
    {
        if (addHistory && HasGuidedQuestion && !string.Equals(_guidedStep, step, StringComparison.Ordinal))
        {
            _guidedHistory.Push(_guidedStep);
        }

        _guidedStep = step;
        GuidedQuestionText = question;
        GuidedOptions.Clear();
        foreach (var option in options)
        {
            GuidedOptions.Add(option);
        }
        RefreshGuidedSelectionState();

        HasGuidedQuestion = true;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(CanRestartGuided));
    }

    private void ShowGuidedStepById(string step, bool addHistory)
    {
        switch (step)
        {
            case "category_more":
                ShowGuidedStep(step, Resource("AssistantGuidedMoreQuestion"), CreateMoreCategoryOptions(), addHistory);
                break;
            case "priority":
                ShowGuidedStep(step, Resource("AssistantGuidedPriorityQuestion"), CreatePriorityOptions(), addHistory);
                break;
            case "budget":
                ShowGuidedStep(step, Resource("AssistantGuidedBudgetQuestion"), CreateBudgetOptions(), addHistory);
                break;
            case "distance":
                ShowGuidedStep(step, Resource("AssistantGuidedDistanceQuestion"), CreateDistanceOptions(), addHistory);
                break;
            case "duration":
                ShowGuidedStep(step, Resource("AssistantGuidedDurationQuestion"), CreateDurationOptions(), addHistory);
                break;
            case "adjust":
                ShowGuidedStep(step, Resource("AssistantGuidedAdjustQuestion"), CreateAdjustOptions(), addHistory);
                break;
            default:
                ShowGuidedStep("category", Resource("AssistantGuidedCategoryQuestion"), CreateCategoryOptions(includeContinue: true), addHistory);
                break;
        }
    }

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateCategoryOptions(bool includeContinue)
    {
        var options = new List<TravelChatGuidedOptionViewModel>
        {
            new("category.food", Resource("AssistantGuidedFood")),
            new("category.relax", Resource("AssistantGuidedRelax")),
            new("category.culture", Resource("AssistantGuidedCulture")),
            new("category.walk", Resource("AssistantGuidedWalk")),
            new("category.dance", Resource("AssistantGuidedDance")),
            new("category.nature", Resource("AssistantGuidedNature")),
            new("category.shopping", Resource("AssistantGuidedShopping")),
            new("category.viewpoint", Resource("AssistantGuidedViewpoint")),
            new("category.nightlife", Resource("AssistantGuidedNightlife"))
        };
        if (includeContinue)
        {
            options.Add(new("category.continue", Resource("CommonContinue")));
        }
        return options;
    }

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateMoreCategoryOptions() =>
    [
        new("category.nature", Resource("AssistantGuidedNature")),
        new("category.shopping", Resource("AssistantGuidedShopping")),
        new("category.viewpoint", Resource("AssistantGuidedViewpoint")),
        new("category.nightlife", Resource("AssistantGuidedNightlife"))
    ];

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreatePriorityOptions() =>
    [
        new("priority.budget", Resource("AssistantGuidedBudget")),
        new("priority.distance", Resource("AssistantGuidedDistance")),
        new("priority.duration", Resource("AssistantGuidedDuration")),
        new("priority.direct", Resource("AssistantGuidedRecommendNow"))
    ];

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateBudgetOptions() =>
    [
        new("budget.low", Resource("AssistantGuidedBudgetLow")),
        new("budget.medium", Resource("AssistantGuidedBudgetMedium")),
        new("budget.high", Resource("AssistantGuidedBudgetHigh")),
        new("budget.continue", Resource("CommonContinue"))
    ];

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateDistanceOptions() =>
    [
        new("distance.15", Resource("AssistantGuidedWalk15")),
        new("distance.30", Resource("AssistantGuidedWalk30")),
        new("distance.none", Resource("AssistantGuidedNoPreference")),
        new("distance.continue", Resource("CommonContinue"))
    ];

    private void ClearGuidedSelections()
    {
        _selectedCategories.Clear();
        _selectedBudgets.Clear();
        _selectedWalkingMinutes.Clear();
    }

    private static void ToggleSelection<T>(ISet<T> selections, T value)
    {
        if (!selections.Add(value)) selections.Remove(value);
    }

    private void UpdateGuidedCriteriaFromSelections()
    {
        var categories = _selectedCategories.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var budgets = _selectedBudgets.Order(StringComparer.OrdinalIgnoreCase).ToList();
        var walking = _selectedWalkingMinutes.Where(value => value > 0).Order().ToList();
        _guidedCriteria = new GuidedPlanCriteriaDto(
            categories.FirstOrDefault(),
            GuidedTravelPriorities.Direct,
            budgets.FirstOrDefault(),
            walking.Count == 0 ? null : walking.Max(),
            _guidedCriteria?.MaxDurationMinutes)
        {
            Categories = categories,
            Budgets = budgets,
            WalkingMinuteOptions = walking
        };
    }

    private void RefreshGuidedSelectionState()
    {
        foreach (var option in GuidedOptions)
        {
            option.IsSelected = option.Id switch
            {
                var id when id.StartsWith("category.", StringComparison.Ordinal) && id != "category.continue" =>
                    _selectedCategories.Contains(id["category.".Length..]),
                var id when id.StartsWith("budget.", StringComparison.Ordinal) && id != "budget.continue" =>
                    _selectedBudgets.Contains(id["budget.".Length..]),
                "distance.none" => _selectedWalkingMinutes.Contains(0),
                var id when id is "distance.15" or "distance.30" =>
                    _selectedWalkingMinutes.Contains(int.Parse(id["distance.".Length..], CultureInfo.InvariantCulture)),
                _ => false
            };
        }
    }

    private bool TryUseSavedPreferences(TravelPreferenceProfileDto? profile)
    {
        if (profile is null || !profile.HasMinimumPreferences || profile.Interests.Count == 0
            || profile.BudgetLevel is not ("low" or "medium" or "high")
            || profile.MaxWalkingMinutes <= 0)
        {
            return false;
        }

        foreach (var interest in profile.Interests)
        {
            var normalized = interest.Trim().ToLowerInvariant();
            var category = normalized switch
            {
                var value when value.Contains("food") || value.Contains("comida") => GuidedTravelCategories.Food,
                var value when value.Contains("relax") || value.Contains("onsen") => GuidedTravelCategories.Relax,
                var value when value.Contains("culture") || value.Contains("cultura") || value.Contains("history") => GuidedTravelCategories.Culture,
                var value when value.Contains("walk") || value.Contains("pase") => GuidedTravelCategories.Walk,
                var value when value.Contains("dance") || value.Contains("bail") => GuidedTravelCategories.Dance,
                var value when value.Contains("nature") || value.Contains("natur") || value.Contains("garden") => GuidedTravelCategories.Nature,
                var value when value.Contains("shop") || value.Contains("compra") => GuidedTravelCategories.Shopping,
                var value when value.Contains("view") || value.Contains("mirador") || value.Contains("photo") => GuidedTravelCategories.Viewpoint,
                var value when value.Contains("night") || value.Contains("noche") || value.Contains("bar") => GuidedTravelCategories.Nightlife,
                _ => null
            };
            if (category is not null) _selectedCategories.Add(category);
        }
        if (_selectedCategories.Count == 0) return false;

        _selectedBudgets.Add(profile.BudgetLevel);
        _selectedWalkingMinutes.Add(profile.MaxWalkingMinutes >= 180
            ? 0
            : profile.MaxWalkingMinutes <= 15 ? 15 : 30);
        UpdateGuidedCriteriaFromSelections();
        return true;
    }

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateDurationOptions() =>
    [
        new("duration.60", Resource("AssistantGuidedDuration60")),
        new("duration.120", Resource("AssistantGuidedDuration120")),
        new("duration.none", Resource("AssistantGuidedNoLimit"))
    ];

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateAdjustOptions() =>
    [
        new("adjust.category", Resource("AssistantGuidedAdjustCategory")),
        new("adjust.budget", Resource("AssistantGuidedBudget")),
        new("adjust.distance", Resource("AssistantGuidedDistance")),
        new("adjust.duration", Resource("AssistantGuidedDuration"))
    ];

    private static IReadOnlyList<TravelChatGuidedOptionViewModel> CreateSecondaryMenuOptions() =>
    [
        new("menu.schedule", Resource("AssistantViewSchedule")),
        new("menu.preferences", Resource("AssistantViewPreferences")),
        new("menu.help", Resource("AssistantHelpCapabilities"))
    ];

    private static string BuildGuidedRequestSummary(GuidedPlanCriteriaDto criteria)
    {
        var category = criteria.Category switch
        {
            GuidedTravelCategories.Food => Resource("AssistantGuidedFood"),
            GuidedTravelCategories.Relax => Resource("AssistantGuidedRelax"),
            GuidedTravelCategories.Culture => Resource("AssistantGuidedCulture"),
            GuidedTravelCategories.Walk => Resource("AssistantGuidedWalk"),
            GuidedTravelCategories.Dance => Resource("AssistantGuidedDance"),
            GuidedTravelCategories.Nature => Resource("AssistantGuidedNature"),
            GuidedTravelCategories.Shopping => Resource("AssistantGuidedShopping"),
            GuidedTravelCategories.Viewpoint => Resource("AssistantGuidedViewpoint"),
            GuidedTravelCategories.Nightlife => Resource("AssistantGuidedNightlife"),
            _ => string.Empty
        };
        return string.Format(CultureInfo.CurrentCulture, Resource("AssistantGuidedRequestSummary"), category);
    }

    private static bool DuplicatesMissingContext(TravelChatResponse response)
    {
        return response.MissingContext is not null
            && (response.Cards?.Count ?? 0) == 0
            && string.Equals(
                response.Message?.Trim(),
                response.MissingContext.Message?.Trim(),
                StringComparison.Ordinal);
    }

    private void ResetDefaultSuggestedReplies()
    {
        SuggestedReplies.Clear();
        SuggestedReplies.Add(Resource("AssistantPlanFood"));
        SuggestedReplies.Add(Resource("AssistantPlanRelax"));
        SuggestedReplies.Add(Resource("AssistantRecommendNearby"));
        SuggestedReplies.Add(Resource("AssistantViewPreferences"));
    }

    private static string Resource(string key)
    {
        return LocalizationResourceManager.Instance[key];
    }

}

public sealed class TravelChatGuidedOptionViewModel(string id, string label) : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    private bool _isSelected;
    public string Id { get; } = id;
    public string Label { get; } = label;
    public string DisplayLabel => IsSelected ? $"✓  {Label}" : Label;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value)) OnPropertyChanged(nameof(DisplayLabel));
        }
    }
}

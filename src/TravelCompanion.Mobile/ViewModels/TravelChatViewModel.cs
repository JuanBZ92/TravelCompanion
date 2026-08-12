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
    OfflineMutationQueueService mutationQueueService) : ViewModelBase, ISessionStateResettable
{
    private static readonly TimeSpan TravelChatNetworkTimeout = TimeSpan.FromSeconds(20);
    private string? _conversationId;
    private string? _lastIntent;
    private string? _lastFailedMessage;
    private bool _isLocalizationSubscribed;
    private string _messageText = Resource("AssistantDefaultPrompt");
    private DateTime _planningDate = DateTime.Today;
    private string? _city;
    private bool _hasLoadedContext;
    private string? _missingContextMessage;
    private string? _missingContextField;

    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> SuggestedReplies { get; } =
    [
        Resource("AssistantPlanFood"),
        Resource("AssistantPlanRelax"),
        Resource("AssistantRecommendNearby"),
        Resource("AssistantViewPreferences")
    ];
    public ObservableCollection<string> MissingContextSuggestions { get; } = [];
    public bool CanEditItinerary => sessionService.CanEditItinerary;
    public ObservableCollection<TravelChatGuideSectionViewModel> GuideSections { get; } = new(CreateGuideSections());
    public string AssistantEyebrow => Resource("AssistantEyebrow");
    public string AssistantTitle => Resource("AssistantTitle");
    public string EmptyStateTitle => Resource("AssistantEmptyTitle");
    public string EmptyStateSubtitle => Resource("AssistantEmptySubtitle");
    public string MessagePlaceholder => Resource("AssistantMessagePlaceholder");
    public string SendButtonText => Resource("AssistantSend");

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

            await ReplayPendingMutationsAsync(token, ct);
            var cached = await bootstrapStore.GetCachedAsync(cancellationToken: ct);
            if (cached is not null)
            {
                ApplyPlanningContext(cached.Value.Schedule);
                MarkLastUpdated(cached.SavedAt);

                if (bootstrapStore.HasFreshSnapshot())
                {
                    StatusMessage = null;
                    _hasLoadedContext = true;
                    return;
                }

                StatusMessage = $"Mostrando agenda guardada mientras la API responde. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
            }

            try
            {
                var bootstrap = await bootstrapStore.RefreshAsync(token, cancellationToken: ct);
                if (bootstrap is null)
                {
                    StatusMessage = cached is null
                        ? Resource("AssistantOfflineStatusNoCache")
                        : $"Render puede estar despertando. Usando agenda guardada para contextualizar el assistant. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
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

                StatusMessage = $"Render puede estar despertando. Usando agenda guardada para contextualizar el assistant. {OfflineCacheService.FormatSavedAt(cached.SavedAt)}";
                _hasLoadedContext = true;
            }
        });
    }

    public void ResetForNewSession()
    {
        ResetLoadState();
        _conversationId = null;
        _lastIntent = null;
        _lastFailedMessage = null;
        _hasLoadedContext = false;
        MessageText = Resource("AssistantDefaultPrompt");
        PlanningDate = DateTime.Today;
        City = null;
        Messages.Clear();
        ResetDefaultSuggestedReplies();
        RefreshGuideSections();
        ClearMissingContext();
        OnMessagesChanged();
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
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;
            ClearMissingContext();
            MessageText = string.Empty;
            Messages.Add(new TravelChatMessageViewModel(message, isFromUser: true));
            OnMessagesChanged();
            var currentLocation = ShouldAttachLocation(message)
                ? await locationService.GetCurrentLocationAsync()
                : null;

            using var timeout = new CancellationTokenSource(TravelChatNetworkTimeout);
            var response = await apiClient.SendTravelChatAsync(
                token,
                new TravelChatRequest(
                    message,
                    _conversationId,
                    City,
                    DateOnly.FromDateTime(PlanningDate),
                    currentLocation,
                    CultureInfo.CurrentUICulture.Name),
                timeout.Token);

            if (response is null)
            {
                await ApplyChatOfflineFallbackAsync(message);
                return;
            }

            _lastFailedMessage = null;
            _conversationId = response.ConversationId;
            _lastIntent = response.Intent;
            var cards = (response.Cards ?? [])
                .Select(card => new TravelChatCardViewModel(card))
                .ToList();
            Messages.Add(new TravelChatMessageViewModel(response.Message, isFromUser: false, cards));
            SuggestedReplies.Clear();
            foreach (var reply in response.SuggestedReplies ?? [])
            {
                SuggestedReplies.Add(reply);
            }

            ApplyMissingContext(response.MissingContext);
            OnMessagesChanged();
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
        if (!sessionService.CanEditItinerary)
        {
            StatusMessage = "Este viaje curado no se puede modificar desde la app.";
            return;
        }

        if (sessionService.RequiresTripSetup)
        {
            MissingContextMessage = "Configura las fechas y ciudades de tu viaje para guardar planes.";
            MissingContextField = "tripSetup";
            MissingContextSuggestions.Clear();
            MissingContextSuggestions.Add("Configurar mi viaje");
            return;
        }
        if (card is null || !card.CanSave || !card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            StatusMessage = Resource("AssistantNoReadyPlan");
            return;
        }

        var confirmed = await Shell.Current.DisplayAlertAsync(
            Resource("AssistantSaveTitle"),
            string.Format(CultureInfo.CurrentCulture, Resource("AssistantSaveMessage"), card.Title),
            Resource("AssistantSaveButton"),
            Resource("AssistantCancel"));
        if (!confirmed)
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
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = null;

            var saveRequest = new SaveItineraryItemRequest(
                card.RecommendationId.Value,
                DateOnly.FromDateTime(PlanningDate),
                card.StartsAt.Value,
                card.EndsAt);

            var response = await apiClient.SaveItineraryItemAsync(token, saveRequest);

            if (response?.Saved == true)
            {
                card.IsSaved = true;
                StatusMessage = response.Message;
                if (response.Item is not null)
                {
                    await bootstrapStore.UpsertScheduleItemAsync(response.Item);
                }

                return;
            }

            ErrorMessage = response?.Message ?? Resource("AssistantSaveError");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            await QueueSaveItineraryItemAsync(card, ex.Message);
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
            : string.Format(
                CultureInfo.CurrentCulture,
                Resource("AssistantOfflineStatusWithCache"),
                OfflineCacheService.FormatSavedAt(cached.SavedAt));

        Messages.Add(new TravelChatMessageViewModel(offlineMessage, isFromUser: false));
        SuggestedReplies.Clear();
        SuggestedReplies.Add(Resource("AssistantRetry"));
        SuggestedReplies.Add(Resource("AssistantOpenToday"));
        SuggestedReplies.Add(Resource("AssistantOpenDiscover"));
        SuggestedReplies.Add(Resource("AssistantOpenDocs"));
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

    private async Task QueueSaveItineraryItemAsync(TravelChatCardViewModel card, string reason)
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
                card.EndsAt));
        card.IsSaved = true;
        var pendingCount = await mutationQueueService.GetPendingCountAsync();
        StatusMessage = pendingCount == 1
            ? Resource("AssistantOfflineQueuedSingle")
            : string.Format(CultureInfo.CurrentCulture, Resource("AssistantOfflineQueuedMany"), pendingCount);
    }

    private async Task ReplayPendingMutationsAsync(string token, CancellationToken cancellationToken)
    {
        var result = await mutationQueueService.ReplayPendingAsync(token, cancellationToken);
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

        var refreshed = await bootstrapStore.RefreshAsync(token);
        return refreshed?.Recommendations
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
        RefreshGuideSections();

        if (!HasMessages)
        {
            MessageText = Resource("AssistantDefaultPrompt");
            ResetDefaultSuggestedReplies();
        }
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

    private void ResetDefaultSuggestedReplies()
    {
        SuggestedReplies.Clear();
        SuggestedReplies.Add(Resource("AssistantPlanFood"));
        SuggestedReplies.Add(Resource("AssistantPlanRelax"));
        SuggestedReplies.Add(Resource("AssistantRecommendNearby"));
        SuggestedReplies.Add(Resource("AssistantViewPreferences"));
    }

    private void RefreshGuideSections()
    {
        GuideSections.Clear();
        foreach (var section in CreateGuideSections())
        {
            GuideSections.Add(section);
        }
    }

    private static string Resource(string key)
    {
        return LocalizationResourceManager.Instance[key];
    }

    private static IReadOnlyList<TravelChatGuideSectionViewModel> CreateGuideSections()
    {
        return
        [
            new TravelChatGuideSectionViewModel(
                "1",
                Resource("AssistantGuidePlan"),
                [
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanFood"), Resource("AssistantPromptFood")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanRelax"), Resource("AssistantPromptRelax")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanWalk"), Resource("AssistantPromptWalk")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanCouple"), Resource("AssistantPromptCouple")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanNight"), Resource("AssistantPromptNight")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanDance"), Resource("AssistantPromptDance")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPlanDate"), Resource("AssistantPromptDate"))
                ]),
            new TravelChatGuideSectionViewModel(
                "2",
                Resource("AssistantGuideAdjust"),
                [
                    new TravelChatGuideActionViewModel(Resource("AssistantRecommendNearby"), Resource("AssistantPromptNearby")),
                    new TravelChatGuideActionViewModel(Resource("AssistantRecommendDuration"), Resource("AssistantPromptDuration")),
                    new TravelChatGuideActionViewModel(Resource("AssistantOtherOption"), Resource("AssistantPromptOther"))
                ]),
            new TravelChatGuideSectionViewModel(
                "3",
                Resource("AssistantGuideSchedule"),
                [
                    new TravelChatGuideActionViewModel(Resource("AssistantViewSchedule"), Resource("AssistantViewSchedule")),
                    new TravelChatGuideActionViewModel(Resource("AssistantTomorrow"), Resource("AssistantPromptTomorrow"))
                ]),
            new TravelChatGuideSectionViewModel(
                "4",
                Resource("AssistantGuidePreferences"),
                [
                    new TravelChatGuideActionViewModel(Resource("AssistantViewPreferences"), Resource("AssistantViewPreferences")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPromptAvoidCulture"), Resource("AssistantPromptAvoidCulture")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPromptLowBudget"), Resource("AssistantPromptLowBudget"))
                ]),
            new TravelChatGuideSectionViewModel(
                "5",
                Resource("AssistantGuideHelp"),
                [
                    new TravelChatGuideActionViewModel(Resource("AssistantHelpCapabilities"), Resource("AssistantHelpCapabilities")),
                    new TravelChatGuideActionViewModel(Resource("AssistantPromptCommands"), Resource("AssistantPromptCommands"))
                ])
        ];
    }
}

public sealed class TravelChatGuideSectionViewModel(
    string number,
    string title,
    IReadOnlyList<TravelChatGuideActionViewModel> actions)
{
    public string Number { get; } = number;
    public string Title { get; } = title;
    public string Header => $"{Number}. {Title}";
    public IReadOnlyList<TravelChatGuideActionViewModel> Actions { get; } = actions;
}

public sealed class TravelChatGuideActionViewModel(string title, string prompt)
{
    public string Title { get; } = title;
    public string Prompt { get; } = prompt;
}

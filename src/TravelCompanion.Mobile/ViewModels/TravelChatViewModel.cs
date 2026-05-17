using System.Collections.ObjectModel;
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
    MobileBootstrapStore bootstrapStore) : ViewModelBase, ISessionStateResettable
{
    private string? _conversationId;
    private string _messageText = "Proponeme un plan entre mis reservas de hoy";
    private DateTime _planningDate = DateTime.Today;
    private string? _city;
    private bool _hasLoadedContext;
    private string? _missingContextMessage;
    private string? _missingContextField;

    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> SuggestedReplies { get; } =
    [
        "Proponeme un plan entre mis reservas de hoy",
        "Algo con menos caminata",
        "Ver mis preferencias"
    ];
    public ObservableCollection<string> MissingContextSuggestions { get; } = [];

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

            var schedule = await apiClient.GetScheduleAsync(token, ct);
            var firstUsefulDay = (schedule?.Items ?? [])
                .GroupBy(item => item.Date)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .FirstOrDefault();

            if (firstUsefulDay is not null)
            {
                PlanningDate = firstUsefulDay.Key.ToDateTime(TimeOnly.MinValue);
                City = firstUsefulDay
                    .Select(item => item.City)
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            }

            _hasLoadedContext = true;
        });
    }

    public void ResetForNewSession()
    {
        ResetLoadState();
        _conversationId = null;
        _hasLoadedContext = false;
        MessageText = "Proponeme un plan entre mis reservas de hoy";
        PlanningDate = DateTime.Today;
        City = null;
        Messages.Clear();
        SuggestedReplies.Clear();
        SuggestedReplies.Add("Proponeme un plan entre mis reservas de hoy");
        SuggestedReplies.Add("Algo con menos caminata");
        SuggestedReplies.Add("Ver mis preferencias");
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
            var currentLocation = await locationService.GetCurrentLocationAsync();

            var response = await apiClient.SendTravelChatAsync(
                token,
                new TravelChatRequest(
                    message,
                    _conversationId,
                    City,
                    DateOnly.FromDateTime(PlanningDate),
                    currentLocation,
                    "es-ES"));

            if (response is null)
            {
                ErrorMessage = "No pude enviar el mensaje. Intenta nuevamente.";
                return;
            }

            _conversationId = response.ConversationId;
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
        catch (Exception ex)
        {
            ErrorMessage = $"No pude preparar el plan: {ex.Message}";
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

        if (IsScheduleReply(reply))
        {
            await Shell.Current.GoToAsync("//main/schedule");
            return;
        }

        MessageText = reply;
        await SendMessageAsync();
    }

    [RelayCommand]
    private async Task SaveItineraryItemAsync(TravelChatCardViewModel? card)
    {
        if (card is null || !card.CanSave || !card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            StatusMessage = "No encontre un plan listo para guardar.";
            return;
        }

        var confirmed = await Shell.Current.DisplayAlertAsync(
            "Guardar plan",
            $"Guardar \"{card.Title}\" en tu itinerario?",
            "Guardar",
            "Cancelar");
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

            var response = await apiClient.SaveItineraryItemAsync(
                token,
                new SaveItineraryItemRequest(
                    card.RecommendationId.Value,
                    DateOnly.FromDateTime(PlanningDate),
                    card.StartsAt.Value,
                    card.EndsAt));

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

            ErrorMessage = response?.Message ?? "No pude guardar el plan. Intenta nuevamente.";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"No pude guardar el plan: {ex.Message}";
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
            StatusMessage = "No encontre el detalle de esa recomendacion.";
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
                StatusMessage = "No encontre el detalle de esa recomendacion.";
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
            ErrorMessage = $"No pude abrir el detalle: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task RequestLessWalkingAsync(TravelChatCardViewModel? card)
    {
        return SendActionMessageAsync("Algo con menos caminata");
    }

    [RelayCommand]
    private Task ReplaceRecommendationAsync(TravelChatCardViewModel? card)
    {
        return SendActionMessageAsync("Otra alternativa");
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
        return reply.Contains("guardar", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("save", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScheduleReply(string reply)
    {
        return reply.Contains("agenda", StringComparison.OrdinalIgnoreCase)
            || reply.Contains("schedule", StringComparison.OrdinalIgnoreCase);
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
}

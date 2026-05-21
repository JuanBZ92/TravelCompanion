using System.Collections.ObjectModel;
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
    private string? _conversationId;
    private string _messageText = "Plan para comer";
    private DateTime _planningDate = DateTime.Today;
    private string? _city;
    private bool _hasLoadedContext;
    private string? _missingContextMessage;
    private string? _missingContextField;

    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> SuggestedReplies { get; } =
    [
        "Plan para comer",
        "Plan para relajar",
        "Recomendar por cercania",
        "Ver mis preferencias"
    ];
    public ObservableCollection<string> MissingContextSuggestions { get; } = [];
    public IReadOnlyList<TravelChatGuideSectionViewModel> GuideSections { get; } = CreateGuideSections();

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

            await ReplayPendingMutationsAsync(token, ct);
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
        MessageText = "Plan para comer";
        PlanningDate = DateTime.Today;
        City = null;
        Messages.Clear();
        SuggestedReplies.Clear();
        SuggestedReplies.Add("Plan para comer");
        SuggestedReplies.Add("Plan para relajar");
        SuggestedReplies.Add("Recomendar por cercania");
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
            var currentLocation = ShouldAttachLocation(message)
                ? await locationService.GetCurrentLocationAsync()
                : null;

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

            ErrorMessage = response?.Message ?? "No pude guardar el plan. Intenta nuevamente.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            await QueueSaveItineraryItemAsync(card, ex.Message);
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
        var reference = card?.RecommendationReference;
        return string.IsNullOrWhiteSpace(reference)
            ? SendActionMessageAsync("Recomendar por cercania")
            : SendActionMessageAsync($"Recomendar por cercania teniendo en cuenta {reference}");
    }

    [RelayCommand]
    private Task ReplaceRecommendationAsync(TravelChatCardViewModel? card)
    {
        var reference = card?.RecommendationReference;
        return string.IsNullOrWhiteSpace(reference)
            ? SendActionMessageAsync("Otra alternativa")
            : SendActionMessageAsync($"Reemplazar {reference}");
    }

    [RelayCommand]
    private Task AvoidTagAsync(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return Task.CompletedTask;
        }

        return SendActionMessageAsync($"Evitar {tag.Trim()}");
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

    private async Task QueueSaveItineraryItemAsync(TravelChatCardViewModel card, string reason)
    {
        if (!card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            ErrorMessage = $"No pude guardar el plan: {reason}";
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
            ? "Sin conexion estable. Deje este plan en cola y lo voy a sincronizar cuando vuelva la red."
            : $"Sin conexion estable. Hay {pendingCount} cambios en cola para sincronizar.";
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
                ? "Sincronice 1 cambio pendiente."
                : $"Sincronice {result.Succeeded} cambios pendientes.";
            return;
        }

        if (result.Succeeded > 0)
        {
            StatusMessage = $"Sincronice {result.Succeeded} cambios; quedan {result.Failed} pendientes.";
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

    private static bool IsScheduleReply(string reply)
    {
        var normalized = NormalizeCommandText(reply);
        return normalized is "ver mi agenda"
            or "ver agenda"
            or "mi agenda"
            or "agenda"
            or "schedule"
            or "ver schedule";
    }

    private static bool ShouldAttachLocation(string message)
    {
        var normalized = NormalizeCommandText(message);
        if (normalized.Contains("preferencia", StringComparison.Ordinal)
            || normalized.Contains("perfil", StringComparison.Ordinal)
            || normalized is "ver mi agenda" or "ver agenda" or "mi agenda" or "agenda"
            || normalized.Contains("que puedo pedirte", StringComparison.Ordinal)
            || normalized is "ayuda" or "comandos")
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

    private static IReadOnlyList<TravelChatGuideSectionViewModel> CreateGuideSections()
    {
        return
        [
            new TravelChatGuideSectionViewModel(
                "1",
                "Planificar",
                [
                    new TravelChatGuideActionViewModel("Plan para comer", "Proponeme un plan para comer teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan para relajar", "Proponeme un plan para relajar teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan para caminar", "Recomendar plan para caminar teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan en pareja", "Recomendar plan para pareja teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan nocturno", "Recomendar plan nocturno teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan para bailar", "Recomendar plan para bailar teniendo en cuenta mi agenda"),
                    new TravelChatGuideActionViewModel("Plan por fecha", "Proponeme planes para 2026-10-08 teniendo en cuenta mi agenda")
                ]),
            new TravelChatGuideSectionViewModel(
                "2",
                "Ajustar",
                [
                    new TravelChatGuideActionViewModel("Por cercania", "Recomendar por cercania teniendo en cuenta el pedido inicial"),
                    new TravelChatGuideActionViewModel("Por duracion", "Recomendar por duracion teniendo en cuenta el pedido inicial"),
                    new TravelChatGuideActionViewModel("Otra opcion", "Otra alternativa teniendo en cuenta el pedido inicial")
                ]),
            new TravelChatGuideSectionViewModel(
                "3",
                "Agenda",
                [
                    new TravelChatGuideActionViewModel("Ver agenda", "Ver mi agenda"),
                    new TravelChatGuideActionViewModel("Mañana", "Proponeme planes para mañana")
                ]),
            new TravelChatGuideSectionViewModel(
                "4",
                "Preferencias",
                [
                    new TravelChatGuideActionViewModel("Ver perfil", "Ver mis preferencias"),
                    new TravelChatGuideActionViewModel("Evitar #culture", "Evitar #culture"),
                    new TravelChatGuideActionViewModel("Presupuesto bajo", "Presupuesto bajo")
                ]),
            new TravelChatGuideSectionViewModel(
                "5",
                "Ayuda",
                [
                    new TravelChatGuideActionViewModel("Que puedo pedirte", "Que puedo pedirte"),
                    new TravelChatGuideActionViewModel("Comandos", "Ayuda")
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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel(
    TravelCompanionApiClient apiClient,
    AuthSessionService sessionService) : ViewModelBase, ISessionStateResettable
{
    private string? _conversationId;
    private string _messageText = "Proponeme un plan entre mis reservas de hoy";
    private DateTime _planningDate = DateTime.Today;
    private string? _city;
    private bool _hasLoadedContext;

    public ObservableCollection<TravelChatMessageViewModel> Messages { get; } = [];
    public ObservableCollection<string> SuggestedReplies { get; } =
    [
        "Proponeme un plan entre mis reservas de hoy",
        "Algo con menos caminata"
    ];

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
            MessageText = string.Empty;
            Messages.Add(new TravelChatMessageViewModel(message, isFromUser: true));
            OnMessagesChanged();

            var response = await apiClient.SendTravelChatAsync(
                token,
                new TravelChatRequest(
                    message,
                    _conversationId,
                    City,
                    DateOnly.FromDateTime(PlanningDate),
                    null,
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

            StatusMessage = response.MissingContext?.Message;
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

        MessageText = reply;
        await SendMessageAsync();
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

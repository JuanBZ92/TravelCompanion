using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel
{
    [RelayCommand]
    private Task FindCloserDayStopAsync(TravelChatCardViewModel? card) => card is null
        ? Task.CompletedTask : ChangeDayStopAsync(card, closer: true);

    private async Task ChangeDayStopAsync(TravelChatCardViewModel card, bool closer)
    {
        if (IsBusy) return;
        if (card.PlanningDate is { } date) PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        if (!sessionService.CanEditItinerary)
        {
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today);
            return;
        }
        if (!card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            card.FeedbackStatusMessage = Resource("AssistantNoReadyPlan");
            return;
        }
        DayPlanChoice? choice;
        if (closer) choice = new([], "closer", null);
        else
        {
            var page = new DayPlanChoicePage([card], batch: false);
            await Shell.Current.Navigation.PushModalAsync(page);
            choice = await page.Result;
        }
        if (choice is not null)
            await RunDayPlanAsync(DayPlanCardActions.CreateAlternative(card,
                Messages.SelectMany(message => message.Cards), choice.Distance, choice.Budget), card);
    }

    private static GuidedTravelActionDto CreateDayAction(DayPlanChoice choice) =>
        new(GuidedTravelActions.FullDay, Guid.NewGuid().ToString("N"))
        {
            ReplaceReservationIds = choice.ReservationIds,
            DistanceAdjustment = choice.Distance,
            BudgetAdjustment = choice.Budget
        };

    private async Task RunDayPlanAsync(GuidedTravelActionDto action, TravelChatCardViewModel? replacementTarget = null)
    {
        if (IsBusy) return;
        if (!sessionService.CanEditItinerary)
        {
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today);
            return;
        }
        var token = await sessionService.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token)) return;
        var planningDate = DateOnly.FromDateTime(PlanningDate);
        List<TravelChatCardViewModel>? completeDay = null;
        try
        {
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = Resource("AssistantFullDayPreparing");
            if (replacementTarget is not null)
            {
                replacementTarget.IsSearchingAlternative = true;
                replacementTarget.FeedbackStatusMessage = Resource("AssistantSearchingAlternative");
            }
            HasGuidedQuestion = false;
            _chatRequestCancellationTokenSource?.Cancel();
            _chatRequestCancellationTokenSource?.Dispose();
            _chatRequestCancellationTokenSource = new CancellationTokenSource(TravelChatNetworkTimeout);
            var cancellationToken = _chatRequestCancellationTokenSource.Token;
            var response = await apiClient.SendTravelChatAsync(token, new TravelChatRequest(
                Resource("AssistantGuidedFullDayRequestSummary"), _conversationId, City,
                planningDate, null, CultureInfo.CurrentUICulture.Name,
                action, OperationId: Guid.NewGuid()), cancellationToken);
            if (response is null)
            {
                ErrorMessage = Resource("PlanningTryAgain");
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = ErrorMessage;
                return;
            }
            if (response.TrialAccess is not null) sessionService.ApplyTrialAccess(response.TrialAccess);
            _conversationId = response.ConversationId;
            if (response.MissingContext?.Field == "upgrade")
            {
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = response.Message;
                await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today);
                return;
            }
            var cards = response.Cards.Select(item => new TravelChatCardViewModel(item) { PlanningDate = planningDate }).ToList();
            StatusMessage = response.Message;
            if (replacementTarget is not null)
            {
                if (cards.Count == 1 && response.MissingContext is null && response.Intent == "day_plan")
                {
                    var owner = Messages.FirstOrDefault(message => message.Cards.Contains(replacementTarget));
                    cards[0].FeedbackStatusMessage = Resource("AssistantAlternativeReady");
                    owner?.ReplaceCard(replacementTarget, cards[0]);
                    OnMessagesChanged();
                }
                else replacementTarget.FeedbackStatusMessage = response.Message;
                return;
            }
            if (response.Intent == "day_complete")
            {
                foreach (var card in cards) card.IsSaved = true;
                Messages.Clear();
                Messages.Add(new TravelChatMessageViewModel(response.Message, false, cards));
                OnMessagesChanged();
                completeDay = cards;
                return;
            }
            if (cards.Count == 0) return;
            var pendingMessage = new TravelChatMessageViewModel(string.Empty, false, cards);
            Messages.Add(pendingMessage);
            OnMessagesChanged();
            StatusMessage = response.Message;
            SuggestedReplies.Clear();
            OnMessagesChanged();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Resource("PlanningTryAgain");
            if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = StatusMessage;
        }
        catch (Exception)
        {
            ErrorMessage = Resource("PlanningTryAgain");
            if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = ErrorMessage;
        }
        finally
        {
            IsBusy = false;
            if (replacementTarget is not null) replacementTarget.IsSearchingAlternative = false;
            if (completeDay is not null)
            {
                var page = new DayPlanChoicePage(completeDay, batch: true);
                await Shell.Current.Navigation.PushModalAsync(page);
                var choice = await page.Result;
                if (choice is not null) await RunDayPlanAsync(CreateDayAction(choice));
            }
        }
    }
}

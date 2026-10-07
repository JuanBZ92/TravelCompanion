using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using TravelCompanion.Mobile.Pages;
using TravelCompanion.Mobile.Services;
using TravelCompanion.Shared;
using TravelCompanion.Shared.Dtos;

namespace TravelCompanion.Mobile.ViewModels;

public sealed partial class TravelChatViewModel
{
    private async Task ChangeDayStopAsync(TravelChatCardViewModel card)
    {
        if (IsBusy) return;
        if (card.PlanningDate is { } date) PlanningDate = date.ToDateTime(TimeOnly.MinValue);
        if (!sessionService.CanEditItinerary)
        {
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
            return;
        }
        if (!card.RecommendationId.HasValue || !card.StartsAt.HasValue)
        {
            card.FeedbackStatusMessage = Resource("AssistantNoReadyPlan");
            return;
        }
        var page = new DayPlanChoicePage([card], batch: false);
        var contextVersion = sessionService.ContextVersion;
        var selectedDate = PlanningDate.Date;
        await Shell.Current.Navigation.PushModalAsync(page);
        var choice = await page.Result;
        if (choice is not null && contextVersion == sessionService.ContextVersion && selectedDate == PlanningDate.Date)
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

    private async Task RunDayPlanAsync(GuidedTravelActionDto action,
        TravelChatCardViewModel? replacementTarget = null, GuidedPlanCriteriaDto? criteria = null,
        Guid? operationId = null)
    {
        if (IsBusy) return;
        if (!sessionService.CanEditItinerary)
        {
            await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
            return;
        }
        var scope = BeginAssistantOperation();
        var planningDate = scope.Date;
        var city = City;
        var locale = CultureInfo.CurrentUICulture.Name;
        List<TravelChatCardViewModel>? completeDay = null;
        try
        {
            var token = await scope.AwaitAsync(_ => sessionService.GetTokenAsync());
            if (string.IsNullOrWhiteSpace(token)) return;
            IsBusy = true;
            ErrorMessage = null;
            StatusMessage = Resource("AssistantFullDayPreparing");
            if (replacementTarget is not null)
            {
                replacementTarget.IsSearchingAlternative = true;
                replacementTarget.FeedbackStatusMessage = Resource("AssistantSearchingAlternative");
            }
            HasGuidedQuestion = false;
            scope.SetNetworkTimeout(TravelChatNetworkTimeout);
            var response = await scope.AwaitAsync(ct => apiClient.SendTravelChatAsync(token, new TravelChatRequest(
                Resource("AssistantGuidedFullDayRequestSummary"), _conversationId, city,
                planningDate, null, locale,
                action, Criteria: criteria, OperationId: operationId ?? Guid.NewGuid()), ct));
            if (response is null)
            {
                ErrorMessage = Resource("PlanningTryAgain");
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = ErrorMessage;
                return;
            }
            if (response.TrialAccess is not null)
            {
                sessionService.ApplyTrialAccess(response.TrialAccess);
                RefreshAssistantContext();
            }
            _conversationId = response.ConversationId;
            if (response.MissingContext?.Field == "upgrade")
            {
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = response.Message;
                if (action.PlanningMode == "personalized" && criteria is not null)
                    pendingItineraryActionStore.SetPersonalization(planningDate, City, criteria);
                await PaywallNavigation.OpenAsync(PaywallEntryPoint.Today, limitReached: true);
                return;
            }
            if (response.MissingContext?.Field == "stale")
            {
                ErrorMessage = response.Message;
                _adaptationRevision = null;
                return;
            }
            var cards = response.Cards.Select(item => new TravelChatCardViewModel(item) { PlanningDate = planningDate }).ToList();
            if (action.AdaptationReason is not null || action.PlanningMode == "personalized"
                || replacementTarget is not null && _adaptationCards.Contains(replacementTarget))
                foreach (var card in cards) _adaptationCards.Add(card);
            if (action.PlanningMode == "personalized" && cards.Count > 0)
            {
                _personalizationRequestKey = null;
                foreach (var card in cards) _personalizedCards.Add(card);
                await analytics.TrackAsync("personalization_proposal_generated", "assistant", tripId: scope.TripId);
            }
            else if (action.AdaptationReason is null && replacementTarget is null
                && cards.Count > 0 && response.Intent == "day_plan")
                await analytics.TrackAsync("day_improvement_proposal_generated", "assistant", tripId: scope.TripId);
            scope.Verify();
            StatusMessage = response.Message;
            if (replacementTarget is not null)
            {
                if (cards.Count == 1 && response.MissingContext is null && response.Intent == "day_plan")
                {
                    var owner = Messages.FirstOrDefault(message => message.Cards.Contains(replacementTarget));
                    cards[0].FeedbackStatusMessage = Resource("AssistantAlternativeReady");
                    owner?.ReplaceCard(replacementTarget, cards[0]);
                    OnMessagesChanged();
                    if (owner is not null) await ShowAssistantProposalAsync(owner.Cards.ToList(), response.Message, operation: scope);
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
                await ShowAssistantProposalAsync(cards, response.Message, operation: scope);
                return;
            }
            if (cards.Count == 0)
            {
                if (response.MissingContext is null)
                    await ShowAssistantProposalAsync(cards, response.Message, operation: scope);
                return;
            }
            var pendingMessage = new TravelChatMessageViewModel(string.Empty, false, cards);
            Messages.Add(pendingMessage);
            OnMessagesChanged();
            StatusMessage = response.Message;
            SuggestedReplies.Clear();
            OnMessagesChanged();
            await ShowAssistantProposalAsync(cards, response.Message, operation: scope);
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_assistantOperation, scope) && scope.HasCurrentContext)
            {
                StatusMessage = Resource(scope.ExplicitlyCancelled ? "AssistantRequestCancelled" : "PlanningTryAgain");
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = StatusMessage;
            }
        }
        catch (Exception)
        {
            if (ReferenceEquals(_assistantOperation, scope) && scope.HasCurrentContext)
            {
                ErrorMessage = Resource("PlanningTryAgain");
                if (replacementTarget is not null) replacementTarget.FeedbackStatusMessage = ErrorMessage;
            }
        }
        finally
        {
            if (ReferenceEquals(_assistantOperation, scope))
            {
                _assistantOperation = null;
                IsBusy = false;
            }
            if (replacementTarget is not null) replacementTarget.IsSearchingAlternative = false;
            scope.Dispose();
            if (completeDay is not null && scope.CanPublish)
            {
                var page = new DayPlanChoicePage(completeDay, batch: true);
                await Shell.Current.Navigation.PushModalAsync(page);
                var choice = await page.Result;
                if (choice is not null && scope.CanPublish) await RunDayPlanAsync(CreateDayAction(choice));
            }
        }
    }
}
